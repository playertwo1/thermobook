using System.Diagnostics;
using System.Reflection;
using ThermoBook.Services;

namespace ThermoBook;

/// <summary>Ícone na bandeja com a temperatura e menu de perfis.</summary>
internal sealed class TrayApp : ApplicationContext
{
    private const string SamsungSettingsPackage = "SAMSUNGELECTRONICSCO.LTD.SamsungSettings1.5_3c1yjt4zspk6g";

    private readonly SettingsStore _store;
    private Settings _settings;
    private readonly SensorService _sensors = new();
    private readonly NotifyIcon _tray;
    private readonly ContextMenuStrip _menu = new();
    private readonly ToolStripMenuItem _status;
    private readonly Dictionary<PowerProfile, ToolStripMenuItem> _profileItems = [];
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 1000 };
    private Icon _icon;
    private double? _shownTemp;
    private string _profileLabel = "?";
    private int _ticks;

    public TrayApp(SettingsStore store)
    {
        _store = store;
        _settings = store.Load();

        _status = new ToolStripMenuItem("Lendo sensores...") { Enabled = false };
        _menu.Items.Add(_status);
        _menu.Items.Add(new ToolStripSeparator());
        foreach (var p in PowerService.Profiles)
        {
            var item = new ToolStripMenuItem(p.Name, null, (_, _) => ApplyProfile(p)) { ToolTipText = p.Description };
            _profileItems[p] = item;
            _menu.Items.Add(item);
        }
        _menu.Items.Add(new ToolStripSeparator());
        if (Directory.Exists(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Packages", SamsungSettingsPackage)))
        {
            _menu.Items.Add("Ventoinha (Configurações Samsung)...", null, (_, _) =>
                Process.Start("explorer.exe", $@"shell:AppsFolder\{SamsungSettingsPackage}!App"));
            _menu.Items.Add(new ToolStripSeparator());
        }
        _menu.Items.Add("Sair", null, (_, _) => ExitThread());
        _menu.ShowItemToolTips = true;

        _icon = TrayIconRenderer.Render(null, IconSize);
        _tray = new NotifyIcon { Icon = _icon, Text = "ThermoBook", ContextMenuStrip = _menu, Visible = true };
        // Até o painel existir (fase 3), o clique esquerdo também abre o menu.
        _tray.MouseUp += (_, e) =>
        {
            if (e.Button == MouseButtons.Left)
                typeof(NotifyIcon).GetMethod("ShowContextMenu", BindingFlags.Instance | BindingFlags.NonPublic)?.Invoke(_tray, null);
        };

        ReapplySavedProfile();
        RefreshPowerState();
        _timer.Tick += (_, _) => OnTick();
        _timer.Start();
    }

    private static int IconSize => SystemInformation.SmallIconSize.Width;

    private void ReapplySavedProfile()
    {
        var saved = PowerService.Profiles.FirstOrDefault(p => p.Name == _settings.Profile);
        if (saved is null || PowerService.Current() == saved) return;
        try { PowerService.Apply(saved); }
        catch (Exception ex) { ShowError($"Não consegui reaplicar o perfil {saved.Name}: {ex.Message}"); }
    }

    private void ApplyProfile(PowerProfile p)
    {
        try
        {
            PowerService.Apply(p);
            _settings = _settings with { Profile = p.Name };
            _store.Save(_settings);
        }
        catch (Exception ex)
        {
            ShowError($"Não consegui aplicar o perfil {p.Name}: {ex.Message}");
        }
        RefreshPowerState();
        UpdateIcon(force: true);
    }

    private void RefreshPowerState()
    {
        try
        {
            uint turbo = PowerService.GetTurbo(), limit = PowerService.GetLimit();
            var current = PowerService.Match(turbo, limit);
            foreach (var (p, item) in _profileItems) item.Checked = p == current;
            _profileLabel = current?.Name ?? $"personalizado (turbo {PowerService.TurboNames.GetValueOrDefault(turbo, turbo.ToString())}, {limit}%)";
        }
        catch (Exception ex)
        {
            _profileLabel = "erro ao ler energia";
            Debug.WriteLine(ex);
        }
    }

    private void OnTick()
    {
        // Outro programa (ou o app da Samsung) pode trocar o plano: confere a cada 5 s.
        if (++_ticks % 5 == 0) RefreshPowerState();
        UpdateIcon(force: false);
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

    private void ShowError(string message) =>
        _tray.ShowBalloonTip(5000, "ThermoBook", message, ToolTipIcon.Error);

    protected override void ExitThreadCore()
    {
        _timer.Stop();
        _tray.Visible = false;
        _tray.Dispose();
        _sensors.Dispose();
        _icon.Dispose();
        base.ExitThreadCore();
    }
}
