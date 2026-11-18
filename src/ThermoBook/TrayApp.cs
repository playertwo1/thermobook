using System.Diagnostics;
using ThermoBook.Services;

namespace ThermoBook;

/// <summary>Ícone na bandeja com a temperatura, menu de perfis, painel e alerta de temperatura.</summary>
internal sealed class TrayApp : ApplicationContext
{
    private const string SamsungSettingsPackage = "SAMSUNGELECTRONICSCO.LTD.SamsungSettings1.5_3c1yjt4zspk6g";

    private readonly SettingsStore _store;
    private Settings _settings;
    private readonly SensorService _sensors = new();
    private readonly StartupService _startup = new();
    private readonly TempAlert _alert;
    private readonly MainForm _panel;
    private readonly RegisteredWaitHandle _openPanelWait;
    private readonly NotifyIcon _tray;
    private readonly ContextMenuStrip _menu = new();
    private readonly ToolStripMenuItem _status;
    private readonly Dictionary<PowerMode, ToolStripMenuItem> _profileItems = [];
    private readonly ToolStripMenuItem _background;
    private readonly PillForm _pill = new();
    private readonly PlanWatcher _planWatcher;
    private readonly HotkeyWindow _hotkeyWindow;
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 1000 };
    private Icon _icon;
    private double? _shownTemp;
    private string _profileLabel = "?";
    private uint? _turbo;
    private int _ticks;
    private bool _alertBalloonShown; // o clique no balão só troca de perfil se o balão for o do alerta

    public TrayApp(SettingsStore store, WaitHandle openPanelSignal, bool showPanel)
    {
        _store = store;
        _settings = store.Load();
        _alert = new TempAlert(_settings.AlertCelsius, TimeSpan.FromSeconds(_settings.AlertSeconds));

        Action? openFan = Directory.Exists(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Packages", SamsungSettingsPackage))
            ? () => Process.Start("explorer.exe", $@"shell:AppsFolder\{SamsungSettingsPackage}!App")
            : null;
        _panel = new MainForm(m => ApplyMode(m), (turbo, limit) => ApplyPower(turbo, limit), openFan);
        _ = _panel.Handle; // cria a janela (escondida) para o BeginInvoke do sinal de outra instância
        _openPanelWait = ThreadPool.RegisterWaitForSingleObject(openPanelSignal,
            (_, _) => _panel.BeginInvoke(new Action(ShowPanel)), null, Timeout.Infinite, executeOnlyOnce: false);

        _status = new ToolStripMenuItem("Lendo sensores...") { Enabled = false };
        _menu.Items.Add(_status);
        _menu.Items.Add(new ToolStripSeparator());
        _menu.Items.Add(new ToolStripMenuItem("Abrir painel", null, (_, _) => ShowPanel()) { Font = new Font(_menu.Font, FontStyle.Bold) });
        _menu.Items.Add(new ToolStripSeparator());
        foreach (var p in PowerService.Modes)
        {
            var item = new ToolStripMenuItem(p.Name, null, (_, _) => ApplyMode(p)) { ToolTipText = p.Description };
            _profileItems[p] = item;
            _menu.Items.Add(item);
        }
        _menu.Items.Add(new ToolStripSeparator());
        _menu.Items.Add(new ToolStripMenuItem("Desligar a tela", null, (_, _) => DisplayService.TurnOff())
            { ToolTipText = "Desliga o monitor sem suspender o PC. Mexer o mouse religa." });
        // Provisório da 9.1 (a 9.4 cuida de mostrar/ocultar e lembrar a escolha).
        var pillItem = new ToolStripMenuItem("Mostrar pílula") { CheckOnClick = true };
        pillItem.Click += (_, _) => { if (pillItem.Checked) _pill.Show(); else _pill.Hide(); };
        _menu.Items.Add(pillItem);
        _background = new ToolStripMenuItem("Modo segundo plano", null, (_, _) => ToggleBackground())
        {
            Checked = BackgroundMode.IsOn(_settings),
            ToolTipText = "Tampa fechada e inatividade não suspendem o PC. Ao desligar, volta ao que estava antes.",
        };
        _menu.Items.Add(_background);
        if (openFan is not null)
            _menu.Items.Add("Ventoinha (Configurações Samsung)...", null, (_, _) => openFan());

        var startup = new ToolStripMenuItem("Iniciar com o Windows") { CheckOnClick = true, Checked = _startup.IsEnabled };
        startup.Click += (_, _) =>
        {
            try { _startup.Set(startup.Checked, Environment.ProcessPath!); }
            catch (Exception ex) { ShowError($"Não consegui mudar a inicialização: {ex.Message}"); }
            startup.Checked = _startup.IsEnabled;
        };
        _menu.Items.Add(startup);

        var autoCool = new ToolStripMenuItem($"Trocar para Frio acima de {_settings.AlertCelsius:0} °C")
        {
            CheckOnClick = true, Checked = _settings.AutoCool,
            ToolTipText = $"Quando passar de {_settings.AlertCelsius:0} °C por {_settings.AlertSeconds} s, desliga o turbo sozinho.",
        };
        autoCool.Click += (_, _) => SaveSettings(_settings with { AutoCool = autoCool.Checked });
        _menu.Items.Add(autoCool);
        _menu.Items.Add(new ToolStripSeparator());
        _menu.Items.Add("Sair", null, (_, _) => ExitThread());
        _menu.ShowItemToolTips = true;

        _icon = TrayIconRenderer.Render(null, IconSize, BackgroundMode.IsOn(_settings));
        _tray = new NotifyIcon { Icon = _icon, Text = "ThermoBook", ContextMenuStrip = _menu, Visible = true };
        _tray.MouseUp += (_, e) =>
        {
            if (e.Button == MouseButtons.Left) ShowPanel();
        };
        _tray.BalloonTipClicked += (_, _) =>
        {
            if (_alertBalloonShown) ApplyMode(Cold);
        };

        ReapplySavedProfile();
        if (BackgroundMode.IsOn(_settings)) ApplyBackground(); // continua ligado depois de fechar o app ou reiniciar o PC
        _planWatcher = new PlanWatcher(OnPlanChanged);
        _hotkeyWindow = new HotkeyWindow(DisplayService.TurnOff);
        if (!string.IsNullOrWhiteSpace(_settings.Hotkey) && !_hotkeyWindow.Register(_settings.Hotkey))
            ShowError($"Não consegui registrar o atalho {_settings.Hotkey} (já em uso ou inválido). O ThermoBook segue sem ele.");
        RefreshPowerState(syncControls: true);
        _timer.Tick += (_, _) => OnTick();
        _timer.Start();
        if (showPanel) ShowPanel();
    }

    private static int IconSize => SystemInformation.SmallIconSize.Width;

    private static PowerMode Cold => PowerService.ModeByName("Frio")!;

    private void ShowPanel()
    {
        RefreshPowerState(syncControls: true);
        _panel.Show();
        if (_panel.WindowState == FormWindowState.Minimized) _panel.WindowState = FormWindowState.Normal;
        _panel.Activate();
    }

    /// <summary>Outro programa trocou o plano de energia: o modo salvo é gravado no plano novo.</summary>
    private void OnPlanChanged()
    {
        ReapplySavedProfile();
        if (BackgroundMode.IsOn(_settings)) ApplyBackground();
        RefreshPowerState(syncControls: true);
    }

    /// <summary>Liga no plano ativo (guarda o snapshot dele se ainda não houver).</summary>
    private void ApplyBackground()
    {
        try
        {
            _settings = BackgroundMode.Enable(_settings, PowerService.ActivePlan(), PowerService.ReadPlan, PowerService.WritePlan, SaveSettings);
            PowerService.Commit();
        }
        catch (Exception ex) { ShowError($"Não consegui ligar o modo segundo plano: {ex.Message}"); }
    }

    private void ToggleBackground()
    {
        try
        {
            if (BackgroundMode.IsOn(_settings))
            {
                _settings = BackgroundMode.Disable(_settings, PowerService.WritePlan, SaveSettings);
                PowerService.Commit();
            }
            else ApplyBackground();
        }
        catch (Exception ex) { ShowError($"Não consegui mudar o modo segundo plano: {ex.Message}"); }
        _background.Checked = BackgroundMode.IsOn(_settings);
        _pill.SetMode(PowerService.Current(), BackgroundMode.IsOn(_settings));
        UpdateIcon(force: true);
    }

    private void ReapplySavedProfile()
    {
        var saved = PowerService.ModeByName(_settings.Profile);
        if (saved is null || PowerService.Current() == saved) return;
        try { PowerService.Apply(saved); }
        catch (Exception ex) { ShowError($"Não consegui reaplicar o perfil {saved.Name}: {ex.Message}"); }
    }

    private bool ApplyMode(PowerMode mode) => Apply(mode.Name, () => PowerService.Apply(mode), mode.Name);

    /// <summary>Combinação personalizada (painel): não é reaplicada ao iniciar.</summary>
    private bool ApplyPower(uint turbo, uint limit) => Apply("personalizado", () => PowerService.Apply(turbo, limit), null);

    private bool Apply(string label, Action apply, string? saveAs)
    {
        bool ok = true;
        try
        {
            apply();
            SaveSettings(_settings with { Profile = saveAs });
        }
        catch (Exception ex)
        {
            ShowError($"Não consegui aplicar o modo {label}: {ex.Message}");
            ok = false;
        }
        RefreshPowerState(syncControls: true);
        UpdateIcon(force: true);
        return ok;
    }

    private void SaveSettings(Settings settings)
    {
        _settings = settings;
        try { _store.Save(settings); }
        catch (Exception ex) { ShowError($"Não consegui salvar as configurações: {ex.Message}"); }
    }

    private void RefreshPowerState(bool syncControls = false)
    {
        try
        {
            uint turbo = PowerService.GetTurbo(), limit = PowerService.GetLimit();
            var current = PowerService.Current();
            foreach (var (p, item) in _profileItems) item.Checked = p == current;
            _profileLabel = current?.Name ?? $"Personalizado (turbo {PowerService.TurboNames.GetValueOrDefault(turbo, turbo.ToString())}, {limit}%)";
            _turbo = turbo;
            _panel.ShowPower(turbo, limit, current, syncControls);
            _pill.SetMode(current, BackgroundMode.IsOn(_settings));
        }
        catch (Exception ex)
        {
            _profileLabel = "erro ao ler energia";
            _turbo = null;
            Debug.WriteLine(ex);
        }
    }

    private void OnTick()
    {
        // Só atualiza o que é mostrado (ex.: modo trocado nas Configurações do Windows vira "Personalizado"); a reaplicação vem do PlanWatcher.
        if (++_ticks % 5 == 0) RefreshPowerState();
        UpdateIcon(force: false);

        var s = _sensors.Latest;
        if (s is null) return;
        _panel.Push(s);
        _pill.Push(s);
        if (_alert.Check(s.TempMax, DateTime.Now)) OnOverheat(s.TempMax);
    }

    private void OnOverheat(double celsius)
    {
        string text = $"Está em {celsius:0} °C há mais de {_settings.AlertSeconds} s.";
        bool turboOn = _turbo is > 0; // Frio e Silencioso já estão com o turbo desligado
        _alertBalloonShown = false;
        if (turboOn && _settings.AutoCool && ApplyMode(Cold))
            text += " Troquei para o perfil Frio.";
        else if (turboOn)
        {
            text += " Clique aqui para trocar para o perfil Frio.";
            _alertBalloonShown = true;
        }
        _tray.ShowBalloonTip(10_000, "ThermoBook: temperatura alta", text, ToolTipIcon.Warning);
    }

    private void UpdateIcon(bool force)
    {
        var s = _sensors.Latest;
        if (s is null) return;

        double temp = Math.Round(s.TempMax);
        if (force || temp != _shownTemp)
        {
            var old = _icon;
            _icon = TrayIconRenderer.Render(temp, IconSize, BackgroundMode.IsOn(_settings));
            _tray.Icon = _icon;
            old.Dispose();
            _shownTemp = temp;
        }

        string summary = $"{temp:0} °C · CPU {s.CpuUse:0}% · GPU {s.Gpu:0}% · RAM {s.RamPercent:0}%";
        _status.Text = summary;
        string tip = $"ThermoBook - {temp:0} °C\nCPU {s.CpuUse:0}% · GPU {s.Gpu:0}% · RAM {s.RamPercent:0}%\nPerfil: {_profileLabel}";
        _tray.Text = tip.Length > 127 ? tip[..127] : tip; // limite do Windows para o tooltip
    }

    private void ShowError(string message)
    {
        _alertBalloonShown = false;
        _tray.ShowBalloonTip(5000, "ThermoBook", message, ToolTipIcon.Error);
    }

    protected override void ExitThreadCore()
    {
        _timer.Stop();
        _planWatcher.Dispose();
        _hotkeyWindow.Dispose();
        _openPanelWait.Unregister(null);
        _tray.Visible = false;
        _tray.Dispose();
        _panel.Dispose();
        _pill.Dispose();
        _sensors.Dispose();
        _icon.Dispose();
        base.ExitThreadCore();
    }

    /// <summary>Janela invisível que recebe o aviso de troca do plano de energia ativo.</summary>
    private sealed class PlanWatcher : NativeWindow, IDisposable
    {
        private const int WM_POWERBROADCAST = 0x218, PBT_POWERSETTINGCHANGE = 0x8013;
        private readonly Action _onChange;
        private readonly IntPtr _registration;
        private Guid _plan = PowerService.ActivePlan();

        public PlanWatcher(Action onChange)
        {
            _onChange = onChange;
            CreateHandle(new CreateParams());
            _registration = PowerService.RegisterPlanChange(Handle);
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_POWERBROADCAST && (int)m.WParam == PBT_POWERSETTINGCHANGE)
            {
                // Só reage a troca de plano de verdade: o aviso também chega ao registrar e quando o próprio app reativa o plano (Commit), o que daria laço.
                var plan = PowerService.ActivePlan();
                if (plan != _plan) { _plan = plan; _onChange(); }
            }
            base.WndProc(ref m);
        }

        public void Dispose()
        {
            if (_registration != IntPtr.Zero) PowerService.UnregisterPlanChange(_registration);
            DestroyHandle();
        }
    }

    /// <summary>Janela invisível que recebe WM_HOTKEY do atalho de desligar a tela.</summary>
    private sealed class HotkeyWindow : NativeWindow, IDisposable
    {
        private const int WM_HOTKEY = 0x0312, Id = 1;
        private readonly Action _onPressed;
        private bool _registered;

        public HotkeyWindow(Action onPressed)
        {
            _onPressed = onPressed;
            CreateHandle(new CreateParams());
        }

        public bool Register(string text) => _registered = HotkeyService.Register(Handle, Id, text);

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_HOTKEY && (int)m.WParam == Id) _onPressed();
            base.WndProc(ref m);
        }

        public void Dispose()
        {
            if (_registered) HotkeyService.Unregister(Handle, Id);
            DestroyHandle();
        }
    }
}
