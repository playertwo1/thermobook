using System.Drawing;
using System.Windows.Forms;
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

public class HotkeyTests
{
    [Theory]
    [InlineData("Ctrl+Alt+Shift+D", 7u, 'D')]
    [InlineData("ctrl+alt+d", 3u, 'D')]
    [InlineData("Win+F12", 8u, 0x7Bu)]
    [InlineData(" Shift + 5 ", 4u, '5')]
    public void Aceita_texto_valido(string text, uint mods, uint vk)
    {
        Assert.True(HotkeyService.TryParse(text, out uint m, out uint k));
        Assert.Equal((mods, vk), (m, k));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("D")]
    [InlineData("Ctrl+")]
    [InlineData("Ctrl+Foo")]
    [InlineData("Ctrl+F25")]
    [InlineData("Banana+D")]
    [InlineData("Ctrl+Alt+Shift+Win")]
    public void Rejeita_texto_invalido(string? text) =>
        Assert.False(HotkeyService.TryParse(text, out _, out _));

    [Fact]
    public void Padrao_e_Ctrl_Alt_Shift_D_e_e_valido()
    {
        Assert.Equal("Ctrl+Alt+Shift+D", new Settings().Hotkey);
        Assert.True(HotkeyService.TryParse(new Settings().Hotkey, out _, out _));
    }

    [Fact]
    public void Settings_antigo_sem_Hotkey_usa_o_padrao()
    {
        var s = System.Text.Json.JsonSerializer.Deserialize<Settings>("""{ "Profile": "Frio" }""")!;
        Assert.Equal("Ctrl+Alt+Shift+D", s.Hotkey);
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

public class PillTests
{
    private static Snapshot Snap(double cpu, double gpu, double ramUsed, double temp) =>
        new(cpu, 2.1, 90, gpu, 0, 0, 0, ramUsed, 100, 0, 0, 0, 0, [temp]);

    [Fact]
    public void Arredonda_os_valores_mostrados()
    {
        var a = PillState.From(Snap(cpu: 12.6, gpu: 2.4, ramUsed: 60.6, temp: 54.4));
        Assert.Equal(new PillState(54, 13, 2, 61), a);

        // Valores que arredondam igual geram estado igual: a pílula não redesenha.
        var b = PillState.From(Snap(cpu: 12.9, gpu: 1.6, ramUsed: 60.9, temp: 54.2));
        Assert.True(a == b);
    }

    [Fact]
    public void Estilos_da_janela_nao_roubam_foco_nem_aparecem_na_barra()
    {
        using var form = new PillForm();
        _ = form.Handle;
        int ex = GetWindowLongW(form.Handle, -20 /* GWL_EXSTYLE */);
        Assert.NotEqual(0, ex & 0x80 /* WS_EX_TOOLWINDOW */);
        Assert.NotEqual(0, ex & 0x08000000 /* WS_EX_NOACTIVATE */);
        Assert.False(form.ShowInTaskbar);
        Assert.True(form.TopMost);
    }

    [Fact]
    public void Push_repetido_nao_quebra_a_janela()
    {
        using var form = new PillForm();
        _ = form.Handle;
        var s = Snap(cpu: 12.6, gpu: 2.4, ramUsed: 60.6, temp: 54.4);
        form.Push(s);
        form.Push(s);
        Assert.False(form.IsDisposed);
        Assert.True(form.IsHandleCreated);
    }

    [Theory]
    [InlineData(Orientation.Vertical, 1f)]
    [InlineData(Orientation.Vertical, 1.25f)]
    [InlineData(Orientation.Vertical, 1.5f)]
    [InlineData(Orientation.Vertical, 2f)]
    [InlineData(Orientation.Horizontal, 1f)]
    [InlineData(Orientation.Horizontal, 1.25f)]
    [InlineData(Orientation.Horizontal, 1.5f)]
    [InlineData(Orientation.Horizontal, 2f)]
    public void Linhas_nao_se_sobrepoem_e_cabem_na_janela(Orientation o, float scale)
    {
        var (size, items) = PillLayout.Compute(o, scale);
        var bounds = new Rectangle(Point.Empty, size);
        Assert.Equal(PillLayout.ItemCount, items.Length);
        for (int i = 0; i < items.Length; i++)
        {
            var it = items[i];
            Assert.True(bounds.Contains(it.Row), $"linha {i} fora da janela");
            Assert.True(it.Row.Contains(it.Value), $"valor {i} fora da linha");
            Assert.Equal(i is PillLayout.Cpu or PillLayout.Gpu or PillLayout.Ram, it.Meter is not null);
            if (it.Meter is { } m)
            {
                Assert.True(it.Row.Contains(m), $"medidor {i} fora da linha");
                Assert.False(m.IntersectsWith(it.Value), $"medidor {i} sobrepõe o valor");
            }
            for (int j = i + 1; j < items.Length; j++)
                Assert.False(it.Row.IntersectsWith(items[j].Row), $"linhas {i} e {j} se sobrepõem");
        }
    }

    [Fact]
    public void Vertical_tem_36_px_de_largura_e_linhas_de_metrica_de_28()
    {
        var (size, items) = PillLayout.Compute(Orientation.Vertical, 1f);
        Assert.Equal(36, size.Width);
        foreach (var i in new[] { PillLayout.Cpu, PillLayout.Gpu, PillLayout.Ram })
            Assert.Equal(28, items[i].Row.Height);
    }

    [Fact]
    public void Horizontal_tem_36_px_de_altura()
    {
        var (size, _) = PillLayout.Compute(Orientation.Horizontal, 1f);
        Assert.Equal(36, size.Height);
    }

    [Fact]
    public void Indisponivel_vira_null_no_estado()
    {
        var semTemp = PillState.From(Snap(cpu: 12, gpu: 2, ramUsed: 60, temp: 54) with { Temps = [] });
        Assert.Null(semTemp.Temp);
        var semRam = PillState.From(Snap(cpu: 12, gpu: 2, ramUsed: 60, temp: 54) with { RamTotal = 0 });
        Assert.Null(semRam.Ram);
        var semCpu = PillState.From(Snap(cpu: double.NaN, gpu: 2, ramUsed: 60, temp: 54));
        Assert.Null(semCpu.Cpu);
    }

    [Fact]
    public void Nome_acessivel_de_cada_linha()
    {
        var st = new PillState(54, 13, 2, 61);
        Assert.Equal("Temperatura: 54 graus", PillForm.RowName(PillLayout.Temp, st, null, false));
        Assert.Equal("Processador: 13 por cento", PillForm.RowName(PillLayout.Cpu, st, null, false));
        Assert.Equal("Memória RAM: indisponível", PillForm.RowName(PillLayout.Ram, st with { Ram = null }, null, false));
        Assert.Equal("Modo de energia: Personalizado", PillForm.RowName(PillLayout.Mode, st, null, false));
        Assert.Equal("Modo de energia: Frio, segundo plano ligado",
            PillForm.RowName(PillLayout.Mode, st, PowerService.ModeByName("Frio"), true));
    }

    [Fact]
    public void Janela_acompanha_a_orientacao()
    {
        using var form = new PillForm();
        _ = form.Handle;
        float k = form.DeviceDpi / 96f;
        form.PillOrientation = Orientation.Horizontal;
        Assert.Equal(PillLayout.Compute(Orientation.Horizontal, k).Size, form.ClientSize);
        form.PillOrientation = Orientation.Vertical;
        Assert.Equal(PillLayout.Compute(Orientation.Vertical, k).Size, form.ClientSize);
    }

    [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
    internal static extern int GetWindowLongW(IntPtr hwnd, int index);
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

public class BackgroundModeTests
{
    private static readonly Guid P1 = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid P2 = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid P3 = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid Lid = PowerService.LidAction;
    private static readonly Guid Standby = PowerService.StandbyIdle;
    private static readonly Guid Hibernate = PowerService.HibernateIdle;

    private static readonly (Guid Plan, Guid Setting, uint Ac, uint Dc)[] ValoresP1 =
    [
        (P1, Lid, 1, 2), (P1, Standby, 900, 300), (P1, Hibernate, 3600, 1800),
    ];

    private static readonly (Guid Plan, Guid Setting, uint Ac, uint Dc)[] ValoresP2 =
    [
        (P2, Lid, 4, 4), (P2, Standby, 120, 60), (P2, Hibernate, 1800, 900),
    ];

    /// <summary>Sistema falso: leitura e gravação num dicionário. Configuração ausente devolve null / false, como no hardware.</summary>
    private sealed class FakeSystem
    {
        public readonly Dictionary<(Guid Plan, Guid Setting), (uint Ac, uint Dc)> Values = new();
        public readonly List<(Settings Settings, Dictionary<(Guid Plan, Guid Setting), (uint Ac, uint Dc)> Estado)> Saves = new();

        public (uint Ac, uint Dc)? Read(Guid plan, Guid sub, Guid setting) =>
            Values.TryGetValue((plan, setting), out var v) ? v : null;

        public bool Write(Guid plan, Guid sub, Guid setting, uint ac, uint dc)
        {
            if (!Values.ContainsKey((plan, setting))) return false;
            Values[(plan, setting)] = (ac, dc);
            return true;
        }

        public void Save(Settings s)
        {
            var estado = new Dictionary<(Guid Plan, Guid Setting), (uint Ac, uint Dc)>(Values);
            Saves.Add((s, estado));
        }
    }

    private static FakeSystem Sistema(params (Guid Plan, Guid Setting, uint Ac, uint Dc)[] valores)
    {
        var sys = new FakeSystem();
        foreach (var (plan, setting, ac, dc) in valores) sys.Values[(plan, setting)] = (ac, dc);
        return sys;
    }

    private static void AssertPlano(FakeSystem sys, Guid plan, (uint Ac, uint Dc) lid, (uint Ac, uint Dc) standby, (uint Ac, uint Dc) hibernate)
    {
        Assert.Equal(lid, sys.Values[(plan, Lid)]);
        Assert.Equal(standby, sys.Values[(plan, Standby)]);
        Assert.Equal(hibernate, sys.Values[(plan, Hibernate)]);
    }

    [Fact]
    public void Ligar_e_desligar_devolve_o_original()
    {
        var sys = Sistema(ValoresP1);
        var s = BackgroundMode.Enable(new Settings(), P1, sys.Read, sys.Write, sys.Save);
        AssertPlano(sys, P1, (0, 0), (0, 0), (0, 0));
        Assert.True(BackgroundMode.IsOn(s));

        s = BackgroundMode.Disable(s, sys.Write, sys.Save);
        AssertPlano(sys, P1, (1, 2), (900, 300), (3600, 1800));
        Assert.False(BackgroundMode.IsOn(s));
        Assert.Null(s.BackgroundPlans);
    }

    [Fact]
    public void Snapshot_e_salvo_antes_de_gravar()
    {
        var sys = Sistema(ValoresP1);
        BackgroundMode.Enable(new Settings(), P1, sys.Read, sys.Write, sys.Save);

        var (salvo, estado) = sys.Saves[0];
        Assert.True(BackgroundMode.IsOn(salvo));
        Assert.Equal((1u, 2u), estado[(P1, Lid)]);
        Assert.Equal((900u, 300u), estado[(P1, Standby)]);
        Assert.Equal((3600u, 1800u), estado[(P1, Hibernate)]);
    }

    [Fact]
    public void Ligar_duas_vezes_nao_sobrescreve_o_snapshot()
    {
        var sys = Sistema(ValoresP1);
        var s1 = BackgroundMode.Enable(new Settings(), P1, sys.Read, sys.Write, sys.Save);
        var s2 = BackgroundMode.Enable(s1, P1, sys.Read, sys.Write, sys.Save);
        Assert.Equal(new PairValue(1, 2), s2.BackgroundPlans![P1.ToString()]["lid"]);

        BackgroundMode.Disable(s2, sys.Write, sys.Save);
        AssertPlano(sys, P1, (1, 2), (900, 300), (3600, 1800));
    }

    [Fact]
    public void Dois_planos_sao_restaurados()
    {
        var sys = Sistema([.. ValoresP1, .. ValoresP2]);
        var s = BackgroundMode.Enable(new Settings(), P1, sys.Read, sys.Write, sys.Save);
        s = BackgroundMode.Enable(s, P2, sys.Read, sys.Write, sys.Save);
        Assert.Equal(2, s.BackgroundPlans!.Count);

        BackgroundMode.Disable(s, sys.Write, sys.Save);
        AssertPlano(sys, P1, (1, 2), (900, 300), (3600, 1800));
        AssertPlano(sys, P2, (4, 4), (120, 60), (1800, 900));
    }

    [Fact]
    public void Configuracao_ausente_no_hardware_e_ignorada()
    {
        // P3 não tem a chave de tampa (lid) neste hardware.
        var sys = Sistema((P3, Standby, 900, 300), (P3, Hibernate, 3600, 1800));
        var s = BackgroundMode.Enable(new Settings(), P3, sys.Read, sys.Write, sys.Save);
        Assert.False(s.BackgroundPlans![P3.ToString()].ContainsKey("lid"));
        Assert.Equal(2, s.BackgroundPlans[P3.ToString()].Count);

        s = BackgroundMode.Disable(s, sys.Write, sys.Save);
        Assert.Null(s.BackgroundPlans);
        Assert.False(sys.Values.ContainsKey((P3, Lid)));
        Assert.Equal((900u, 300u), sys.Values[(P3, Standby)]);
    }

    [Fact]
    public void Settings_com_snapshot_sobrevive_a_JSON()
    {
        var s = new Settings("Frio", BackgroundPlans: new Dictionary<string, Dictionary<string, PairValue>>
        {
            [P1.ToString()] = new() { ["lid"] = new(1, 2), ["standby"] = new(900, 300), ["hibernate"] = new(3600, 1800) },
        });

        var json = System.Text.Json.JsonSerializer.Serialize(s);
        var volta = System.Text.Json.JsonSerializer.Deserialize<Settings>(json)!;

        Assert.True(BackgroundMode.IsOn(volta));
        var plano = volta.BackgroundPlans![P1.ToString()];
        Assert.Equal(new PairValue(1, 2), plano["lid"]);
        Assert.Equal(new PairValue(900, 300), plano["standby"]);
        Assert.Equal(new PairValue(3600, 1800), plano["hibernate"]);
        Assert.Equal("Frio", volta.Profile);

        var antigo = System.Text.Json.JsonSerializer.Deserialize<Settings>("""{ "Profile": "Frio" }""")!;
        Assert.False(BackgroundMode.IsOn(antigo));
    }
}

public class PillDockTests
{
    private static readonly Rectangle Area = new(0, 0, 1920, 1040);
    private static readonly Size VerticalSize = new(36, 132);
    private static readonly Size HorizontalSize = new(152, 36);
    private const int Limiar = 24;
    private const string Primaria = "A|1920x1080";
    private const string Externa = "B|2560x1440";

    private static DockResult Solta(int x, int y, Orientation atual) => PillDock.Snap(
        new Point(x, y), atual == Orientation.Vertical ? VerticalSize : HorizontalSize, atual,
        VerticalSize, HorizontalSize, Area, Limiar);

    [Theory]
    [InlineData(10, 500, Orientation.Vertical, 0, 500, Orientation.Vertical)]           // esquerda
    [InlineData(1874, 500, Orientation.Vertical, 1884, 500, Orientation.Vertical)]      // direita
    [InlineData(900, 10, Orientation.Horizontal, 900, 0, Orientation.Horizontal)]       // topo
    [InlineData(900, 994, Orientation.Horizontal, 900, 1004, Orientation.Horizontal)]   // base
    public void Encaixa_em_cada_borda(int x, int y, Orientation atual, int ex, int ey, Orientation eo)
    {
        Assert.Equal(new DockResult(new Point(ex, ey), eo), Solta(x, y, atual));
    }

    [Theory]
    [InlineData(5, 10, Orientation.Vertical, 0, 0, Orientation.Vertical)]                // sup. esq.: lateral mais perto
    [InlineData(10, 3, Orientation.Vertical, 0, 0, Orientation.Horizontal)]              // sup. esq.: topo mais perto
    [InlineData(5, 5, Orientation.Vertical, 0, 0, Orientation.Vertical)]                 // empate: vertical
    [InlineData(1874, 15, Orientation.Vertical, 1884, 0, Orientation.Vertical)]          // sup. dir.: lateral mais perto
    [InlineData(1874, 3, Orientation.Vertical, 1768, 0, Orientation.Horizontal)]         // sup. dir.: topo mais perto
    [InlineData(10, 893, Orientation.Vertical, 0, 908, Orientation.Vertical)]            // inf. esq.: lateral mais perto
    [InlineData(10, 905, Orientation.Vertical, 0, 1004, Orientation.Horizontal)]         // inf. esq.: base mais perto
    [InlineData(1874, 893, Orientation.Vertical, 1884, 908, Orientation.Vertical)]       // inf. dir.: lateral mais perto
    [InlineData(1874, 905, Orientation.Vertical, 1768, 1004, Orientation.Horizontal)]    // inf. dir.: base mais perto
    public void Encaixa_no_canto_com_o_tamanho_da_orientacao_final(int x, int y, Orientation atual, int ex, int ey, Orientation eo)
    {
        var r = Solta(x, y, atual);
        Assert.Equal(new DockResult(new Point(ex, ey), eo), r);

        var size = eo == Orientation.Vertical ? VerticalSize : HorizontalSize;
        Assert.True(r.Location.X == 0 || r.Location.X + size.Width == Area.Right, "não encosta na lateral");
        Assert.True(r.Location.Y == 0 || r.Location.Y + size.Height == Area.Bottom, "não encosta em topo/base");
    }

    [Theory]
    [InlineData(Orientation.Vertical)]
    [InlineData(Orientation.Horizontal)]
    public void Meio_da_tela_nao_encaixa_e_mantem_a_orientacao(Orientation atual)
    {
        Assert.Equal(new DockResult(new Point(900, 500), atual), Solta(900, 500, atual));
    }

    [Fact]
    public void Solta_fora_da_area_encaixa_ou_fica_dentro()
    {
        Assert.Equal(new DockResult(new Point(0, 500), Orientation.Vertical), Solta(-50, 500, Orientation.Vertical));
        Assert.Equal(new DockResult(new Point(900, 1004), Orientation.Horizontal), Solta(900, 2000, Orientation.Horizontal));
    }

    [Theory]
    [InlineData(24, 0)]   // dentro do limite: encaixa
    [InlineData(25, 25)]  // fora do limite: mantém
    public void Limiar_de_24_px_na_borda_esquerda(int x, int esperadoX)
    {
        Assert.Equal(new DockResult(new Point(esperadoX, 500), Orientation.Vertical), Solta(x, 500, Orientation.Vertical));
    }

    [Fact]
    public void Limiar_de_24_px_vale_para_topo_e_direita()
    {
        Assert.Equal(new DockResult(new Point(900, 0), Orientation.Horizontal), Solta(900, 24, Orientation.Horizontal));
        Assert.Equal(new DockResult(new Point(900, 25), Orientation.Horizontal), Solta(900, 25, Orientation.Horizontal));
        Assert.Equal(new DockResult(new Point(1884, 500), Orientation.Vertical), Solta(1860, 500, Orientation.Vertical));
        Assert.Equal(new DockResult(new Point(1859, 500), Orientation.Vertical), Solta(1859, 500, Orientation.Vertical));
    }

    [Fact]
    public void Resolve_usa_a_posicao_salva_do_monitor()
    {
        var telas = new[] { (Key: Primaria, Area: Area) };
        var posicoes = new Dictionary<string, PillPosition> { [Primaria] = new(300, 400, true) };

        var (monitor, r) = PillDock.Resolve(Primaria, posicoes, telas, Primaria, VerticalSize, HorizontalSize);

        Assert.Equal(Primaria, monitor);
        Assert.Equal(new DockResult(new Point(300, 400), Orientation.Horizontal), r);
    }

    [Fact]
    public void Resolve_limita_posicao_salva_fora_da_area()
    {
        var telas = new[] { (Key: Primaria, Area: Area) };
        var posicoes = new Dictionary<string, PillPosition> { [Primaria] = new(5000, -300, false) };

        var (_, r) = PillDock.Resolve(Primaria, posicoes, telas, Primaria, VerticalSize, HorizontalSize);

        Assert.Equal(new DockResult(new Point(Area.Right - VerticalSize.Width, 0), Orientation.Vertical), r);
    }

    [Fact]
    public void Monitor_salvo_ausente_volta_ao_canto_padrao_do_principal()
    {
        var telas = new[] { (Key: Primaria, Area: Area) };
        var posicoes = new Dictionary<string, PillPosition> { [Externa] = new(3000, 300, false) };

        var (monitor, r) = PillDock.Resolve(Externa, posicoes, telas, Primaria, VerticalSize, HorizontalSize);

        Assert.Equal(Primaria, monitor);
        Assert.Equal(new DockResult(
            new Point(Area.Right - VerticalSize.Width, Area.Top + (Area.Height - VerticalSize.Height) / 2),
            Orientation.Vertical), r);
    }

    [Fact]
    public void Sem_monitor_salvo_e_sem_posicoes_usa_o_canto_padrao()
    {
        var telas = new[] { (Key: Primaria, Area: Area) };

        var (monitor, r) = PillDock.Resolve(null, null, telas, Primaria, VerticalSize, HorizontalSize);

        Assert.Equal(Primaria, monitor);
        Assert.Equal(new DockResult(new Point(1884, 454), Orientation.Vertical), r);
    }

    [Fact]
    public void Duas_telas_usa_a_externa_e_sem_ela_volta_ao_principal()
    {
        var externa = new Rectangle(1920, 0, 2560, 1400);
        var duas = new[] { (Key: Primaria, Area: Area), (Key: Externa, Area: externa) };
        var posicoes = new Dictionary<string, PillPosition> { [Externa] = new(3000, 300, false) };

        var (monitor, r) = PillDock.Resolve(Externa, posicoes, duas, Primaria, VerticalSize, HorizontalSize);
        Assert.Equal(Externa, monitor);
        Assert.Equal(new DockResult(new Point(3000, 300), Orientation.Vertical), r);

        var soPrincipal = new[] { (Key: Primaria, Area: Area) };
        var (volta, padrao) = PillDock.Resolve(Externa, posicoes, soPrincipal, Primaria, VerticalSize, HorizontalSize);
        Assert.Equal(Primaria, volta);
        Assert.Equal(new DockResult(new Point(1884, 454), Orientation.Vertical), padrao);
    }

    [Fact]
    public void Chave_do_monitor_junta_nome_e_resolucao()
    {
        Assert.Equal("\\\\.\\DISPLAY1|1920x1080", PillDock.MonitorKey("\\\\.\\DISPLAY1", new Rectangle(0, 0, 1920, 1080)));
    }

    [Fact]
    public void Posicao_da_pilula_sobrevive_a_JSON()
    {
        var s = new Settings(PillMonitor: Externa, PillPositions: new() { [Externa] = new PillPosition(3000, 300, false) });
        var volta = System.Text.Json.JsonSerializer.Deserialize<Settings>(System.Text.Json.JsonSerializer.Serialize(s))!;
        Assert.Equal(Externa, volta.PillMonitor);
        Assert.Equal(new PillPosition(3000, 300, false), volta.PillPositions![Externa]);

        var antigo = System.Text.Json.JsonSerializer.Deserialize<Settings>("""{ "Profile": "Frio" }""")!;
        Assert.Null(antigo.PillMonitor);
        Assert.Null(antigo.PillPositions);
    }
}

public class FullscreenGuardTests
{
    [Theory]
    [InlineData(2, true)]
    [InlineData(3, true)]
    [InlineData(4, true)]
    [InlineData(1, false)]
    [InlineData(5, false)]
    [InlineData(6, false)]
    [InlineData(7, false)]
    [InlineData(0, false)]
    public void ShouldHide_so_nos_estados_ocupado_e_tela_cheia(int state, bool expected) =>
        Assert.Equal(expected, FullscreenGuard.ShouldHide(state));

    [Fact]
    public void IsFullscreenAppActive_nao_lanca() => _ = FullscreenGuard.IsFullscreenAppActive();

    [Fact]
    public void ShowPill_liga_por_padrao_e_sobrevive_ao_JSON()
    {
        Assert.True(new Settings().ShowPill);
        Assert.True(System.Text.Json.JsonSerializer.Deserialize<Settings>("""{ "Profile": "Frio" }""")!.ShowPill);
        Assert.False(System.Text.Json.JsonSerializer.Deserialize<Settings>("""{ "ShowPill": false }""")!.ShowPill);
    }
}

public class FlyoutTests
{
    private static readonly Rectangle Area = new(0, 0, 1920, 1040);
    private static readonly Size FlyoutSize = new(300, 220);
    private static readonly Size PillV = new(36, 132), PillH = new(152, 36);

    private static Size PillSize(Orientation o) => o == Orientation.Vertical ? PillV : PillH;

    private static Snapshot Snap(double[] temps) => new(12, 2.1, 90, 2, 5, 1, 0, 2e9, 8e9, 3, 1e6, 2e6, 500e9, temps);

    [Theory]
    [InlineData(54.0, "normal")]
    [InlineData(74.9, "normal")]
    [InlineData(75.0, "quente")]
    [InlineData(89.9, "quente")]
    [InlineData(90.0, "quente demais")]
    [InlineData(100.0, "quente demais")]
    public void Heat_classifica_pela_faixa(double celsius, string expected) =>
        Assert.Equal(expected, FlyoutText.Heat(celsius));

    [Fact]
    public void Duas_zonas_de_temperatura_com_nivel_e_valor()
    {
        var temps = FlyoutText.Lines(Snap([54.0, 78.0]), null, false).Where(l => l.Text.StartsWith("Temperatura")).ToList();
        Assert.Equal(2, temps.Count);
        Assert.Contains("zona 1", temps[0].Text);
        Assert.Contains("zona 2", temps[1].Text);
        Assert.Contains("54 °C, normal", temps[0].Text);
        Assert.Contains("78 °C, quente", temps[1].Text);
        Assert.Equal(54.0, temps[0].Temp);
        Assert.Equal(78.0, temps[1].Temp);
    }

    [Fact]
    public void Uma_zona_nao_mostra_numero_de_zona()
    {
        var lines = FlyoutText.Lines(Snap([54.0]), null, false);
        Assert.Contains(lines, l => l.Text == "Temperatura: 54 °C, normal" && l.Temp == 54.0);
        Assert.DoesNotContain(lines, l => l.Text.Contains("zona"));
    }

    [Fact]
    public void Sem_sensor_de_temperatura_fica_indisponivel()
    {
        var lines = FlyoutText.Lines(Snap([]), null, false);
        Assert.Null(lines.Single(l => l.Text == "Temperatura: indisponível").Temp);
    }

    [Fact]
    public void Sem_RAM_total_fica_indisponivel()
    {
        var lines = FlyoutText.Lines(Snap([54.0]) with { RamTotal = 0 }, null, false);
        Assert.Contains(lines, l => l.Text == "RAM: indisponível");
    }

    [Fact]
    public void Modo_e_segundo_plano()
    {
        var s = Snap([54.0]);
        var semModo = FlyoutText.Lines(s, null, false);
        Assert.Contains(semModo, l => l.Text == "Modo: Personalizado");
        Assert.Contains(semModo, l => l.Text == "Segundo plano: desligado");

        var comModo = FlyoutText.Lines(s, PowerService.ModeByName("Frio"), true);
        Assert.Contains(comModo, l => l.Text == "Modo: Frio");
        Assert.Contains(comModo, l => l.Text == "Segundo plano: ligado");
    }

    [Fact]
    public void Turbo_aparece_so_acima_de_101()
    {
        var s = Snap([54.0]);
        Assert.Contains(FlyoutText.Lines(s with { CpuPerf = 102 }, null, false), l => l.Text.StartsWith("CPU:") && l.Text.Contains("em turbo"));
        Assert.DoesNotContain(FlyoutText.Lines(s with { CpuPerf = 101 }, null, false), l => l.Text.Contains("em turbo"));
    }

    [Fact]
    public void Valor_NaN_vira_travessao_e_nao_aparece_NaN()
    {
        var s = Snap([54.0]) with { Gpu3D = double.NaN, CpuUse = double.NaN, DiskActive = double.NaN, GpuVideo = double.NaN };
        var lines = FlyoutText.Lines(s, null, false);
        Assert.Contains("GPU: 3D – · vídeo –", lines.Single(l => l.Text.StartsWith("GPU")).Text);
        Assert.DoesNotContain("NaN", string.Join("\n", lines.Select(l => l.Text)));
    }

    [Theory]
    [InlineData(0, 400, Orientation.Vertical, 44, 356)]        // encostada à esquerda: abre à direita
    [InlineData(1884, 400, Orientation.Vertical, 1576, 356)]   // encostada à direita: abre à esquerda
    [InlineData(900, 0, Orientation.Horizontal, 826, 44)]      // encostada no topo: abre embaixo
    [InlineData(900, 1004, Orientation.Horizontal, 826, 776)]  // encostada na base: abre em cima
    public void Place_abre_do_lado_oposto_a_borda(int x, int y, Orientation o, int ex, int ey)
    {
        var p = FlyoutPlacement.Place(new Rectangle(new Point(x, y), PillSize(o)), o, FlyoutSize, Area, 8);
        Assert.Equal(new Point(ex, ey), p);
    }

    [Theory]
    [InlineData(0, 0, Orientation.Vertical)]
    [InlineData(1884, 0, Orientation.Vertical)]
    [InlineData(0, 908, Orientation.Vertical)]
    [InlineData(1884, 908, Orientation.Vertical)]
    [InlineData(0, 0, Orientation.Horizontal)]
    [InlineData(1768, 0, Orientation.Horizontal)]
    [InlineData(0, 1004, Orientation.Horizontal)]
    [InlineData(1768, 1004, Orientation.Horizontal)]
    public void Nos_quatro_cantos_o_flyout_fica_dentro_da_area_e_fora_da_pilula(int x, int y, Orientation o)
    {
        var pill = new Rectangle(new Point(x, y), PillSize(o));
        var flyout = new Rectangle(FlyoutPlacement.Place(pill, o, FlyoutSize, Area, 8), FlyoutSize);
        Assert.True(Area.Contains(flyout), $"flyout {flyout} fora da área");
        Assert.False(flyout.IntersectsWith(pill), $"flyout {flyout} sobrepõe a pílula {pill}");
    }

    [Fact]
    public void Flyout_maior_que_a_area_fica_no_canto_sem_lancar()
    {
        var p = FlyoutPlacement.Place(new Rectangle(0, 400, 36, 132), Orientation.Vertical, new Size(3000, 2000), Area, 8);
        Assert.Equal(new Point(Area.Left, Area.Top), p);
    }

    [Fact]
    public void Janela_nao_rouba_foco_nem_aparece_na_barra_e_cresce_com_as_linhas()
    {
        using var form = new FlyoutForm();
        _ = form.Handle;
        int ex = PillTests.GetWindowLongW(form.Handle, -20 /* GWL_EXSTYLE */);
        Assert.NotEqual(0, ex & 0x80 /* WS_EX_TOOLWINDOW */);
        Assert.NotEqual(0, ex & 0x08000000 /* WS_EX_NOACTIVATE */);
        Assert.False(form.ShowInTaskbar);

        form.SetLines(FlyoutText.Lines(Snap([54.0]), null, false), 1f);
        Assert.True(form.Size.Width > 0 && form.Size.Height > 0);
        int altura = form.Height;

        form.SetLines(FlyoutText.Lines(Snap([54.0, 78.0]), null, false), 1f);
        Assert.True(form.Height > altura);
    }
}
