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
    private readonly Dictionary<PowerProfile, ToolStripMenuItem> _profileItems = [];
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
        _panel = new MainForm((turbo, limit) => ApplyPower(turbo, limit), openFan);
        _ = _panel.Handle; // cria a janela (escondida) para o BeginInvoke do sinal de outra instância
        _openPanelWait = ThreadPool.RegisterWaitForSingleObject(openPanelSignal,
            (_, _) => _panel.BeginInvoke(new Action(ShowPanel)), null, Timeout.Infinite, executeOnlyOnce: false);

        _status = new ToolStripMenuItem("Lendo sensores...") { Enabled = false };
        _menu.Items.Add(_status);
        _menu.Items.Add(new ToolStripSeparator());
        _menu.Items.Add(new ToolStripMenuItem("Abrir painel", null, (_, _) => ShowPanel()) { Font = new Font(_menu.Font, FontStyle.Bold) });
        _menu.Items.Add(new ToolStripSeparator());
        foreach (var p in PowerService.Profiles)
        {
            var item = new ToolStripMenuItem(p.Name, null, (_, _) => ApplyPower(p.Turbo, p.Limit)) { ToolTipText = p.Description };
            _profileItems[p] = item;
            _menu.Items.Add(item);
        }
        _menu.Items.Add(new ToolStripSeparator());
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

        _icon = TrayIconRenderer.Render(null, IconSize);
        _tray = new NotifyIcon { Icon = _icon, Text = "ThermoBook", ContextMenuStrip = _menu, Visible = true };
        _tray.MouseUp += (_, e) =>
        {
            if (e.Button == MouseButtons.Left) ShowPanel();
        };
        _tray.BalloonTipClicked += (_, _) =>
        {
            if (_alertBalloonShown) ApplyPower(Cold.Turbo, Cold.Limit);
        };

        ReapplySavedProfile();
        RefreshPowerState(syncControls: true);
        _timer.Tick += (_, _) => OnTick();
        _timer.Start();
        if (showPanel) ShowPanel();
    }

    private static int IconSize => SystemInformation.SmallIconSize.Width;

    private static PowerProfile Cold => PowerService.Profiles.First(p => p.Name == "Frio");

    private void ShowPanel()
    {
        RefreshPowerState(syncControls: true);
        _panel.Show();
        if (_panel.WindowState == FormWindowState.Minimized) _panel.WindowState = FormWindowState.Normal;
        _panel.Activate();
    }

    private void ReapplySavedProfile()
    {
        var saved = PowerService.Profiles.FirstOrDefault(p => p.Name == _settings.Profile);
        if (saved is null || PowerService.Current() == saved) return;
        try { PowerService.Apply(saved); }
        catch (Exception ex) { ShowError($"Não consegui reaplicar o perfil {saved.Name}: {ex.Message}"); }
    }

    private bool ApplyPower(uint turbo, uint limit)
    {
        var profile = PowerService.Match(turbo, limit);
        bool ok = true;
        try
        {
            PowerService.Apply(turbo, limit);
            SaveSettings(_settings with { Profile = profile?.Name }); // personalizado: não é reaplicado ao iniciar
        }
        catch (Exception ex)
        {
            ShowError($"Não consegui aplicar o perfil {profile?.Name ?? "personalizado"}: {ex.Message}");
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
            var current = PowerService.Match(turbo, limit);
            foreach (var (p, item) in _profileItems) item.Checked = p == current;
            _profileLabel = current?.Name ?? $"personalizado (turbo {PowerService.TurboNames.GetValueOrDefault(turbo, turbo.ToString())}, {limit}%)";
            _turbo = turbo;
            _panel.ShowPower(turbo, limit, syncControls);
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
        // Outro programa (ou o app da Samsung) pode trocar o plano: confere a cada 5 s.
        if (++_ticks % 5 == 0) RefreshPowerState();
        UpdateIcon(force: false);

        var s = _sensors.Latest;
        if (s is null) return;
        _panel.Push(s);
        if (_alert.Check(s.TempMax, DateTime.Now)) OnOverheat(s.TempMax);
    }

    private void OnOverheat(double celsius)
    {
        string text = $"Está em {celsius:0} °C há mais de {_settings.AlertSeconds} s.";
        bool turboOn = _turbo is > 0; // Frio e Silencioso já estão com o turbo desligado
        _alertBalloonShown = false;
        if (turboOn && _settings.AutoCool && ApplyPower(Cold.Turbo, Cold.Limit))
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
            _icon = TrayIconRenderer.Render(temp, IconSize);
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
        _openPanelWait.Unregister(null);
        _tray.Visible = false;
        _tray.Dispose();
        _panel.Dispose();
        _sensors.Dispose();
        _icon.Dispose();
        base.ExitThreadCore();
    }
}
