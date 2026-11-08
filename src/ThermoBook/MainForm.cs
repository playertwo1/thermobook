using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using ThermoBook.Services;

namespace ThermoBook;

/// <summary>Painel com os cartões dos sensores e os controles de energia (port do protótipo PowerShell).</summary>
internal sealed partial class MainForm : Form
{
    private const int HistoryLength = 60; // um ponto por segundo: o último minuto

    private static readonly Color Bg = Color.FromArgb(24, 24, 27), CardBg = Color.FromArgb(36, 36, 40), BarBg = Color.FromArgb(30, 30, 34);
    private static readonly Color Fg = Color.FromArgb(236, 236, 240), Dim = Color.FromArgb(160, 160, 170);
    private static readonly Color Blue = Color.FromArgb(64, 156, 255), Green = Color.FromArgb(80, 200, 120), Purple = Color.FromArgb(180, 130, 255),
        Teal = Color.FromArgb(60, 200, 200), Orange = Color.FromArgb(255, 160, 60), Red = Color.FromArgb(255, 85, 85), Yellow = Color.FromArgb(240, 200, 60);
    private static readonly Color[] ProfileColors = [Blue, Purple, Green, Red]; // na ordem de PowerService.Profiles
    private static readonly uint[] TurboOrder = [0, 3, 1, 2, 4];

    private readonly Card _temp = new("Temperatura", Green, 30, 100), _cpu = new("Processador", Blue), _gpu = new("GPU Intel Arc", Green),
        _ram = new("Memória RAM", Purple), _disk = new("Disco (SSD)", Teal), _bat = new("Bateria", Yellow);
    private readonly Dictionary<PowerProfile, Button> _profileButtons = [];
    private readonly ComboBox _turbo;
    private readonly TrackBar _limit;
    private readonly Label _current, _status;

    /// <param name="openFan">null quando o app Configurações Samsung não está instalado.</param>
    public MainForm(Action<uint, uint> applyPower, Action? openFan)
    {
        SuspendLayout();
        // Tamanhos em pixels de 96 DPI; o WinForms escala para o DPI do monitor (PerMonitorV2 no csproj).
        AutoScaleDimensions = new SizeF(96, 96);
        AutoScaleMode = AutoScaleMode.Dpi;
        Text = "ThermoBook";
        Font = new Font("Segoe UI", 9.5f);
        BackColor = Bg;
        ForeColor = Fg;
        ClientSize = new Size(1040, 680);
        MinimumSize = new Size(820, 600);
        StartPosition = FormStartPosition.CenterScreen;

        var grid = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(6), ColumnCount = 3, RowCount = 2 };
        for (int i = 0; i < 3; i++) grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.33f));
        for (int i = 0; i < 2; i++) grid.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        grid.Controls.AddRange([_temp, _cpu, _gpu, _ram, _disk, _bat]);

        var tip = new ToolTip();
        var top = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(10, 6, 10, 6), BackColor = BarBg };
        top.Controls.Add(Lbl("Perfis:"));
        for (int i = 0; i < PowerService.Profiles.Length; i++)
        {
            var p = PowerService.Profiles[i];
            var b = Btn(p.Name, ProfileColors[i], 104);
            b.Click += (_, _) => applyPower(p.Turbo, p.Limit);
            tip.SetToolTip(b, p.Description);
            _profileButtons[p] = b;
            top.Controls.Add(b);
        }
        Control lastOfRow = top.Controls[^1];
        if (openFan is not null)
        {
            var fan = Btn("Ventoinha (Samsung)...", Orange, 190);
            fan.Click += (_, _) => openFan();
            tip.SetToolTip(fan, "Abre Configurações Samsung > Bateria e desempenho. \"Alto desempenho\" faz a ventoinha girar mais forte.");
            top.Controls.Add(lastOfRow = fan);
        }
        top.SetFlowBreak(lastOfRow, true);

        _turbo = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList, BackColor = CardBg, ForeColor = Fg,
            Width = 190, Margin = new Padding(4, 8, 14, 0),
        };
        // Tema escuro nativo só na ComboBox: o modo escuro global do WinForms apaga as cores das bordas dos perfis.
        _turbo.HandleCreated += (_, _) => SetWindowTheme(_turbo.Handle, "DarkMode_CFD", null);
        foreach (var t in TurboOrder)
        {
            string name = PowerService.TurboNames[t];
            _turbo.Items.Add(char.ToUpper(name[0]) + name[1..]);
        }
        _limit = new TrackBar
        {
            Minimum = 50, Maximum = 100, SmallChange = 5, LargeChange = 10, TickFrequency = 10,
            AutoSize = false, Width = 200, Height = 34, BackColor = BarBg, Margin = new Padding(4, 6, 0, 0),
        };
        var limitLabel = Lbl("100%", Fg);
        limitLabel.AutoSize = false;
        limitLabel.Width = 48;
        _limit.ValueChanged += (_, _) => limitLabel.Text = $"{_limit.Value}%";
        var apply = Btn("Aplicar", Fg, 90);
        apply.Click += (_, _) =>
        {
            if (_turbo.SelectedIndex >= 0) applyPower(TurboOrder[_turbo.SelectedIndex], (uint)_limit.Value);
        };
        _current = Lbl("", Fg);
        top.Controls.AddRange([Lbl("Turbo Boost:"), _turbo, Lbl("Limite da CPU:"), _limit, limitLabel, apply, _current]);

        _status = new Label
        {
            Dock = DockStyle.Bottom, Height = 26, Padding = new Padding(12, 0, 0, 0), TextAlign = ContentAlignment.MiddleLeft,
            BackColor = BarBg, ForeColor = Dim, Text = "Coletando dados...",
        };

        // Fill primeiro: o docking processa do último para o primeiro.
        Controls.AddRange([grid, top, _status]);
        ResumeLayout(true);
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        int dark = 1;
        DwmSetWindowAttribute(Handle, 20 /* DWMWA_USE_IMMERSIVE_DARK_MODE */, ref dark, sizeof(int));
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        // Fechar só esconde: o app continua na bandeja e a coleta não para.
        // Inclui WM_CLOSE vindo de outro programa (TaskManagerClosing): fechar de verdade descartaria o painel e o próximo "abrir" quebraria.
        if (e.CloseReason is not (CloseReason.ApplicationExitCall or CloseReason.WindowsShutDown))
        {
            e.Cancel = true;
            Hide();
        }
        base.OnFormClosing(e);
    }

    /// <summary>Acrescenta uma leitura aos cartões. Chamado a cada segundo, mesmo com o painel escondido.</summary>
    public void Push(Snapshot s)
    {
        double t = s.TempMax;
        _temp.Push(t, $"{t:0} °C", $"Sensores: {string.Join(" / ", s.Temps.Select(x => $"{x:0}°"))}  ({TempState(t)})", TrayIconRenderer.TempColor(t));
        _cpu.Push(s.CpuUse, $"{s.CpuUse:0}%", $"{s.CpuGHz:0.00} GHz  ({s.CpuPerf:0}% da base{(s.InTurbo ? ", em turbo" : "")})");
        _gpu.Push(s.Gpu, $"{s.Gpu:0}%", $"3D {s.Gpu3D:0}%   Vídeo {s.GpuVideo:0}%   Mem {Bytes(s.GpuMem)}");
        _ram.Push(s.RamPercent, $"{s.RamPercent:0}%", $"{Bytes(s.RamUsed)} de {Bytes(s.RamTotal)} em uso");
        _disk.Push(s.DiskActive, $"{s.DiskActive:0}%", $"Ler {Bytes(s.DiskRead)}/s  Gravar {Bytes(s.DiskWrite)}/s  C: {Bytes(s.CFree)} livre");

        var power = SystemInformation.PowerStatus;
        if (power.BatteryChargeStatus.HasFlag(BatteryChargeStatus.NoSystemBattery))
            _bat.Push(0, "--", "Sem bateria");
        else
        {
            double bat = Math.Round(power.BatteryLifePercent * 100);
            _bat.Push(bat, $"{bat:0}%", BatteryText(power.PowerLineStatus == PowerLineStatus.Online,
                power.BatteryChargeStatus.HasFlag(BatteryChargeStatus.Charging), power.BatteryLifeRemaining));
        }

        _status.Text = $"Atualizado {DateTime.Now:HH:mm:ss}   |   Ventoinha: controlada pelo firmware Samsung (sem leitura de RPM no Windows)";
    }

    /// <param name="syncControls">false durante a leitura periódica, para não desfazer o que o usuário está escolhendo.</param>
    public void ShowPower(uint turbo, uint limit, bool syncControls)
    {
        var current = PowerService.Match(turbo, limit);
        foreach (var (p, b) in _profileButtons) b.FlatAppearance.BorderSize = p == current ? 3 : 1;
        _current.Text = $"Ativo agora: turbo {PowerService.TurboNames.GetValueOrDefault(turbo, turbo.ToString())}, limite {limit}%"
            + (current is null ? "" : $" ({current.Name})");
        if (!syncControls) return;
        _turbo.SelectedIndex = Array.IndexOf(TurboOrder, turbo);
        _limit.Value = (int)Math.Clamp(limit, 50, 100);
    }

    public static string Bytes(double b) => b switch
    {
        >= 1 << 30 => $"{b / (1 << 30):N1} GB",
        >= 1 << 20 => $"{b / (1 << 20):N1} MB",
        _ => $"{b / 1024:N0} KB",
    };

    public static string TempState(double celsius) => celsius switch
    {
        >= 90 => "quente demais",
        >= 75 => "quente",
        _ => "ok",
    };

    public static string BatteryText(bool plugged, bool charging, int secondsLeft) =>
        plugged ? (charging ? "Na tomada, carregando" : "Na tomada")
        : secondsLeft > 0 ? $"Na bateria, ~{secondsLeft / 3600}h{secondsLeft % 3600 / 60:00} restantes"
        : "Na bateria";

    private static Button Btn(string text, Color border, int width)
    {
        var b = new Button
        {
            Text = text, Width = width, Height = 34, FlatStyle = FlatStyle.Flat, BackColor = CardBg, ForeColor = Fg,
            Margin = new Padding(4, 3, 4, 3),
        };
        b.FlatAppearance.BorderColor = border;
        return b;
    }

    private static Label Lbl(string text, Color? color = null) =>
        new() { Text = text, AutoSize = true, ForeColor = color ?? Dim, Margin = new Padding(4, 11, 4, 0) };

    /// <summary>Cartão com título, valor grande, linha de detalhe e gráfico do último minuto.</summary>
    private sealed class Card : Panel
    {
        private readonly Label _value, _detail;
        private readonly Graph _graph = new() { Dock = DockStyle.Fill };
        private readonly List<double> _history = [];
        private readonly double _min, _max;
        private Color _color;

        public Card(string title, Color color, double min = 0, double max = 100)
        {
            (_color, _min, _max) = (color, min, max);
            Dock = DockStyle.Fill;
            BackColor = CardBg;
            Margin = new Padding(6);
            Padding = new Padding(12, 8, 12, 10);
            _detail = new Label { Dock = DockStyle.Top, Height = 22, ForeColor = Dim, Text = "...", AutoEllipsis = true };
            _value = new Label { Dock = DockStyle.Top, Height = 46, Font = new Font("Segoe UI Semibold", 22), ForeColor = color, Text = "--" };
            var t = new Label { Dock = DockStyle.Top, Height = 22, Font = new Font("Segoe UI Semibold", 10), ForeColor = Dim, Text = title.ToUpper() };
            _graph.Paint += DrawGraph;
            Controls.AddRange([_graph, _detail, _value, t]);
        }

        public void Push(double v, string value, string detail, Color? color = null)
        {
            _history.Add(v);
            if (_history.Count > HistoryLength) _history.RemoveAt(0);
            _value.Text = value;
            _detail.Text = detail;
            if (color is { } c) _value.ForeColor = _color = c;
            _graph.Invalidate();
        }

        private void DrawGraph(object? sender, PaintEventArgs e)
        {
            var g = e.Graphics;
            float w = _graph.ClientSize.Width, h = _graph.ClientSize.Height;
            if (w < 10 || h < 10) return;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var gridPen = new Pen(Color.FromArgb(55, 55, 62)))
                foreach (float f in new[] { 0.25f, 0.5f, 0.75f }) g.DrawLine(gridPen, 0, h * f, w, h * f);

            int n = _history.Count;
            if (n < 2) return;
            float step = w / (HistoryLength - 1);
            var pts = new PointF[n];
            for (int i = 0; i < n; i++)
            {
                double v = Math.Clamp((_history[i] - _min) / (_max - _min), 0, 1);
                pts[i] = new PointF(w - (n - 1 - i) * step, (float)(h - 1 - v * (h - 3)));
            }
            using (var fill = new SolidBrush(Color.FromArgb(50, _color)))
                g.FillPolygon(fill, [.. pts, new PointF(pts[^1].X, h), new PointF(pts[0].X, h)]);
            using var pen = new Pen(_color, LogicalToDeviceUnits(2));
            g.DrawLines(pen, pts);
        }
    }

    private sealed class Graph : Panel
    {
        public Graph()
        {
            DoubleBuffered = true;
            ResizeRedraw = true;
        }
    }

    [LibraryImport("dwmapi.dll")]
    private static partial int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    [LibraryImport("uxtheme.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int SetWindowTheme(IntPtr hwnd, string? subAppName, string? subIdList);
}
