using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using ThermoBook.Services;

namespace ThermoBook;

/// <summary>Abas do painel completo, na ordem em que aparecem.</summary>
internal enum PanelTab { Monitor, Modes, Tools, Settings }

/// <summary>
/// Painel completo em abas (12.1), no jeito do OpenUsage: faixa de botões de segmento no topo, uma página por vez e rodapé com o status.
/// Monitor e Modos são montados aqui; Ferramentas e Configurações chegam prontas do TrayApp.
/// </summary>
internal sealed partial class MainForm : Form
{
    private const int HistoryLength = 60; // um ponto por segundo: o último minuto

    private static readonly Color Bg = Theme.Bg, CardBg = Theme.Raised, Fg = Theme.Fg, Dim = Theme.Muted;
    private static readonly Color Blue = Color.FromArgb(64, 156, 255), Green = Color.FromArgb(80, 200, 120), Purple = Color.FromArgb(180, 130, 255),
        Teal = Color.FromArgb(60, 200, 200), Yellow = Color.FromArgb(240, 200, 60);
    private static readonly uint[] TurboOrder = [0, 3, 1, 2, 4];
    private static readonly string[] TabNames = ["Monitor", "Modos", "Ferramentas", "Configurações"]; // na ordem de PanelTab

    private readonly Card _temp = new("Temperatura", Green, 30, 100), _cpu = new("Processador", Blue), _gpu = new("GPU Intel Arc", Green),
        _ram = new("Memória RAM", Purple), _disk = new("Disco (SSD)", Teal), _bat = new("Bateria", Yellow);
    private readonly Dictionary<PowerMode, Button> _profileButtons = [];
    private readonly ComboBox _turbo;
    private readonly TrackBar _limit;
    private readonly Label _current, _status;
    private readonly Button[] _tabButtons = new Button[TabNames.Length];
    private readonly Control[] _pages;
    private readonly Font _bold;

    public PanelTab Tab { get; private set; }

    /// <param name="openFan">null quando o app Configurações Samsung não está instalado.</param>
    public MainForm(Action<PowerMode> applyMode, Action<uint, uint> applyPower, Action? openFan, Control tools, Control settings)
    {
        SuspendLayout();
        // Tamanhos em pixels de 96 DPI; o WinForms escala para o DPI do monitor (PerMonitorV2 no csproj).
        AutoScaleDimensions = new SizeF(96, 96);
        AutoScaleMode = AutoScaleMode.Dpi;
        Text = "ThermoBook";
        Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        Font = new Font("Segoe UI", 9.5f);
        BackColor = Bg;
        ForeColor = Fg;
        ClientSize = new Size(1040, 680);
        MinimumSize = new Size(820, 600);
        StartPosition = FormStartPosition.CenterScreen;
        _bold = new Font(Font, FontStyle.Bold);

        var grid = new TableLayoutPanel { Padding = new Padding(6), ColumnCount = 3, RowCount = 2 };
        for (int i = 0; i < 3; i++) grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.33f));
        for (int i = 0; i < 2; i++) grid.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        grid.Controls.AddRange([_temp, _cpu, _gpu, _ram, _disk, _bat]);

        // Aba Modos: os 6 modos com descrição e, embaixo, os controles avançados da v0.1.
        var tip = new ToolTip();
        var modes = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, Padding = new Padding(12) };
        modes.Controls.Add(Theme.Header("Modo de energia"));
        var table = new TableLayoutPanel { AutoSize = true, ColumnCount = 2, Margin = new Padding(8, 0, 0, 0) };
        foreach (var p in PowerService.Modes)
        {
            var b = Theme.Button(p.Name, 130);
            b.Height = 34;
            b.Click += (_, _) => applyMode(p);
            b.AccessibleDescription = p.Description;
            _profileButtons[p] = b;
            table.Controls.Add(b);
            table.Controls.Add(new Label { Text = p.Description, AutoSize = true, ForeColor = Dim, Anchor = AnchorStyles.Left, Margin = new Padding(8, 0, 3, 0) });
        }
        modes.Controls.Add(table);
        _current = new Label { AutoSize = true, ForeColor = Fg, Margin = new Padding(12, 8, 3, 0) };
        modes.Controls.Add(_current);

        modes.Controls.Add(Theme.Header("Avançado"));
        modes.Controls.Add(Theme.Note("Turbo e limite da CPU à mão. A combinação aparece como \"Personalizado\" e não é reaplicada ao abrir o app."));
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
            AutoSize = false, Width = 200, Height = 34, BackColor = Bg, Margin = new Padding(4, 6, 0, 0),
        };
        var limitLabel = Lbl("100%", Fg);
        limitLabel.AutoSize = false;
        limitLabel.Width = 48;
        _limit.ValueChanged += (_, _) => limitLabel.Text = $"{_limit.Value}%";
        var apply = Theme.Button("Aplicar", 90);
        apply.Click += (_, _) =>
        {
            if (_turbo.SelectedIndex >= 0) applyPower(TurboOrder[_turbo.SelectedIndex], (uint)_limit.Value);
        };
        var advanced = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(8, 0, 0, 0) };
        advanced.Controls.AddRange([Lbl("Turbo Boost:"), _turbo, Lbl("Limite da CPU:"), _limit, limitLabel, apply]);
        modes.Controls.Add(advanced);
        if (openFan is not null)
        {
            modes.Controls.Add(Theme.Header("Ventoinha"));
            var fan = Theme.Button("Abrir Configurações Samsung...");
            fan.Margin = new Padding(12, 3, 3, 3);
            fan.Click += (_, _) => openFan();
            tip.SetToolTip(fan, "Abre Configurações Samsung > Bateria e desempenho. \"Alto desempenho\" faz a ventoinha girar mais forte.");
            modes.Controls.Add(fan);
        }

        _status = new Label
        {
            Dock = DockStyle.Bottom, Height = 26, Padding = new Padding(12, 0, 0, 0), TextAlign = ContentAlignment.MiddleLeft,
            BackColor = Bg, ForeColor = Dim, Text = "Coletando dados...",
        };

        var nav = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(12, 8, 12, 4), BackColor = Bg };
        for (int i = 0; i < TabNames.Length; i++)
        {
            var tab = (PanelTab)i;
            var b = Theme.Button(TabNames[i]);
            b.BackColor = Bg; // aba fora de foco é "quieta" (sem fundo), como o SegmentButton do OpenUsage
            b.AccessibleRole = AccessibleRole.PageTab;
            b.Click += (_, _) => ShowTab(tab);
            _tabButtons[i] = b;
            nav.Controls.Add(b);
        }

        _pages = [grid, modes, tools, settings];
        var host = new Panel { Dock = DockStyle.Fill, BackColor = Bg };
        foreach (var page in _pages)
        {
            page.Dock = DockStyle.Fill;
            page.Visible = false;
            host.Controls.Add(page);
        }

        // Fill primeiro: o docking processa do último para o primeiro.
        Controls.AddRange([host, nav, _status]);
        ShowTab(PanelTab.Monitor);
        ResumeLayout(true);
    }

    /// <summary>Mostra uma aba. As páginas escondidas não são criadas até aparecerem (a de Ferramentas só lê apps e Defender aí).</summary>
    public void ShowTab(PanelTab tab)
    {
        Tab = tab;
        for (int i = 0; i < _pages.Length; i++)
        {
            bool on = i == (int)tab;
            _pages[i].Visible = on;
            var b = _tabButtons[i];
            b.BackColor = on ? CardBg : Bg;
            b.Font = on ? _bold : Font;
            b.FlatAppearance.MouseOverBackColor = Theme.Border;
            b.AccessibleDescription = on ? "Aba selecionada" : null;
        }
        ActiveControl = _tabButtons[(int)tab]; // o anel de foco acompanha a aba, mesmo quando ela é aberta pelo painel rápido
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        // Ctrl+Tab / Ctrl+Shift+Tab trocam de aba, como num TabControl.
        if (keyData is (Keys.Control | Keys.Tab) or (Keys.Control | Keys.Shift | Keys.Tab))
        {
            int step = keyData.HasFlag(Keys.Shift) ? -1 : 1;
            ShowTab((PanelTab)(((int)Tab + step + TabNames.Length) % TabNames.Length));
            return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
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
    public void ShowPower(uint turbo, uint limit, PowerMode? current, bool syncControls)
    {
        foreach (var (p, b) in _profileButtons) Theme.Highlight(b, p == current);
        _current.Text = $"Ativo agora: turbo {PowerService.TurboNames.GetValueOrDefault(turbo, turbo.ToString())}, limite {limit}%"
            + $" ({current?.Name ?? "Personalizado"})";
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
