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
        using var form = Painel();
        var s = new Snapshot(12, 2.1, 90, 5, 4, 1, 2e9, 8e9, 32e9, 3, 1e6, 2e6, 500e9, [55, 52]);
        for (int i = 0; i < 70; i++) form.Push(s);
        form.ShowPower(0, 100, current: null, syncControls: true);
        form.ShowPower(7, 42, current: null, syncControls: true); // valores fora da lista e da faixa do TrackBar não quebram
    }

    [Fact]
    public void Close_from_another_program_only_hides()
    {
        // Regressão: WM_CLOSE sem SC_CLOSE chega como TaskManagerClosing; o painel era descartado e o "abrir" seguinte derrubava o app.
        using var form = Painel();
        form.Show();
        SendMessage(form.Handle, 0x0010 /* WM_CLOSE */, 0, 0);
        Assert.False(form.IsDisposed);
        Assert.False(form.Visible);
    }

    // Ferramentas e Configurações entram como painéis vazios: nada de registro, UAC ou settings.json reais.
    private static MainForm Painel() => new(_ => { }, (_, _) => { }, openFan: null, new Panel(), new Panel());

    [Fact]
    public void Abre_no_Monitor_e_mostra_uma_aba_por_vez()
    {
        using var form = Painel();
        Assert.Equal(PanelTab.Monitor, form.Tab);
        form.Show();

        form.ShowTab(PanelTab.Tools);
        var host = form.Controls.OfType<Panel>().Single(p => p.Dock == DockStyle.Fill);
        Assert.Single(host.Controls.Cast<Control>(), c => c.Visible);
        Assert.Equal(PanelTab.Tools, form.Tab);
    }

    [Fact]
    public void Ctrl_Tab_troca_de_aba_e_da_a_volta()
    {
        using var form = Painel();
        form.Show();
        SendKeysTo(form, Keys.Control | Keys.Shift | Keys.Tab);
        Assert.Equal(PanelTab.Settings, form.Tab);
        SendKeysTo(form, Keys.Control | Keys.Tab);
        Assert.Equal(PanelTab.Monitor, form.Tab);
    }

    private static void SendKeysTo(MainForm form, Keys keys)
    {
        var msg = new Message { HWnd = form.Handle, Msg = 0x0100 /* WM_KEYDOWN */ };
        typeof(Form).GetMethod("ProcessCmdKey", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .Invoke(form, [msg, keys]);
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam);
}

public class NotchTests
{
    private static readonly Rectangle Area = new(0, 0, 1920, 1040);

    [Theory]
    [InlineData(1884, 500, (int)DockEdges.Right)]             // borda direita: vertical
    [InlineData(0, 500, (int)DockEdges.Left)]
    [InlineData(900, 0, (int)DockEdges.Top)]
    [InlineData(900, 1004, (int)DockEdges.Bottom)]
    [InlineData(1884, 0, (int)(DockEdges.Top | DockEdges.Right))] // canto: toca as duas
    [InlineData(900, 500, (int)DockEdges.None)]               // solta no meio
    public void Bordas_que_a_pilula_toca(int x, int y, int esperado) =>
        Assert.Equal((DockEdges)esperado, PillDock.Edges(new Rectangle(x, y, 36, 36), Area));

    [Fact]
    public void Cantos_do_lado_da_tela_ficam_retos()
    {
        Assert.Equal((18, 0, 0, 18), PillDock.Corners(18, DockEdges.Right));
        Assert.Equal((0, 0, 18, 18), PillDock.Corners(18, DockEdges.Top));
        Assert.Equal((0, 0, 0, 18), PillDock.Corners(18, DockEdges.Top | DockEdges.Right));
        Assert.Equal((18, 18, 18, 18), PillDock.Corners(18, DockEdges.None));
    }
}

public class SettingsPageTests
{
    private static SettingsPage Pagina(List<Settings> salvas, Func<Settings, string?>? erro = null)
    {
        SettingsPage? pagina = null;
        var atual = new Settings();
        pagina = new SettingsPage(edit =>
        {
            var next = edit(atual);
            string? e = erro?.Invoke(next);
            if (e is null) atual = next;
            salvas.Add(atual);
            pagina!.Show(atual, startup: false); // como o TrayApp: devolve o que ficou salvo
            return e;
        }, _ => { });
        pagina.Show(atual, startup: false);
        return pagina;
    }

    [Fact]
    public void Mostrar_nao_dispara_mudanca()
    {
        List<Settings> salvas = [];
        using var pagina = Pagina(salvas);
        pagina.Show(new Settings(AlertCelsius: 80, AlertSeconds: 10, AutoCool: true, ShowPill: false, Hotkey: "Ctrl+Alt+K"), startup: true);
        Assert.Empty(salvas);
        Assert.False(pagina.Pill.Checked);
        Assert.True(pagina.Startup.Checked);
        Assert.Equal(80, pagina.AlertCelsius.Value);
        Assert.Equal("Ctrl+Alt+K", pagina.Hotkey.Text);
    }

    [Fact]
    public void Cada_controle_muda_so_o_seu_campo()
    {
        List<Settings> salvas = [];
        using var pagina = Pagina(salvas);
        pagina.Pill.Checked = false;
        pagina.AutoCool.Checked = true;
        pagina.AlertCelsius.Value = 85;
        pagina.AlertSeconds.Value = 45;

        var s = salvas[^1];
        Assert.Equal(new Settings(ShowPill: false, AutoCool: true, AlertCelsius: 85, AlertSeconds: 45), s);
    }

    [Theory]
    [InlineData("Ctrl+Alt+K", "Ctrl+Alt+K", "Atalho registrado.")]
    [InlineData("", "", "Atalho desligado.")]
    public void Atalho_valido_e_salvo(string texto, string salvo, string aviso)
    {
        List<Settings> salvas = [];
        using var pagina = Pagina(salvas);
        pagina.Hotkey.Text = texto;
        pagina.ApplyHotkey();
        Assert.Equal(salvo, Assert.Single(salvas).Hotkey);
        Assert.Equal(aviso, pagina.HotkeyStatus.Text);
    }

    [Fact]
    public void Atalho_invalido_nem_chega_ao_TrayApp()
    {
        List<Settings> salvas = [];
        using var pagina = Pagina(salvas);
        pagina.Hotkey.Text = "K";
        pagina.ApplyHotkey();
        Assert.Empty(salvas);
        Assert.Equal("Atalho inválido.", pagina.HotkeyStatus.Text);
    }

    [Fact]
    public void Atalho_em_uso_mostra_o_erro_e_volta_ao_anterior()
    {
        List<Settings> salvas = [];
        using var pagina = Pagina(salvas, next => next.Hotkey == "Ctrl+Alt+K" ? "em uso" : null);
        pagina.Hotkey.Text = "Ctrl+Alt+K";
        pagina.ApplyHotkey();
        Assert.Equal("em uso", pagina.HotkeyStatus.Text);
        Assert.Equal(HotkeyService.Default, pagina.Hotkey.Text);
    }

    [Fact]
    public void Modo_automatico_mostra_o_padrao_sem_salvar()
    {
        List<Settings> salvas = [];
        using var pagina = Pagina(salvas);
        Assert.Empty(salvas);
        Assert.False(pagina.AutoOn.Checked);
        Assert.Equal("Economia", pagina.OnBattery.SelectedItem);
        Assert.Equal("Equilibrado", pagina.OnAc.SelectedItem);
        Assert.Equal("ffmpeg → Frio", Assert.Single(pagina.Programs.Items));
    }

    [Fact]
    public void Ligar_o_modo_automatico_grava_com_as_regras_de_exemplo()
    {
        List<Settings> salvas = [];
        using var pagina = Pagina(salvas);
        pagina.AutoOn.Checked = true;
        var regras = salvas[^1].Auto!;
        Assert.True(regras.Enabled);
        Assert.Equal(new ProgramRule("ffmpeg", "Frio"), Assert.Single(regras.Programs!));
    }

    [Fact]
    public void Modos_de_tomada_e_bateria_mudam_so_o_seu_campo_e_nenhum_vira_null()
    {
        List<Settings> salvas = [];
        using var pagina = Pagina(salvas);
        pagina.OnAc.SelectedItem = "Alto";
        Assert.Equal(("Economia", "Alto"), (salvas[^1].Auto!.OnBattery, salvas[^1].Auto!.OnAc));
        pagina.OnBattery.SelectedItem = "(nenhum)";
        Assert.Null(salvas[^1].Auto!.OnBattery);
        Assert.Equal("Alto", salvas[^1].Auto!.OnAc);
    }

    [Fact]
    public void Adicionar_e_remover_programa()
    {
        List<Settings> salvas = [];
        using var pagina = Pagina(salvas);
        pagina.ProgramName.Text = "  HandBrake.exe ";
        pagina.ProgramMode.SelectedItem = "Máximo";
        pagina.AddProgram();
        Assert.Equal(["ffmpeg → Frio", "HandBrake.exe → Máximo"], pagina.Programs.Items.Cast<string>());
        Assert.Equal("", pagina.ProgramName.Text);

        pagina.ProgramName.Text = "handbrake"; // mesmo programa: troca o modo em vez de duplicar
        pagina.ProgramMode.SelectedItem = "Alto";
        pagina.AddProgram();
        Assert.Equal(["ffmpeg → Frio", "handbrake → Alto"], pagina.Programs.Items.Cast<string>());

        pagina.Programs.SelectedIndex = 0;
        pagina.RemoveProgram();
        Assert.Equal(new ProgramRule("handbrake", "Alto"), Assert.Single(salvas[^1].Auto!.Programs!));
    }

    [Fact]
    public void Nome_vazio_nao_adiciona_e_remover_sem_selecao_nao_faz_nada()
    {
        List<Settings> salvas = [];
        using var pagina = Pagina(salvas);
        pagina.ProgramName.Text = "   ";
        pagina.AddProgram();
        pagina.RemoveProgram();
        Assert.Empty(salvas);
    }
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
    public void Vertical_tem_40_px_de_largura_e_linhas_de_metrica_de_32()
    {
        var (size, items) = PillLayout.Compute(Orientation.Vertical, 1f);
        Assert.Equal(40, size.Width);
        foreach (var i in new[] { PillLayout.Cpu, PillLayout.Gpu, PillLayout.Ram })
            Assert.Equal(32, items[i].Row.Height);
    }

    [Fact]
    public void Horizontal_tem_40_px_de_altura()
    {
        var (size, _) = PillLayout.Compute(Orientation.Horizontal, 1f);
        Assert.Equal(40, size.Height);
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
        Assert.Equal("Modo de energia: Economia, automático",
            PillForm.RowName(PillLayout.Mode, st, PowerService.ModeByName("Economia"), false, auto: true));
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

        var automatico = FlyoutText.Lines(s, PowerService.ModeByName("Economia"), false, auto: true);
        Assert.Contains(automatico, l => l.Text == "Modo: Economia · automático");
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

public class QuickPanelTests
{
    private static readonly Color Destaque = Color.FromArgb(0x40, 0x9C, 0xFF), Neutro = Color.FromArgb(0x14, 0x14, 0x14);

    private static QuickPanel Abrir(Action<PowerMode>? modo = null, Action? segundoPlano = null, Action? tela = null, Action? completo = null, Action? ferramentas = null)
    {
        var painel = new QuickPanel(modo ?? (_ => { }), segundoPlano ?? (() => { }), tela ?? (() => { }), completo ?? (() => { }), ferramentas ?? (() => { }));
        painel.Location = new Point(-3000, -3000); // mostra de verdade, mas fora da tela
        painel.Show();
        Application.DoEvents();
        return painel;
    }

    private static Button Botao(QuickPanel painel, string texto) =>
        painel.Controls.OfType<Button>().Single(b => b.Text == texto);

    // O texto do botão de segundo plano muda com o estado ("Segundo\nplano ligado").
    private static Button Segundo(QuickPanel painel) =>
        painel.Controls.OfType<Button>().Single(b => b.Text.StartsWith("Segundo"));

    [Fact]
    public void Cada_botao_de_modo_aplica_o_seu_modo_na_ordem_de_Modes()
    {
        var aplicados = new List<PowerMode>();
        using var painel = Abrir(modo: aplicados.Add);

        foreach (var m in PowerService.Modes) Botao(painel, m.Name).PerformClick();

        Assert.Equal(PowerService.Modes.Select(m => m.Name), aplicados.Select(m => m.Name));
    }

    [Fact]
    public void Desligar_tela_chama_a_acao_e_esconde_o_painel()
    {
        var chamadas = 0;
        using var painel = Abrir(tela: () => chamadas++);

        Botao(painel, "Desligar tela").PerformClick();

        Assert.Equal(1, chamadas);
        Assert.False(painel.Visible);
    }

    [Fact]
    public void Ferramentas_abre_a_aba_Ferramentas_e_esconde_o_painel()
    {
        int ferramentas = 0, completo = 0;
        using var painel = Abrir(completo: () => completo++, ferramentas: () => ferramentas++);

        Botao(painel, "Ferramentas").PerformClick();

        Assert.Equal(1, ferramentas);
        Assert.Equal(0, completo);
        Assert.False(painel.Visible);
    }

    [Fact]
    public void Abrir_painel_completo_chama_a_acao_e_esconde_o_painel()
    {
        var chamadas = 0;
        using var painel = Abrir(completo: () => chamadas++);

        Botao(painel, "Abrir painel completo").PerformClick();

        Assert.Equal(1, chamadas);
        Assert.False(painel.Visible);
    }

    [Fact]
    public void Segundo_plano_chama_a_acao_e_mostra_o_estado()
    {
        var chamadas = 0;
        using var painel = Abrir(segundoPlano: () => chamadas++);

        Segundo(painel).PerformClick();
        Assert.Equal(1, chamadas);

        painel.SetState(null, true);
        Assert.Contains("ligado", Segundo(painel).Text);
        Assert.DoesNotContain("desligado", Segundo(painel).Text);

        painel.SetState(null, false);
        Assert.Contains("desligado", Segundo(painel).Text);
    }

    [Fact]
    public void SetState_destaca_so_o_modo_atual()
    {
        using var painel = Abrir();
        var frio = PowerService.ModeByName("Frio")!;

        painel.SetState(frio, false);
        foreach (var m in PowerService.Modes)
            Assert.Equal(m == frio ? Destaque : Neutro, Botao(painel, m.Name).BackColor);

        painel.SetState(null, false);
        foreach (var m in PowerService.Modes)
            Assert.Equal(Neutro, Botao(painel, m.Name).BackColor);
    }

    [Fact]
    public void Titulo_mostra_quando_o_modo_e_automatico()
    {
        using var painel = Abrir();
        var frio = PowerService.ModeByName("Frio")!;
        Label Titulo() => painel.Controls.OfType<Label>().Single(l => l.Text.StartsWith("Modo de energia"));

        painel.SetState(frio, false, auto: true);
        Assert.Equal("Modo de energia · automático", Titulo().Text);
        painel.SetState(frio, false);
        Assert.Equal("Modo de energia", Titulo().Text);
    }

    [Fact]
    public void Painel_rapido_pode_receber_foco_e_tem_TOOLWINDOW()
    {
        using var painel = Abrir();
        int ex = PillTests.GetWindowLongW(painel.Handle, -20 /* GWL_EXSTYLE */);
        Assert.NotEqual(0, ex & 0x80 /* WS_EX_TOOLWINDOW */);
        Assert.Equal(0, ex & 0x08000000 /* WS_EX_NOACTIVATE */);
    }

    [Fact]
    public void Pilula_e_flyout_continuam_sem_foco()
    {
        using var pill = new PillForm();
        using var flyout = new FlyoutForm();
        foreach (var janela in new Form[] { pill, flyout })
        {
            int ex = PillTests.GetWindowLongW(janela.Handle, -20 /* GWL_EXSTYLE */);
            Assert.NotEqual(0, ex & 0x80 /* WS_EX_TOOLWINDOW */);
            Assert.NotEqual(0, ex & 0x08000000 /* WS_EX_NOACTIVATE */);
        }
    }

    [Fact]
    public void Tab_percorre_modos_depois_acoes_e_rodape()
    {
        using var painel = Abrir();
        var botoes = painel.Controls.OfType<Button>().ToList();
        Assert.All(botoes, b => Assert.True(b.TabStop));
        Assert.Equal(botoes.Count, botoes.Select(b => b.TabIndex).Distinct().Count()); // TabIndex definido, não empatado

        var esperado = PowerService.Modes.Select(m => m.Name)
            .Concat(new[] { "Segundo plano", "Desligar tela", "Ferramentas", "Abrir painel completo" });
        Assert.Equal(esperado, botoes.OrderBy(b => b.TabIndex).Select(b => b.Text));
    }
}

public class ModeStoreTests
{
    // Modo null = Personalizado. Os argumentos são sempre passados explicitamente.
    private static ModeState Estado(PowerMode? modo, uint turbo = 1, uint limite = 100, bool segundo = false) =>
        new(modo, turbo, limite, segundo);

    // Uma "janela" da interface: conta os avisos e guarda o último estado recebido.
    private sealed class Janela
    {
        public int Avisos;
        public ModeState? Ultimo;

        public void Ao(ModeState s)
        {
            Avisos++;
            Ultimo = s;
        }
    }

    [Fact]
    public void Estado_comeca_nulo_e_o_primeiro_Set_devolve_true_e_dispara_uma_vez()
    {
        var store = new ModeStore();
        Assert.Null(store.State);

        var recebidos = new List<ModeState>();
        store.Changed += recebidos.Add;

        var estado = Estado(PowerService.ModeByName("Equilibrado"));
        Assert.True(store.Set(estado));
        Assert.Equal(new[] { estado }, recebidos);
        Assert.Equal<ModeState?>(estado, store.State);
    }

    [Fact]
    public void Set_com_estado_igual_devolve_false_e_nao_dispara_de_novo()
    {
        var store = new ModeStore();
        var disparos = 0;
        store.Changed += _ => disparos++;

        store.Set(Estado(PowerService.ModeByName("Equilibrado")));
        Assert.False(store.Set(Estado(PowerService.ModeByName("Equilibrado"))));
        Assert.Equal(1, disparos);
    }

    [Fact]
    public void Mudar_segundo_plano_modo_personalizado_turbo_ou_limite_dispara()
    {
        var store = new ModeStore();
        var disparos = 0;
        store.Changed += _ => disparos++;

        Assert.True(store.Set(Estado(PowerService.ModeByName("Frio"), 0, 50, false)));
        Assert.True(store.Set(Estado(PowerService.ModeByName("Frio"), 0, 50, true)));         // só o segundo plano
        Assert.True(store.Set(Estado(PowerService.ModeByName("Equilibrado"), 0, 50, true)));  // só o modo (Frio -> Equilibrado)
        Assert.True(store.Set(Estado(null, 0, 50, true)));                                    // modo vira Personalizado
        Assert.True(store.Set(Estado(null, 2, 50, true)));                                    // só o turbo
        Assert.True(store.Set(Estado(null, 2, 80, true)));                                    // só o limite
        Assert.Equal(6, disparos);
    }

    [Fact]
    public void Varios_assinantes_recebem_o_mesmo_estado_na_ordem_e_o_removido_deixa_de_receber()
    {
        var store = new ModeStore();
        var recebidos = new List<(string Quem, ModeState Estado)>();
        Action<ModeState> a = s => recebidos.Add(("A", s));
        Action<ModeState> b = s => recebidos.Add(("B", s));
        Action<ModeState> c = s => recebidos.Add(("C", s));
        store.Changed += a;
        store.Changed += b;
        store.Changed += c;

        var primeiro = Estado(PowerService.ModeByName("Frio"));
        store.Set(primeiro);
        Assert.Equal(new[] { ("A", primeiro), ("B", primeiro), ("C", primeiro) }, recebidos);

        recebidos.Clear();
        store.Changed -= b;
        var segundo = Estado(PowerService.ModeByName("Alto"), 2);
        store.Set(segundo);
        Assert.Equal(new[] { ("A", segundo), ("C", segundo) }, recebidos);
    }

    [Fact]
    public void Um_Set_atualiza_as_quatro_janelas_com_o_mesmo_estado()
    {
        // Bandeja, pílula, painel rápido e painel completo: todas assinam o mesmo ModeStore.
        var store = new ModeStore();
        var bandeja = new Janela();
        var pilula = new Janela();
        var rapido = new Janela();
        var completo = new Janela();
        store.Changed += bandeja.Ao;
        store.Changed += pilula.Ao;
        store.Changed += rapido.Ao;
        store.Changed += completo.Ao;

        var estado = Estado(PowerService.ModeByName("Máximo"), 4, 100, true);
        store.Set(estado);

        foreach (var janela in new[] { bandeja, pilula, rapido, completo })
        {
            Assert.Equal(1, janela.Avisos);
            Assert.Equal<ModeState?>(estado, janela.Ultimo);
        }
    }
}

public class ElevatedTaskTests
{
    private static readonly string Report = Path.Combine(Path.GetTempPath(), "ThermoBook-teste.json");

    // Tarefa falsa: os testes nunca podem rodar a limpeza de verdade na máquina de quem testa.
    static ElevatedTaskTests() => ElevatedTask.Tasks["teste"] = () => new(TaskOutcome.Ok, "feito");

    [Theory]
    [InlineData("limpeza")]
    [InlineData("limpeza-lixeira")]
    [InlineData("otimizacao-entrega")]
    [InlineData("otimizacao-trim")]
    [InlineData("otimizacao-entrega-trim")]
    [InlineData("otimizacao-desfazer")]
    public void Aceita_so_tarefas_conhecidas(string task) =>
        Assert.Equal(new ElevatedTask.Args(task, Report), ElevatedTask.Parse(["--tarefa", task, "--relatorio", Report]));

    public static TheoryData<string[]> Invalidos => new()
    {
        Array.Empty<string>(),
        new[] { "--tarefa", "limpeza" },
        new[] { "--tarefa", "formatar", "--relatorio", Report },
        new[] { "--tarefa", "Limpeza", "--relatorio", Report },
        new[] { "--relatorio", Report, "--tarefa", "limpeza" },
        new[] { "--tarefa", "limpeza", "--relatorio", Report, "--extra" },
        new[] { "--tarefa", "limpeza", "--relatorio", "relatorio.json" },
        new[] { "--tarefa", "limpeza", "--relatorio", @"C:\Windows\System32\drivers\etc\hosts" },
        new[] { "--tarefa", "limpeza", "--relatorio", "C:\\x\0.json" },
    };

    [Theory]
    [MemberData(nameof(Invalidos))]
    public void Rejeita_qualquer_outro_argumento(string[] args)
    {
        Assert.Null(ElevatedTask.Parse(args));
        Assert.Equal(2, ElevatedTask.RunElevated(args));
    }

    [Fact]
    public void Lado_elevado_nao_sobrescreve_arquivo_existente()
    {
        string path = Path.Combine(Path.GetTempPath(), $"ThermoBook-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, "original");
        try
        {
            Assert.Equal(3, ElevatedTask.RunElevated(["--tarefa", "teste", "--relatorio", path]));
            Assert.Equal("original", File.ReadAllText(path));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task UAC_cancelado_vira_Cancelado_sem_excecao()
    {
        var r = await ElevatedTask.LaunchAsync("limpeza", _ => throw new System.ComponentModel.Win32Exception(1223));
        Assert.Equal(new TaskReport(TaskOutcome.Cancelled, "Cancelado"), r);
    }

    [Fact]
    public async Task Tarefa_desconhecida_nem_pede_UAC()
    {
        bool started = false;
        var r = await ElevatedTask.LaunchAsync("formatar", _ => { started = true; return null; });
        Assert.False(started);
        Assert.Equal(TaskOutcome.Failed, r.Outcome);
    }

    [Fact]
    public async Task Le_o_relatorio_do_processo_elevado_e_apaga_o_arquivo()
    {
        string? report = null;
        var r = await ElevatedTask.LaunchAsync("teste", psi =>
        {
            Assert.Equal("runas", psi.Verb);
            string[] args = [.. psi.ArgumentList];
            report = args[3];
            ElevatedTask.RunElevated(args); // faz o papel do processo elevado
            return System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("cmd.exe", "/c exit 0") { CreateNoWindow = true });
        });
        Assert.Equal(new TaskReport(TaskOutcome.Ok, "feito"), r);
        Assert.False(File.Exists(report));
    }

    [Fact]
    public async Task Processo_sem_relatorio_vira_falha()
    {
        var r = await ElevatedTask.LaunchAsync("limpeza", _ =>
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("cmd.exe", "/c exit 0") { CreateNoWindow = true }));
        Assert.Equal(new TaskReport(TaskOutcome.Failed, "A tarefa terminou sem relatório."), r);
    }
}

public class UninstallTests : IDisposable
{
    // Chave falsa no lugar de HKCU e HKLM; settings.json numa pasta temporária.
    private readonly string _regPath = $@"Software\ThermoBook\Teste\{Guid.NewGuid():N}";
    private readonly Microsoft.Win32.RegistryKey _root;
    private readonly string _file = Path.Combine(Path.GetTempPath(), $"ThermoBook-{Guid.NewGuid():N}", "settings.json");
    private readonly SettingsStore _store;
    private readonly List<(Guid Plan, uint Ac)> _writes = [];
    private readonly List<string> _elevated = [];
    private int _applied;

    public UninstallTests()
    {
        _root = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(_regPath, writable: true);
        _store = new SettingsStore(_file);
    }

    public void Dispose()
    {
        _root.Dispose();
        Microsoft.Win32.Registry.CurrentUser.DeleteSubKeyTree(_regPath, throwOnMissingSubKey: false);
        foreach (var k in new[] { @"Software\ThermoBook\Teste", @"Software\ThermoBook" }) // some quando vazias, como no TweaksTests
            try { Microsoft.Win32.Registry.CurrentUser.DeleteSubKey(k, throwOnMissingSubKey: false); } catch (InvalidOperationException) { break; }
        if (Directory.Exists(Path.GetDirectoryName(_file)))
            Directory.Delete(Path.GetDirectoryName(_file)!, recursive: true);
    }

    private int Run(Action? apply = null, TaskOutcome uac = TaskOutcome.Ok) => Uninstall.Run(_store, _root, _root,
        (plan, _, _, ac, _) => { _writes.Add((plan, ac)); return true; },
        apply ?? (() => _applied++), task => { _elevated.Add(task); return new(uac, ""); });

    [Fact]
    public void Desfaz_segundo_plano_ajustes_da_conta_e_de_maquina_e_volta_o_modo()
    {
        var plan = Guid.NewGuid();
        var t = Tweaks.All.First(x => !x.Machine);
        var backup = new Dictionary<string, int?>();
        Tweaks.Apply(_root, t, backup, () => { });
        using (var k = _root.CreateSubKey(@"SOFTWARE\ThermoBook")) k.SetValue("Desfazer", "{\"x\":1}");
        _store.Save(new Settings(TweakBackup: backup,
            BackgroundPlans: new() { [plan.ToString()] = new() { ["lid"] = new PairValue(1, 1) } }));

        Assert.Equal(0, Run());

        Assert.Contains((plan, 1u), _writes);
        using (var k = _root.OpenSubKey(t.Path)) Assert.Null(k?.GetValue(t.Name));
        Assert.Equal(1, _applied);
        Assert.Equal(["otimizacao-desfazer"], _elevated);
        var s = _store.Load();
        Assert.Null(s.BackgroundPlans);
        Assert.Empty(s.TweakBackup!);
    }

    [Fact]
    public void Sem_nada_ligado_so_volta_o_modo_e_nao_pede_UAC()
    {
        Assert.Equal(0, Run());
        Assert.Empty(_writes);
        Assert.Empty(_elevated);
        Assert.Equal(1, _applied);
    }

    [Fact]
    public void Passo_que_falha_nao_impede_os_outros()
    {
        var plan = Guid.NewGuid();
        using (var k = _root.CreateSubKey(@"SOFTWARE\ThermoBook")) k.SetValue("Desfazer", "{\"x\":1}");
        _store.Save(new Settings(BackgroundPlans: new() { [plan.ToString()] = new() { ["lid"] = new PairValue(1, 1) } }));

        Assert.Equal(1, Run(apply: () => throw new InvalidOperationException()));
        Assert.Contains((plan, 1u), _writes);
        Assert.Equal(["otimizacao-desfazer"], _elevated);
    }

    [Fact]
    public void UAC_recusado_devolve_1()
    {
        using (var k = _root.CreateSubKey(@"SOFTWARE\ThermoBook")) k.SetValue("Desfazer", "{\"x\":1}");
        Assert.Equal(1, Run(uac: TaskOutcome.Cancelled));
    }
}

public class CleanupTests
{
    private static string NewDir()
    {
        string d = Path.Combine(Path.GetTempPath(), $"ThermoBook-limpeza-{Guid.NewGuid():N}");
        Directory.CreateDirectory(d);
        return d;
    }

    [Fact]
    public void Apaga_o_conteudo_e_mantem_a_pasta_raiz()
    {
        string root = NewDir();
        try
        {
            File.WriteAllText(Path.Combine(root, "a.tmp"), "x");
            Directory.CreateDirectory(Path.Combine(root, "sub", "vazia"));
            File.WriteAllText(Path.Combine(root, "sub", "b.tmp"), "y");
            var ro = Path.Combine(root, "somente-leitura.tmp");
            File.WriteAllText(ro, "z");
            File.SetAttributes(ro, FileAttributes.ReadOnly);

            Assert.True(Cleanup.DeleteContents(root) >= 4);
            Assert.True(Directory.Exists(root));
            Assert.Empty(Directory.GetFileSystemEntries(root));
            Assert.Equal(0, Cleanup.DeleteContents(root)); // segunda vez: já estava limpo
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void Nao_toca_em_nada_fora_da_pasta_listada()
    {
        string root = NewDir(), outside = NewDir();
        try
        {
            string keep = Path.Combine(outside, "fica.txt");
            File.WriteAllText(keep, "x");
            File.WriteAllText(Path.Combine(root, "vai.tmp"), "x");
            Cleanup.DeleteContents(root);
            Assert.True(File.Exists(keep));
        }
        finally { Directory.Delete(root, true); Directory.Delete(outside, true); }
    }

    [Fact]
    public void Item_em_uso_e_pulado_sem_erro()
    {
        string root = NewDir();
        try
        {
            string busy = Path.Combine(root, "em-uso.tmp");
            using (new FileStream(busy, FileMode.Create, FileAccess.ReadWrite, FileShare.None))
            {
                File.WriteAllText(Path.Combine(root, "livre.tmp"), "x");
                Cleanup.DeleteContents(root);
                Assert.True(File.Exists(busy));
                Assert.False(File.Exists(Path.Combine(root, "livre.tmp")));
            }
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void Junction_e_removida_sem_apagar_o_destino()
    {
        string root = NewDir(), target = NewDir();
        try
        {
            string keep = Path.Combine(target, "fica.txt");
            File.WriteAllText(keep, "x");
            Directory.CreateSymbolicLink(Path.Combine(root, "link"), target);
            Cleanup.DeleteContents(root);
            Assert.Empty(Directory.GetFileSystemEntries(root));
            Assert.True(File.Exists(keep));
        }
        catch (UnauthorizedAccessException) { /* sem permissão para criar link simbólico nesta máquina */ }
        finally { Directory.Delete(root, true); Directory.Delete(target, true); }
    }

    [Theory]
    [InlineData(@"C:")]
    [InlineData(@"C:")]
    [InlineData(@"relativa")]
    [InlineData(@"")]
    public void Recusa_raiz_de_disco_e_caminho_relativo(string root) => Assert.Equal(0, Cleanup.DeleteContents(root));

    [Fact]
    public void Lista_padrao_so_tem_pastas_de_temporarios_e_nenhuma_raiz()
    {
        var roots = Cleanup.DefaultRoots();
        Assert.Equal(7, roots.Count);
        Assert.All(roots, r => Assert.NotNull(Path.GetDirectoryName(r.TrimEnd('\\'))));
    }

    [Theory]
    [InlineData(0L, "Já estava limpo.")]
    [InlineData(-5L, "Já estava limpo.")]
    [InlineData(52_428_800L, "50 MB liberados")]
    [InlineData(1_288_490_189L, "1,2 GB liberados")]
    public void Texto_do_resultado(long bytes, string expected) => Assert.Equal(expected, Cleanup.Describe(bytes));
}

public class TweaksTests
{
    // Nunca as chaves reais do Windows: cada teste usa HKCU\Software\ThermoBook\Teste\<guid> como raiz (no lugar de HKCU e HKLM).
    private sealed class Raiz : IDisposable
    {
        private readonly string _path = $@"Software\ThermoBook\Teste\{Guid.NewGuid():N}";
        public readonly Microsoft.Win32.RegistryKey Key;
        public Raiz() => Key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(_path, writable: true);
        public object? Valor(Tweak t) { using var k = Key.OpenSubKey(t.Path); return k?.GetValue(t.Name); }
        public void Gravar(Tweak t, object v) { using var k = Key.CreateSubKey(t.Path); k.SetValue(t.Name, v); }
        public void Dispose()
        {
            Key.Dispose();
            Microsoft.Win32.Registry.CurrentUser.DeleteSubKeyTree(_path, throwOnMissingSubKey: false);
            // Some com a Teste (e a ThermoBook) quando ficam vazias; se outro teste ainda usa, DeleteSubKey lança e fica para ele.
            foreach (var k in new[] { @"Software\ThermoBook\Teste", @"Software\ThermoBook" })
                try { Microsoft.Win32.Registry.CurrentUser.DeleteSubKey(k, throwOnMissingSubKey: false); } catch (InvalidOperationException) { break; }
        }
    }

    public static TheoryData<int> Ajustes => new(Enumerable.Range(0, Tweaks.All.Length));

    [Theory]
    [MemberData(nameof(Ajustes))]
    public void Aplicar_e_desfazer_volta_o_valor_original_quando_existia(int i)
    {
        var t = Tweaks.All[i];
        using var raiz = new Raiz();
        raiz.Gravar(t, 7);
        var backup = new Dictionary<string, int?>();

        Assert.True(Tweaks.Apply(raiz.Key, t, backup, () => { }));
        Assert.Equal(t.Value, raiz.Valor(t));
        Assert.Equal(7, backup[t.Key]);

        Assert.Equal(1, Tweaks.Undo(raiz.Key, [t], backup, () => { }));
        Assert.Equal(7, raiz.Valor(t));
        Assert.Empty(backup);
    }

    [Theory]
    [MemberData(nameof(Ajustes))]
    public void Aplicar_e_desfazer_apaga_o_valor_quando_nao_existia(int i)
    {
        var t = Tweaks.All[i];
        using var raiz = new Raiz();
        var backup = new Dictionary<string, int?>();

        Assert.True(Tweaks.Apply(raiz.Key, t, backup, () => { }));
        Assert.Equal(t.Value, raiz.Valor(t));
        Assert.Equal(1, Tweaks.Undo(raiz.Key, [t], backup, () => { }));
        Assert.Null(raiz.Valor(t));
    }

    [Fact]
    public void Aplicar_duas_vezes_nao_sobrescreve_o_backup()
    {
        var t = Tweaks.All[0];
        using var raiz = new Raiz();
        raiz.Gravar(t, 1);
        var backup = new Dictionary<string, int?>();
        Tweaks.Apply(raiz.Key, t, backup, () => { });
        Tweaks.Apply(raiz.Key, t, backup, () => { });
        Tweaks.Undo(raiz.Key, [t], backup, () => { });
        Assert.Equal(1, raiz.Valor(t));
    }

    [Fact]
    public void Backup_e_salvo_antes_de_gravar()
    {
        var t = Tweaks.All[0];
        using var raiz = new Raiz();
        raiz.Gravar(t, 5);
        object? naHoraDeSalvar = null;
        Tweaks.Apply(raiz.Key, t, new(), () => naHoraDeSalvar = raiz.Valor(t));
        Assert.Equal(5, naHoraDeSalvar);
    }

    [Fact]
    public void Valor_de_outro_tipo_nao_e_tocado()
    {
        var t = Tweaks.All[0];
        using var raiz = new Raiz();
        raiz.Gravar(t, "texto");
        var backup = new Dictionary<string, int?>();
        Assert.False(Tweaks.Apply(raiz.Key, t, backup, () => { }));
        Assert.Equal("texto", raiz.Valor(t));
        Assert.Empty(backup);
    }

    [Fact]
    public void Ajustes_de_maquina_guardam_o_backup_na_raiz_e_desfazem()
    {
        var t = Tweaks.All.Single(x => x.Machine);
        using var raiz = new Raiz();
        raiz.Gravar(t, 3);

        Assert.True(Tweaks.ApplyMachine(raiz.Key));
        Assert.Equal(0, raiz.Valor(t));
        Assert.Equal(3, Tweaks.LoadMachineBackup(raiz.Key)[t.Key]);

        Assert.Equal(1, Tweaks.UndoMachine(raiz.Key));
        Assert.Equal(3, raiz.Valor(t));
        Assert.Empty(Tweaks.LoadMachineBackup(raiz.Key));
    }

    [Fact]
    public void So_Delivery_Optimization_e_de_maquina_e_os_grupos_sao_os_do_ps1()
    {
        Assert.Equal([@"SOFTWARE\Policies\Microsoft\Windows\DeliveryOptimization\DODownloadMode"], Tweaks.All.Where(t => t.Machine).Select(t => t.Key));
        Assert.Equal([Tweaks.Privacy, Tweaks.Performance], Tweaks.All.Select(t => t.Group).Distinct());
    }

    [Theory]
    [InlineData(true, true, "Defender ativo, proteção em tempo real ligada.")]
    [InlineData(true, false, "Defender ativo, mas a proteção em tempo real está DESLIGADA.")]
    [InlineData(false, false, "Defender desligado.")]
    public void Texto_do_Defender(bool av, bool rt, string esperado) => Assert.Equal(esperado, Tweaks.DescribeDefender(av, rt, null));

    // Tela com dependências falsas: nenhum UAC, nenhum app real, registro só na raiz de teste.
    private static ToolsPage Tela(Raiz raiz, List<string> elevadas, List<string> removidos, bool confirma, List<string>? perguntas = null) =>
        new(new(), _ => { }, raiz.Key,
            elevated: task => { elevadas.Add(task); return Task.FromResult(new TaskReport(TaskOutcome.Ok, "ok")); },
            listApps: () => [], removeApp: n => { removidos.Add(n); return Task.CompletedTask; },
            defenderStatus: () => "", confirm: q => { perguntas?.Add(q); return confirma; });

    private static readonly BloatApp[] Apps = [new("A", "App A", "A_1"), new("B", "App B", "B_1")];

    [Fact]
    public async Task Apps_vem_desmarcados_e_nenhum_e_removido_sem_marcar()
    {
        using var raiz = new Raiz();
        List<string> elevadas = [], removidos = [], perguntas = [];
        using var tela = Tela(raiz, elevadas, removidos, confirma: true, perguntas);
        tela.ShowApps(Apps);

        Assert.All(tela.AppBoxes, a => Assert.False(a.Box.Checked));
        await tela.ApplyAsync();
        Assert.Empty(removidos);
        Assert.Empty(perguntas);
        Assert.Equal(["otimizacao-entrega-trim"], elevadas);
    }

    [Fact]
    public async Task Remove_so_o_marcado_depois_de_confirmar()
    {
        using var raiz = new Raiz();
        List<string> elevadas = [], removidos = [], perguntas = [];
        using var tela = Tela(raiz, elevadas, removidos, confirma: true, perguntas);
        tela.ShowApps(Apps);
        tela.AppBoxes[1].Box.Checked = true;

        await tela.ApplyAsync();
        Assert.Equal(["B_1"], removidos);
        Assert.Contains(ToolsPage.RemoveWarning, Assert.Single(perguntas));
    }

    [Fact]
    public async Task Confirmacao_negada_nao_remove_nem_aplica_nada()
    {
        using var raiz = new Raiz();
        List<string> elevadas = [], removidos = [];
        using var tela = Tela(raiz, elevadas, removidos, confirma: false);
        tela.ShowApps(Apps);
        tela.AppBoxes[0].Box.Checked = true;

        await tela.ApplyAsync();
        Assert.Empty(removidos);
        Assert.Empty(elevadas);
        Assert.Null(raiz.Valor(Tweaks.All[0]));
    }

    [Fact]
    public async Task Limpeza_sem_Lixeira_nao_pergunta_e_roda_limpeza()
    {
        using var raiz = new Raiz();
        List<string> elevadas = [], removidos = [], perguntas = [];
        using var tela = Tela(raiz, elevadas, removidos, confirma: false, perguntas);
        Assert.False(tela.Recycle.Checked); // desmarcada por padrão: não tem volta

        await tela.CleanAsync();
        Assert.Equal(["limpeza"], elevadas);
        Assert.Empty(perguntas);
    }

    [Theory]
    [InlineData(true, "limpeza-lixeira")]
    [InlineData(false, null)]
    public async Task Lixeira_so_depois_de_confirmar(bool confirma, string? esperada)
    {
        using var raiz = new Raiz();
        List<string> elevadas = [], removidos = [], perguntas = [];
        using var tela = Tela(raiz, elevadas, removidos, confirma, perguntas);
        tela.Recycle.Checked = true;

        await tela.CleanAsync();
        Assert.Equal(ToolsPage.RecycleWarning, Assert.Single(perguntas));
        Assert.Equal(esperada is null ? [] : [esperada], elevadas);
    }

    [Fact]
    public async Task Tela_aplica_e_desfaz_os_ajustes_da_conta_sem_UAC()
    {
        using var raiz = new Raiz();
        List<string> elevadas = [], removidos = [];
        using var tela = Tela(raiz, elevadas, removidos, confirma: true);
        foreach (var (t, box) in tela.TweakBoxes) box.Checked = !t.Machine;
        tela.Trim.Checked = false;

        await tela.ApplyAsync();
        Assert.Empty(elevadas);
        Assert.All(Tweaks.All.Where(t => !t.Machine), t => Assert.Equal(t.Value, raiz.Valor(t)));

        await tela.UndoAsync();
        Assert.Empty(elevadas); // nada de máquina no backup: não pede UAC
        Assert.All(Tweaks.All, t => Assert.Null(raiz.Valor(t)));
    }
}

public class ScheduledCleanupTests
{
    private sealed class FakeBackend : ISchedulerBackend
    {
        public List<TaskDef> Registered = [];
        public bool Exists = true;
        public bool Delete(string folder, string name) { var e = Exists; Exists = false; return e; }
        public void Register(TaskDef def) => Registered.Add(def);
    }

    [Fact]
    public void Horario_vai_e_volta_pela_gramatica_fechada()
    {
        foreach (var s in new[] { CleanupSchedule.Default, new(ScheduleFrequency.Monthly, 28, 0, 5), new(ScheduleFrequency.Weekly, 6, 23, 59) })
        {
            Assert.True(CleanupSchedule.TryParse(s.Format(), out var lido));
            Assert.Equal(s, lido);
        }
        Assert.Equal("semanal:0:12:00", CleanupSchedule.Default.Format());
    }

    [Theory]
    [InlineData("")]
    [InlineData("diario:0:12:00")]
    [InlineData("semanal:7:12:00")]   // dia da semana 0–6
    [InlineData("mensal:0:12:00")]    // dia do mês 1–28
    [InlineData("mensal:29:12:00")]
    [InlineData("semanal:0:24:00")]
    [InlineData("semanal:0:12:60")]
    [InlineData("semanal:0:12:00 ")]
    [InlineData("semanal:-1:12:00")]
    [InlineData("semanal:0:12:00:00")]
    [InlineData("semanal:0:12;calc")]
    public void Horario_invalido_e_recusado(string texto) => Assert.False(CleanupSchedule.TryParse(texto, out _));

    [Fact]
    public void Tarefa_elevada_com_horario_so_vale_se_o_horario_for_valido()
    {
        Assert.NotNull(ElevatedTask.Resolve("limpeza-agendar:semanal:0:12:00"));
        Assert.Null(ElevatedTask.Resolve("limpeza-agendar:semanal:0:99:00"));
        Assert.Null(ElevatedTask.Resolve("limpeza-agendar:"));
        Assert.Null(ElevatedTask.Resolve("limpeza-agendar"));
        Assert.NotNull(ElevatedTask.Resolve("limpeza-desagendar"));
        var report = @"C:\Temp\r.json";
        Assert.NotNull(ElevatedTask.Parse(["--tarefa", "limpeza-agendar:mensal:1:08:30", "--relatorio", report]));
        Assert.Null(ElevatedTask.Parse(["--tarefa", "limpeza-agendar:mensal:1:08:30;x", "--relatorio", report]));
    }

    [Fact]
    public void Definicao_tem_gatilho_acao_privilegio_e_condicoes()
    {
        var d = ScheduledCleanup.Definition(CleanupSchedule.Default, @"C:\Program Files\ThermoBook\ThermoBook.exe", @"PC\ana");
        Assert.Equal(@"\ThermoBook", d.Folder);
        Assert.Equal("Limpeza", d.Name);
        Assert.Equal("--limpeza-agendada", d.Arguments);
        Assert.Equal(@"C:\Program Files\ThermoBook\ThermoBook.exe", d.Exe);
        Assert.Equal(@"PC\ana", d.UserId);
        Assert.True(d.Highest);
        Assert.True(d.OnlyOnAc);
        Assert.True(d.StartWhenAvailable);
        Assert.Equal("PT1H", d.TimeLimit);
        Assert.Equal(CleanupSchedule.Default, d.Schedule);
    }

    [Fact]
    public void Agendar_copia_o_app_e_registra_a_tarefa_apontando_para_a_copia()
    {
        var backend = new FakeBackend();
        var r = ScheduledCleanup.Schedule(new(ScheduleFrequency.Monthly, 1, 8, 30), backend, () => @"C:\PF\ThermoBook.exe", @"PC\ana");
        Assert.Equal(TaskOutcome.Ok, r.Outcome);
        Assert.Contains("todo dia 1 do mês às 8:30", r.Message);
        Assert.Equal(@"C:\PF\ThermoBook.exe", Assert.Single(backend.Registered).Exe);
    }

    [Fact]
    public void Falha_ao_copiar_nao_registra_tarefa()
    {
        var backend = new FakeBackend();
        Assert.ThrowsAny<IOException>(() => ScheduledCleanup.Schedule(CleanupSchedule.Default, backend, () => throw new IOException("em uso"), "u"));
        Assert.Empty(backend.Registered);
    }

    [Fact]
    public void Desagendar_remove_a_tarefa_e_a_copia_e_aceita_nao_existir()
    {
        var backend = new FakeBackend();
        int copias = 0;
        Assert.Equal("Limpeza agendada desligada.", ScheduledCleanup.Unschedule(backend, () => copias++).Message);
        var de_novo = ScheduledCleanup.Unschedule(backend, () => copias++);
        Assert.Equal(TaskOutcome.Ok, de_novo.Outcome);
        Assert.Equal("Não havia limpeza agendada.", de_novo.Message);
        Assert.Equal(2, copias);
    }

    [Fact]
    public void Copia_em_uso_nao_impede_desagendar()
    {
        var r = ScheduledCleanup.Unschedule(new FakeBackend(), () => throw new IOException("em uso"));
        Assert.Equal(TaskOutcome.Ok, r.Outcome);
        Assert.Contains("em uso", r.Message);
    }

    private static string TempLog() => Path.Combine(Path.GetTempPath(), "thermobook-tests", Guid.NewGuid() + ".json");

    [Fact]
    public void Limpeza_agendada_grava_o_resultado_e_o_codigo_de_saida()
    {
        var path = TempLog();
        var quando = new DateTime(2026, 11, 22, 12, 0, 5);
        Assert.Equal(0, ScheduledCleanup.RunScheduled(path, () => new(TaskOutcome.Ok, "1,2 GB liberados"), quando));
        Assert.Equal(new CleanupLog(quando, TaskOutcome.Ok, "1,2 GB liberados"), ScheduledCleanup.ReadLog(path));

        Assert.Equal(1, ScheduledCleanup.RunScheduled(path, () => throw new InvalidOperationException("quebrou"), quando));
        var log = ScheduledCleanup.ReadLog(path)!;
        Assert.Equal((TaskOutcome.Failed, "quebrou"), (log.Outcome, log.Message));
    }

    [Fact]
    public void Log_ausente_ou_corrompido_e_nulo()
    {
        var path = TempLog();
        Assert.Null(ScheduledCleanup.ReadLog(path));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "{ nao e json");
        Assert.Null(ScheduledCleanup.ReadLog(path));
    }

    [Fact]
    public void Limpeza_agendada_nao_esvazia_a_Lixeira()
    {
        // A tarefa agendada chama Cleanup.Run(emptyRecycleBin: false) (RunScheduled()); a Lixeira não tem volta (D6).
        var fonte = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "ThermoBook", "Services", "CleanupSchedule.cs"));
        Assert.Contains("Cleanup.Run(emptyRecycleBin: false)", fonte);
        Assert.DoesNotContain("emptyRecycleBin: true", fonte);
    }

    // Verificação única contra o Agendador de verdade, sem administrador (tarefa não elevada, pasta de teste, removida no fim).
    [Fact]
    public void Agendador_real_registra_e_remove_uma_tarefa_sem_privilegio()
    {
        var backend = new ComScheduler();
        string pasta = @"\ThermoBookTeste" + Guid.NewGuid().ToString("N")[..6];
        var def = ScheduledCleanup.Definition(new(ScheduleFrequency.Weekly, 3, 9, 15), @"C:\Windows\System32\cmd.exe",
            System.Security.Principal.WindowsIdentity.GetCurrent().Name) with { Folder = pasta, Highest = false };
        try
        {
            backend.Register(def);
            backend.Register(def); // criar ou atualizar: repetir não falha
            Type t = Type.GetTypeFromProgID("Schedule.Service")!;
            dynamic svc = Activator.CreateInstance(t)!;
            svc.Connect();
            dynamic tarefa = svc.GetFolder(pasta).GetTask("Limpeza");
            Assert.Equal(1, (int)tarefa.Definition.Triggers.Count);
            Assert.Equal(8, (int)tarefa.Definition.Triggers[1].DaysOfWeek); // quarta (3) = bit 1<<3
            Assert.Equal("--limpeza-agendada", (string)tarefa.Definition.Actions[1].Arguments);
            Assert.True((bool)tarefa.Definition.Settings.DisallowStartIfOnBatteries);
        }
        finally
        {
            Assert.True(backend.Delete(pasta, "Limpeza") || true);
        }
        Assert.False(backend.Delete(pasta, "Limpeza")); // já não existe
    }
}

public class AutoModeTests
{
    private static readonly AutoRules Regras = new(Enabled: true, OnBattery: "Economia", OnAc: "Equilibrado",
        Programs: [new("ffmpeg", "Frio"), new("HandBrake.exe", "Máximo")]);

    private static string? Nome(PowerMode? m) => m?.Name;

    [Theory]
    [InlineData(false, false, new string[0], "Economia")]
    [InlineData(true, false, new string[0], "Equilibrado")]
    [InlineData(true, false, new[] { "explorer", "ffmpeg" }, "Frio")]
    [InlineData(false, false, new[] { "FFMPEG" }, "Frio")] // nome sem diferença de maiúsculas
    [InlineData(true, false, new[] { "handbrake" }, "Máximo")] // ".exe" na regra é ignorado
    [InlineData(true, false, new[] { "handbrake", "ffmpeg" }, "Frio")] // vale o primeiro da lista de regras
    [InlineData(true, true, new[] { "handbrake" }, "Frio")] // temperatura alta vence o programa
    public void Target_segue_a_prioridade(bool tomada, bool quente, string[] abertos, string esperado)
    {
        Assert.Equal(esperado, Nome(AutoMode.Target(Regras, tomada, quente, abertos)));
    }

    [Fact]
    public void Regra_sem_modo_ou_com_modo_desconhecido_nao_vale()
    {
        var regras = Regras with { OnBattery = null, Programs = [new("ffmpeg", "Turbo"), new("x", "Alto")] };
        Assert.Null(AutoMode.Target(regras, onAc: false, hot: false, ["ffmpeg"]));
        Assert.Equal("Alto", Nome(AutoMode.Target(regras, false, false, ["ffmpeg", "x.exe"])));
    }

    [Fact]
    public void Regras_vazias_nao_pedem_nada()
    {
        var vazias = new AutoRules(Enabled: true, OnBattery: null, OnAc: null);
        Assert.Null(AutoMode.Target(vazias, true, false, ["ffmpeg"]));
        Assert.Null(new AutoMode().Decide(vazias, true, false, ["ffmpeg"]));
    }

    [Fact]
    public void So_pede_troca_quando_o_modo_desejado_muda()
    {
        var auto = new AutoMode();
        Assert.Equal("Equilibrado", Nome(auto.Decide(Regras, true, false, [])));
        Assert.Null(auto.Decide(Regras, true, false, [])); // nada mudou: não reaplica (troca manual continua valendo)
        Assert.Equal("Frio", Nome(auto.Decide(Regras, true, false, ["ffmpeg"])));
        Assert.Null(auto.Decide(Regras, false, false, ["ffmpeg"])); // tirou da tomada, mas o programa ainda manda: Frio de novo não
        Assert.Equal("Economia", Nome(auto.Decide(Regras, false, false, [])));
    }

    [Fact]
    public void Temperatura_alta_segura_o_frio_e_depois_volta()
    {
        var auto = new AutoMode();
        auto.Decide(Regras, true, false, []);
        Assert.Equal("Frio", Nome(auto.Decide(Regras, true, true, [])));
        Assert.Equal("Equilibrado", Nome(auto.Decide(Regras, true, false, [])));
    }

    [Fact]
    public void Desligado_nao_pede_nada_e_ao_religar_aplica_de_novo()
    {
        var auto = new AutoMode();
        Assert.Null(auto.Decide(null, true, false, []));
        Assert.Null(auto.Decide(Regras with { Enabled = false }, true, false, []));
        Assert.Equal("Equilibrado", Nome(auto.Decide(Regras, true, false, [])));
        auto.Decide(Regras with { Enabled = false }, true, false, []);
        Assert.Equal("Equilibrado", Nome(auto.Decide(Regras, true, false, []))); // mesmo modo de antes, mas religou
    }

    [Fact]
    public void Editar_as_regras_aplica_se_o_modo_desejado_mudar()
    {
        var auto = new AutoMode();
        auto.Decide(Regras, true, false, []);
        Assert.Equal("Alto", Nome(auto.Decide(Regras with { OnAc = "Alto" }, true, false, [])));
    }

    [Fact]
    public void Padrao_vem_desligado_com_as_regras_do_backlog()
    {
        var d = AutoRules.Default;
        Assert.False(d.Enabled);
        Assert.Equal("Economia", d.OnBattery);
        Assert.Equal("Equilibrado", d.OnAc);
        Assert.Equal(new ProgramRule("ffmpeg", "Frio"), Assert.Single(d.Programs!));
    }

    private static string TempFile() => Path.Combine(Path.GetTempPath(), "thermobook-tests", Guid.NewGuid() + ".json");

    [Fact]
    public void Settings_antigo_carrega_sem_regras()
    {
        var path = TempFile();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, """{ "Profile": "Frio", "AutoCool": true }""");
        Assert.Null(new SettingsStore(path).Load().Auto);
    }

    [Fact]
    public void Regras_vao_e_voltam_do_settings()
    {
        var store = new SettingsStore(TempFile());
        store.Save(new Settings(Auto: Regras));
        var lido = store.Load().Auto!;
        Assert.True(lido.Enabled);
        Assert.Equal("Equilibrado", lido.OnAc);
        Assert.Equal(Regras.Programs, lido.Programs);
    }

    [Fact]
    public void Regras_com_campos_faltando_ganham_o_padrao()
    {
        var path = TempFile();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, """{ "Auto": { "Enabled": true } }""");
        var lido = new SettingsStore(path).Load().Auto!;
        Assert.Equal(("Economia", "Equilibrado"), (lido.OnBattery, lido.OnAc));
        Assert.Null(lido.Programs);
    }

    [Fact]
    public void Alerta_fica_ativo_ate_esfriar_5_graus()
    {
        var t0 = new DateTime(2026, 1, 1, 12, 0, 0);
        var alerta = new TempAlert(90, TimeSpan.FromSeconds(30));
        alerta.Check(95, t0);
        Assert.False(alerta.Active);
        alerta.Check(95, t0.AddSeconds(30));
        Assert.True(alerta.Active);
        alerta.Check(87, t0.AddSeconds(40));
        Assert.True(alerta.Active);
        alerta.Check(84, t0.AddSeconds(50));
        Assert.False(alerta.Active);
    }
}
