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
    private TempAlert _alert;
    private readonly MainForm _panel;
    private readonly SettingsPage _settingsPage;
    private readonly ToolStripMenuItem _pillItem, _startupItem, _autoCoolItem, _autoModeItem;
    private readonly RegisteredWaitHandle _openPanelWait;
    private readonly NotifyIcon _tray;
    private readonly ContextMenuStrip _menu = new();
    private readonly ToolStripMenuItem _status;
    private readonly Dictionary<PowerMode, ToolStripMenuItem> _profileItems = [];
    private readonly ToolStripMenuItem _background;
    private readonly PillForm _pill = new();
    private readonly PillFlyout _flyout;
    private readonly QuickPanel _quick;
    private readonly ModeStore _modeState = new();
    private bool _fullscreenApp;
    private readonly PlanWatcher _planWatcher;
    private readonly HotkeyWindow _hotkeyWindow;
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 1000 };
    private Icon _icon;
    private double? _shownTemp;
    private string _profileLabel = "?";
    private uint? _turbo;
    private int _ticks;
    private bool _alertBalloonShown; // o clique no balão só troca de perfil se o balão for o do alerta
    private readonly AutoMode _auto = new();
    private readonly System.Threading.Timer _processTimer;
    private readonly HistoryWriter _history = new(HistoryWriter.DefaultDir);
    private readonly System.Threading.Timer _historyTimer;
    private volatile string _modeName = "";
    private DateTime _pruned;
    private volatile string[] _running = []; // nomes dos processos abertos, lidos a cada 5 s fora da thread da interface

    public TrayApp(SettingsStore store, WaitHandle openPanelSignal, bool showPanel)
    {
        _store = store;
        _settings = store.Load();
        _alert = new TempAlert(_settings.AlertCelsius, TimeSpan.FromSeconds(_settings.AlertSeconds));

        Action? openFan = Directory.Exists(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Packages", SamsungSettingsPackage))
            ? () => Process.Start("explorer.exe", $@"shell:AppsFolder\{SamsungSettingsPackage}!App")
            : null;
        var tools = new ToolsPage(new(_settings.TweakBackup ?? []), b => SaveSettings(_settings with { TweakBackup = b }),
            getSchedule: () => _settings.CleanupAuto, setSchedule: s => SaveSettings(_settings with { CleanupAuto = s }));
        _settingsPage = new SettingsPage(ChangeSettings, SetStartup);
        _panel = new MainForm(m => ApplyMode(m), (turbo, limit) => ApplyPower(turbo, limit), openFan, tools, _settingsPage);
        _ = _panel.Handle; // cria a janela (escondida) para o BeginInvoke do sinal de outra instância
        _openPanelWait = ThreadPool.RegisterWaitForSingleObject(openPanelSignal,
            (_, _) => _panel.BeginInvoke(() => ShowPanel()), null, Timeout.Infinite, executeOnlyOnce: false);

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
        _menu.Items.Add(new ToolStripMenuItem("Ferramentas...", null, (_, _) => ShowPanel(PanelTab.Tools))
            { ToolTipText = "Limpeza e otimização do Windows (abre o painel na aba Ferramentas)." });
        _flyout = new PillFlyout(_pill);
        _quick = new QuickPanel(m => ApplyMode(m), ToggleBackground, DisplayService.TurnOff, () => ShowPanel(), () => ShowPanel(PanelTab.Tools));
        _pill.Clicked += ToggleQuickPanel;
        _pill.Restore(_settings.PillMonitor, _settings.PillPositions);
        _pill.PlacementChanged += (key, pos) =>
            SaveSettings(_settings with { PillMonitor = key, PillPositions = new(_settings.PillPositions ?? []) { [key] = pos } });
        _pillItem = new ToolStripMenuItem("Mostrar pílula") { CheckOnClick = true };
        _pillItem.Click += (_, _) => ChangeSettings(s => s with { ShowPill = _pillItem.Checked });
        _menu.Items.Add(_pillItem);
        _pill.ContextMenuStrip = _menu; // botão direito na pílula abre o mesmo menu da bandeja
        _background = new ToolStripMenuItem("Modo segundo plano", null, (_, _) => ToggleBackground())
        {
            Checked = BackgroundMode.IsOn(_settings),
            ToolTipText = "Tampa fechada e inatividade não suspendem o PC. Ao desligar, volta ao que estava antes.",
        };
        _menu.Items.Add(_background);
        _autoModeItem = new ToolStripMenuItem("Modo automático") { CheckOnClick = true, ToolTipText = "Troca o modo pela tomada/bateria e por programas abertos (regras em Configurações)." };
        _autoModeItem.Click += (_, _) => ChangeSettings(s => s with { Auto = (s.Auto ?? AutoRules.Default) with { Enabled = _autoModeItem.Checked } });
        _menu.Items.Add(_autoModeItem);
        if (openFan is not null)
            _menu.Items.Add("Ventoinha (Configurações Samsung)...", null, (_, _) => openFan());

        _startupItem = new ToolStripMenuItem("Iniciar com o Windows") { CheckOnClick = true };
        _startupItem.Click += (_, _) => SetStartup(_startupItem.Checked);
        _menu.Items.Add(_startupItem);

        _autoCoolItem = new ToolStripMenuItem { CheckOnClick = true };
        _autoCoolItem.Click += (_, _) => ChangeSettings(s => s with { AutoCool = _autoCoolItem.Checked });
        _menu.Items.Add(_autoCoolItem);
        SyncSettingsViews();
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

        _modeState.Changed += OnModeStateChanged; // antes da primeira leitura: todas as janelas recebem o estado inicial
        ReapplySavedProfile();
        if (BackgroundMode.IsOn(_settings)) ApplyBackground(); // continua ligado depois de fechar o app ou reiniciar o PC
        _planWatcher = new PlanWatcher(OnPlanChanged);
        _hotkeyWindow = new HotkeyWindow(DisplayService.TurnOff);
        if (!_hotkeyWindow.Register(_settings.Hotkey))
            ShowError($"Não consegui registrar o atalho {_settings.Hotkey} (já em uso ou inválido). O ThermoBook segue sem ele.");
        RefreshPowerState(syncControls: true);
        UpdatePillVisibility();
        _processTimer = new System.Threading.Timer(_ => PollProcesses(), null, 0, 5000);
        _historyTimer = new System.Threading.Timer(_ => RecordHistory(), null, 1000, 1000);
        _timer.Tick += (_, _) => OnTick();
        _timer.Start();
        if (showPanel) ShowPanel();
    }

    private static int IconSize => SystemInformation.SmallIconSize.Width;

    private static PowerMode Cold => PowerService.ModeByName("Frio")!;

    /// <param name="tab">null mantém a aba em que o painel estava.</param>
    private void ShowPanel(PanelTab? tab = null)
    {
        if (tab is { } t) _panel.ShowTab(t);
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
        RefreshPowerState(); // publica o novo estado a todas as janelas na hora, sem esperar a leitura de sensores
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

    /// <summary>Única porta para mudar configurações (aba Configurações e menu da bandeja). Devolve o erro para mostrar, ou null.</summary>
    private string? ChangeSettings(Func<Settings, Settings> edit)
    {
        var next = edit(_settings);
        string? error = null;
        if (next.Hotkey != _settings.Hotkey && !_hotkeyWindow.Register(next.Hotkey))
        {
            error = $"Não consegui registrar {next.Hotkey} (já em uso por outro programa). Ficou o atalho anterior.";
            _hotkeyWindow.Register(_settings.Hotkey);
            next = next with { Hotkey = _settings.Hotkey };
        }
        if (next.AlertCelsius != _settings.AlertCelsius || next.AlertSeconds != _settings.AlertSeconds)
            _alert = new TempAlert(next.AlertCelsius, TimeSpan.FromSeconds(next.AlertSeconds));
        SaveSettings(next);
        UpdatePillVisibility();
        SyncSettingsViews();
        return error;
    }

    private void SetStartup(bool on)
    {
        try { _startup.Set(on, Environment.ProcessPath!); }
        catch (Exception ex) { ShowError($"Não consegui mudar a inicialização: {ex.Message}"); }
        SyncSettingsViews();
    }

    /// <summary>Menu da bandeja e aba Configurações mostram o que está salvo.</summary>
    private void SyncSettingsViews()
    {
        bool startup = _startup.IsEnabled;
        _pillItem.Checked = _settings.ShowPill;
        _startupItem.Checked = startup;
        _autoCoolItem.Checked = _settings.AutoCool;
        _autoModeItem.Checked = _settings.Auto is { Enabled: true };
        _autoCoolItem.Text = $"Trocar para Frio acima de {_settings.AlertCelsius:0} °C";
        _autoCoolItem.ToolTipText = $"Quando passar de {_settings.AlertCelsius:0} °C por {_settings.AlertSeconds} s, desliga o turbo sozinho.";
        _settingsPage.Show(_settings, startup);
    }

    private void SaveSettings(Settings settings)
    {
        _settings = settings;
        try { _store.Save(settings); }
        catch (Exception ex) { ShowError($"Não consegui salvar as configurações: {ex.Message}"); }
    }

    /// <summary>Única leitura do sistema para a interface: lê o plano ativo e publica em <see cref="_modeState"/>.</summary>
    private void RefreshPowerState(bool syncControls = false)
    {
        try
        {
            uint turbo = PowerService.GetTurbo(), limit = PowerService.GetLimit();
            var current = PowerService.Current();
            _modeState.Set(new ModeState(current, turbo, limit, BackgroundMode.IsOn(_settings), current is not null && current == _auto.Wanted));
            // Sem mudança o evento não dispara; ao abrir o painel ou depois de aplicar, os controles precisam acompanhar mesmo assim.
            if (syncControls) _panel.ShowPower(turbo, limit, current, true);
        }
        catch (Exception ex)
        {
            _profileLabel = "erro ao ler energia";
            _turbo = null;
            Debug.WriteLine(ex);
        }
    }

    /// <summary>Todas as janelas que mostram o modo ou o segundo plano são atualizadas aqui, e só aqui.</summary>
    private void OnModeStateChanged(ModeState s)
    {
        foreach (var (p, item) in _profileItems) item.Checked = p == s.Mode;
        _background.Checked = s.Background;
        _profileLabel = (s.Mode?.Name ?? $"Personalizado (turbo {PowerService.TurboNames.GetValueOrDefault(s.Turbo, s.Turbo.ToString())}, {s.Limit}%)")
            + (s.Auto ? " · automático" : "");
        _turbo = s.Turbo;
        _modeName = s.Mode?.Name ?? "Personalizado";
        _panel.ShowPower(s.Turbo, s.Limit, s.Mode, syncControls: false);
        _pill.SetMode(s.Mode, s.Background, s.Auto);
        _quick.SetState(s.Mode, s.Background, s.Auto);
    }

    /// <summary>Thread do timer (nunca a da interface): soma a última leitura ao minuto e grava quando ele fecha.</summary>
    private void RecordHistory()
    {
        try
        {
            if (!_settings.History || _sensors.Latest is not { } s) return;
            var now = DateTime.Now;
            if (_pruned != now.Date) { _pruned = now.Date; _history.Prune(now); }
            _history.Add(now, HistorySample.From(s, _modeName));
        }
        catch (Exception ex) { Debug.WriteLine(ex); } // exceção no timer derrubaria o app
    }

    /// <summary>Thread do timer: só lista processos se alguma regra de programa estiver valendo.</summary>
    private void PollProcesses()
    {
        try
        {
            if (_settings.Auto is not { Enabled: true, Programs.Count: > 0 }) { _running = []; return; }
            var processes = Process.GetProcesses();
            try { _running = processes.Select(p => p.ProcessName).ToArray(); }
            finally { foreach (var p in processes) p.Dispose(); }
        }
        catch (Exception ex) { Debug.WriteLine(ex); } // exceção no timer derrubaria o app
    }

    /// <summary>Aplica o modo que as regras pedem, só quando o pedido muda (troca manual vale até lá).</summary>
    private void RunAutoMode()
    {
        // Unknown (desktop sem bateria) conta como tomada.
        bool onAc = SystemInformation.PowerStatus.PowerLineStatus != PowerLineStatus.Offline;
        bool hot = _settings.AutoCool && _alert.Active;
        var before = _auto.Wanted;
        var mode = _auto.Decide(_settings.Auto, onAc, hot, _running);
        if (mode is not null && _modeState.State?.Mode != mode) ApplyMode(mode);
        else if (_auto.Wanted != before) RefreshPowerState(); // só o selo "automático" muda
    }

    /// <summary>Pílula visível se o usuário quer e nenhum app em tela cheia (jogo, vídeo, apresentação) está na frente.</summary>
    private void UpdatePillVisibility()
    {
        _fullscreenApp = FullscreenGuard.IsFullscreenAppActive();
        bool show = _settings.ShowPill && !_fullscreenApp;
        if (show == _pill.Visible) return;
        if (show) _pill.Show(); else _pill.Hide();
    }

    private void ToggleQuickPanel()
    {
        if (_quick.Visible) { _quick.Hide(); return; }
        float k = _pill.DeviceDpi / 96f;
        _quick.Location = FlyoutPlacement.Place(_pill.Bounds, _pill.PillOrientation, _quick.Size, Screen.FromControl(_pill).WorkingArea, (int)Math.Round(8 * k));
        _quick.Show();
        _quick.Activate();
        _quick.Focus();
    }

    private void OnTick()
    {
        // Só atualiza o que é mostrado (ex.: modo trocado nas Configurações do Windows vira "Personalizado"); a reaplicação vem do PlanWatcher.
        if (++_ticks % 5 == 0) RefreshPowerState();
        if (_ticks % 2 == 0) UpdatePillVisibility();
        RunAutoMode();
        UpdateIcon(force: false);

        var s = _sensors.Latest;
        if (s is null) return;
        _panel.Push(s);
        _pill.Push(s);
        _flyout.Update(s);
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
        _processTimer.Dispose();
        _historyTimer.Dispose();
        _history.Flush(); // o minuto em andamento não se perde ao sair
        _planWatcher.Dispose();
        _hotkeyWindow.Dispose();
        _openPanelWait.Unregister(null);
        _tray.Visible = false;
        _tray.Dispose();
        _panel.Dispose();
        _quick.Dispose();
        _flyout.Dispose();
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

        /// <summary>Troca o atalho (solta o anterior). Vazio só solta e conta como sucesso.</summary>
        public bool Register(string text)
        {
            if (_registered) HotkeyService.Unregister(Handle, Id);
            _registered = false;
            return string.IsNullOrWhiteSpace(text) || (_registered = HotkeyService.Register(Handle, Id, text));
        }

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
