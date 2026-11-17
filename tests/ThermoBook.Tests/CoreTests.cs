using ThermoBook.Services;

namespace ThermoBook.Tests;

public class GpuAggregatorTests
{
    private const string Luid = "luid_0x00000000_0x00012ADC_phys_0";

    [Fact]
    public void Sums_processes_on_the_same_engine_and_takes_max_per_type()
    {
        var usage = GpuAggregator.Aggregate(
        [
            ($"pid_100_{Luid}_eng_0_engtype_3D", 10),
            ($"pid_200_{Luid}_eng_0_engtype_3D", 15),   // mesmo motor: soma 25
            ($"pid_300_{Luid}_eng_1_engtype_3D", 20),   // outro motor 3D: 20
            ($"pid_400_{Luid}_eng_2_engtype_VideoDecode", 60),
            ($"pid_400_{Luid}_eng_3_engtype_VideoProcessing", 30),
        ]);

        Assert.Equal(25, usage.ThreeD);
        Assert.Equal(60, usage.Video);
        Assert.Equal(60, usage.All);
    }

    [Fact]
    public void Caps_at_100_and_ignores_unknown_names()
    {
        var usage = GpuAggregator.Aggregate(
        [
            ($"pid_1_{Luid}_eng_0_engtype_3D", 80),
            ($"pid_2_{Luid}_eng_0_engtype_3D", 70),
            ("_Total", 999),
        ]);

        Assert.Equal(100, usage.All);
        Assert.Equal(0, usage.Video);
    }

    [Fact]
    public void Empty_input_is_zero()
    {
        Assert.Equal(new GpuAggregator.GpuUsage(0, 0, 0), GpuAggregator.Aggregate([]));
    }
}

public class PowerModeTests
{
    [Theory]
    [InlineData("Silencioso", "Economia")]
    [InlineData("Normal", "Eficiência")]
    [InlineData("Frio", "Frio")]
    [InlineData("Máximo", "Máximo")]
    [InlineData(null, null)]
    public void ModeByName_migra_nomes_antigos(string? name, string? expected)
    {
        Assert.Equal(expected, PowerService.ModeByName(name)?.Name);
    }

    [Fact]
    public void Catalogo_tem_os_6_modos_na_ordem()
    {
        Assert.Equal(
            new[] { "Economia", "Frio", "Eficiência", "Equilibrado", "Alto", "Máximo" },
            PowerService.Modes.Select(m => m.Name));
    }

    [Fact]
    public void Cada_modo_tem_combinacao_propria_e_Match_devolve_ele_mesmo()
    {
        var keys = PowerService.Modes.Select(m => (m.Turbo, m.CpuMax, m.Epp)).ToList();
        Assert.Equal(keys.Count, keys.Distinct().Count());
        foreach (var m in PowerService.Modes)
            Assert.Same(m, PowerService.Match(m.Turbo, m.CpuMax, m.Epp));
    }

    [Fact]
    public void Combinacao_sem_modo_nao_casa()
    {
        Assert.Null(PowerService.Match(2, 100, null));
    }

    [Fact]
    public void Cada_modo_casa_tambem_com_a_CPU_minima()
    {
        foreach (var m in PowerService.Modes)
            Assert.Same(m, PowerService.Match(m.Turbo, m.CpuMax, m.Epp, m.CpuMinAc));
    }

    [Fact]
    public void Alto_nao_casa_quando_CPU_minima_e_5()
    {
        var alto = PowerService.ModeByName("Alto")!;
        Assert.Null(PowerService.Match(alto.Turbo, alto.CpuMax, alto.Epp, 5));
    }

    [Fact]
    public void EPP_diferente_vira_Personalizado()
    {
        Assert.Equal("Equilibrado", PowerService.Match(1, 100, 50, 5)?.Name);
        Assert.Null(PowerService.Match(1, 100, 51, 5));
    }
}

public class SettingsStoreTests
{
    private static string TempFile() => Path.Combine(Path.GetTempPath(), "thermobook-tests", Guid.NewGuid() + ".json");

    [Fact]
    public void Round_trip()
    {
        var store = new SettingsStore(TempFile());
        store.Save(new Settings(Profile: "Frio"));
        Assert.Equal("Frio", store.Load().Profile);
    }

    [Fact]
    public void Missing_file_gives_defaults()
    {
        Assert.Null(new SettingsStore(TempFile()).Load().Profile);
    }

    [Fact]
    public void Corrupt_file_gives_defaults()
    {
        var path = TempFile();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "{ isto nao e json");
        Assert.Null(new SettingsStore(path).Load().Profile);
    }

    [Fact]
    public void Alert_fields_round_trip()
    {
        var store = new SettingsStore(TempFile());
        store.Save(new Settings(Profile: null, AlertCelsius: 85, AlertSeconds: 10, AutoCool: true));
        Assert.Equal(new Settings(null, 85, 10, true), store.Load());
    }

    [Fact]
    public void File_from_older_version_gets_alert_defaults()
    {
        var path = TempFile();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, """{ "Profile": "Frio" }""");
        Assert.Equal(new Settings("Frio", 90, 30, false), new SettingsStore(path).Load());
    }
}

public class TempAlertTests
{
    private static readonly DateTime T0 = new(2026, 1, 1, 12, 0, 0);

    [Fact]
    public void Fires_once_after_staying_above_limit()
    {
        var alert = new TempAlert(90, TimeSpan.FromSeconds(30));
        Assert.False(alert.Check(95, T0));
        Assert.False(alert.Check(95, T0.AddSeconds(29)));
        Assert.True(alert.Check(95, T0.AddSeconds(30)));
        Assert.False(alert.Check(95, T0.AddSeconds(60)));
    }

    [Fact]
    public void Dip_below_limit_restarts_the_count()
    {
        var alert = new TempAlert(90, TimeSpan.FromSeconds(30));
        alert.Check(95, T0);
        alert.Check(90, T0.AddSeconds(20)); // igual ao limite não conta como "passou"
        Assert.False(alert.Check(95, T0.AddSeconds(31)));
        Assert.True(alert.Check(95, T0.AddSeconds(61)));
    }

    [Fact]
    public void Rearms_only_after_cooling_5_degrees()
    {
        var alert = new TempAlert(90, TimeSpan.FromSeconds(30));
        alert.Check(95, T0);
        Assert.True(alert.Check(95, T0.AddSeconds(30)));

        alert.Check(87, T0.AddSeconds(40)); // esfriou pouco: continua desarmado
        alert.Check(95, T0.AddSeconds(50));
        Assert.False(alert.Check(95, T0.AddSeconds(90)));

        alert.Check(84, T0.AddSeconds(100)); // 6 °C abaixo: rearma
        alert.Check(95, T0.AddSeconds(110));
        Assert.True(alert.Check(95, T0.AddSeconds(140)));
    }
}

public class StartupServiceTests
{
    [Fact]
    public void Enable_and_disable_in_a_test_key()
    {
        string key = $@"Software\ThermoBookTests\{Guid.NewGuid()}";
        var startup = new StartupService(key);
        try
        {
            Assert.False(startup.IsEnabled);
            startup.Set(true, @"C:\Apps\ThermoBook.exe");
            Assert.True(startup.IsEnabled);
            using (var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(key))
                Assert.Equal("\"C:\\Apps\\ThermoBook.exe\" --bandeja", k!.GetValue("ThermoBook"));
            startup.Set(false, @"C:\Apps\ThermoBook.exe");
            Assert.False(startup.IsEnabled);
            startup.Set(false, @"C:\Apps\ThermoBook.exe"); // desligar de novo não dá erro
        }
        finally
        {
            Microsoft.Win32.Registry.CurrentUser.DeleteSubKeyTree(@"Software\ThermoBookTests", throwOnMissingSubKey: false);
        }
    }
}

public class MainFormTests
{
    [Theory]
    [InlineData(512.0 * 1024, "512 KB")]
    [InlineData(1.5 * 1024 * 1024, "1,5 MB")]
    [InlineData(31.94 * 1024 * 1024 * 1024, "31,9 GB")]
    public void Formats_bytes(double bytes, string expected)
    {
        System.Globalization.CultureInfo.CurrentCulture = new("pt-BR");
        Assert.Equal(expected, MainForm.Bytes(bytes));
    }

    [Theory]
    [InlineData(true, true, -1, "Na tomada, carregando")]
    [InlineData(true, false, -1, "Na tomada")]
    [InlineData(false, false, 2 * 3600 + 5 * 60, "Na bateria, ~2h05 restantes")]
    [InlineData(false, false, -1, "Na bateria")]
    public void Battery_text(bool plugged, bool charging, int secondsLeft, string expected)
    {
        Assert.Equal(expected, MainForm.BatteryText(plugged, charging, secondsLeft));
    }

    [Theory]
    [InlineData(60, "ok")]
    [InlineData(75, "quente")]
    [InlineData(90, "quente demais")]
    public void Temp_state_uses_the_icon_thresholds(double celsius, string expected)
    {
        Assert.Equal(expected, MainForm.TempState(celsius));
    }

    [Fact]
    public void Builds_and_takes_more_than_a_minute_of_readings()
    {
        using var form = new MainForm(_ => { }, (_, _) => { }, openFan: null);
        var s = new Snapshot(12, 2.1, 90, 5, 4, 1, 2e9, 8e9, 32e9, 3, 1e6, 2e6, 500e9, [55, 52]);
        for (int i = 0; i < 70; i++) form.Push(s);
        form.ShowPower(0, 100, current: null, syncControls: true);
        form.ShowPower(7, 42, current: null, syncControls: true); // valores fora da lista e da faixa do TrackBar não quebram
    }

    [Fact]
    public void Close_from_another_program_only_hides()
    {
        // Regressão: WM_CLOSE sem SC_CLOSE chega como TaskManagerClosing; o painel era descartado e o "abrir" seguinte derrubava o app.
        using var form = new MainForm(_ => { }, (_, _) => { }, openFan: null);
        form.Show();
        SendMessage(form.Handle, 0x0010 /* WM_CLOSE */, 0, 0);
        Assert.False(form.IsDisposed);
        Assert.False(form.Visible);
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam);
}

public class TrayIconRendererTests
{
    [Theory]
    [InlineData(55, 90, 210, 120)]
    [InlineData(80, 255, 165, 60)]
    [InlineData(95, 255, 85, 85)]
    public void Color_by_temperature(double celsius, int r, int g, int b)
    {
        var c = TrayIconRenderer.TempColor(celsius);
        Assert.Equal((r, g, b), (c.R, c.G, c.B));
    }

    [Theory]
    [InlineData(null)]
    [InlineData(54.0)]
    [InlineData(105.0)]
    public void Renders_icon_of_requested_size(double? celsius)
    {
        using var icon = TrayIconRenderer.Render(celsius, 24);
        Assert.Equal(24, icon.Width);
    }

    [Theory]
    [InlineData(16)]
    [InlineData(24)]
    [InlineData(32)]
    public void Both_digits_are_drawn_without_wrapping(int size)
    {
        // Regressão: o texto quebrava de linha e só o primeiro dígito aparecia.
        using var icon = TrayIconRenderer.Render(57, size);
        using var bmp = icon.ToBitmap();
        int Colored(int x0, int x1)
        {
            int n = 0;
            for (int x = x0; x < x1; x++)
                for (int y = 0; y < size; y++)
                    if (bmp.GetPixel(x, y).G > 120) n++;
            return n;
        }
        Assert.True(Colored(0, size / 4) > 0, "primeiro dígito ausente");
        Assert.True(Colored(size * 3 / 4, size) > 0, "segundo dígito ausente");
    }
}

public class SensorServiceTests
{
    [Fact]
    public void Reads_real_counters_from_this_machine()
    {
        var s = SensorService.Read();
        Assert.True(s.RamTotal > 0);
        Assert.InRange(s.CpuUse, 0, 100);
        Assert.InRange(s.DiskActive, 0, 100);
    }
}

public class PowerServiceTests
{
    [Theory]
    [InlineData(2u, true)]
    [InlineData(0x57u, true)]
    [InlineData(5u, false)] // acesso negado não é "ausente"
    public void IsMissing_classifies_errors(uint err, bool expected) =>
        Assert.Equal(expected, PowerService.IsMissing(err));

    [Fact]
    public void Missing_setting_is_ignored_without_throwing()
    {
        var bogus = Guid.Parse("11111111-2222-3333-4444-555555555555");
        Assert.Null(PowerService.Read(PowerService.SubProcessor, bogus));
        Assert.False(PowerService.Write(PowerService.SubProcessor, bogus, 1, 1));
    }

    [Fact]
    public void Write_then_Read_round_trips_and_restores()
    {
        var old = PowerService.Read(PowerService.SubProcessor, PowerService.ProcThrottleMax);
        if (old is null) return; // máquina sem a configuração
        try
        {
            Assert.True(PowerService.Write(PowerService.SubProcessor, PowerService.ProcThrottleMax, 77, 66));
            Assert.Equal((77u, 66u), PowerService.Read(PowerService.SubProcessor, PowerService.ProcThrottleMax));
        }
        finally { PowerService.Write(PowerService.SubProcessor, PowerService.ProcThrottleMax, old.Value.Ac, old.Value.Dc); }
    }

    public static IEnumerable<object[]> AllSettings() =>
    [
        [PowerService.SubProcessor, PowerService.ProcThrottleMin], [PowerService.SubProcessor, PowerService.ProcThrottleMax],
        [PowerService.SubProcessor, PowerService.PerfBoostMode], [PowerService.SubProcessor, PowerService.PerfEpp],
        [PowerService.SubProcessor, PowerService.PerfEpp1], [PowerService.SubProcessor, PowerService.CpMinCores],
        [PowerService.SubProcessor, PowerService.CpMaxCores], [PowerService.SubProcessor, PowerService.CpIncreaseTime],
        [PowerService.SubProcessor, PowerService.CpDecreaseTime], [PowerService.SubProcessor, PowerService.PerfIncreaseThreshold],
        [PowerService.SubProcessor, PowerService.PerfDecreaseThreshold], [PowerService.SubDisk, PowerService.DiskIdle],
    ];

    [Theory]
    [MemberData(nameof(AllSettings))]
    public void Each_guid_reads_and_rewrites_same_value(Guid sub, Guid setting)
    {
        var v = PowerService.Read(sub, setting);
        if (v is null) return; // ausente neste hardware: ignorado
        Assert.True(PowerService.Write(sub, setting, v.Value.Ac, v.Value.Dc));
        Assert.Equal(v, PowerService.Read(sub, setting));
    }

    [Fact]
    public void Overlay_can_be_read() => _ = PowerService.GetOverlay();
}

public class PowerDiagnosticsTests
{
    private static readonly PowerMode Equilibrado = PowerService.ModeByName("Equilibrado")!;

    // Valor que o modo grava em cada configuração da tabela de diagnóstico.
    private static (uint, uint)? Esperado(PowerMode m, Guid setting)
    {
        var valores = new Dictionary<Guid, (uint, uint)>
        {
            [PowerService.ProcThrottleMin] = (m.CpuMinAc, m.CpuMinDc),
            [PowerService.ProcThrottleMax] = (m.CpuMax, m.CpuMax),
            [PowerService.PerfBoostMode] = (m.Turbo, m.Turbo),
            [PowerService.PerfEpp] = (m.Epp, m.Epp),
            [PowerService.CpMinCores] = (m.CoresMinAc, m.CoresMinDc),
            [PowerService.DiskIdle] = (m.DiskIdleSeconds, m.DiskIdleSeconds),
        };
        return valores.TryGetValue(setting, out var v) ? v : null;
    }

    [Fact]
    public void Valores_iguais_ao_modo_ficam_ok()
    {
        var rows = PowerDiagnostics.Rows(Equilibrado, (_, setting) => Esperado(Equilibrado, setting));
        Assert.Equal(6, rows.Count);
        Assert.All(rows, r => Assert.True(r.Ok));
    }

    [Fact]
    public void Configuracao_ausente_fica_null_e_aparece_no_texto()
    {
        var rows = PowerDiagnostics.Rows(Equilibrado, (_, _) => null);
        Assert.All(rows, r => Assert.Null(r.Ok));
        Assert.Contains("ausente no hardware", PowerDiagnostics.Format(Equilibrado, rows, Equilibrado.Overlay));
    }

    [Fact]
    public void So_a_linha_diferente_fica_marcada()
    {
        var rows = PowerDiagnostics.Rows(Equilibrado,
            (_, setting) => setting == PowerService.ProcThrottleMax ? (0u, 0u) : Esperado(Equilibrado, setting));
        var diferentes = rows.Where(r => r.Ok == false).ToList();
        Assert.Single(diferentes);
        Assert.Equal("CPU máxima (%)", diferentes[0].Name);
        Assert.Equal(5, rows.Count(r => r.Ok == true));

        var texto = PowerDiagnostics.Format(Equilibrado, rows, Equilibrado.Overlay);
        Assert.Contains("DIFERENTE", texto);
        Assert.Contains("DIFERENTE", texto.Split('\n').Single(l => l.StartsWith("CPU máxima")));
    }

    [Fact]
    public void Overlay_diferente_e_marcado_e_igual_fica_ok()
    {
        var rows = PowerDiagnostics.Rows(Equilibrado, (_, setting) => Esperado(Equilibrado, setting));
        Assert.Contains("(DIFERENTE)", PowerDiagnostics.Format(Equilibrado, rows, PowerService.OverlayMaxPerformance));

        var igual = PowerDiagnostics.Format(Equilibrado, rows, Equilibrado.Overlay);
        Assert.Contains("(ok)", igual);
        Assert.DoesNotContain("(DIFERENTE)", igual);
    }
}
