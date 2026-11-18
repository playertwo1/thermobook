using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using ThermoBook.Services;

namespace ThermoBook;

/// <summary>Valores já arredondados da pílula (null = indisponível, mostra "–"): a janela só se redesenha quando algum muda.</summary>
internal readonly record struct PillState(int? Temp, int? Cpu, int? Gpu, int? Ram)
{
    public static PillState From(Snapshot s) => new(
        s.Temps.Length > 0 ? Round(s.TempMax) : null, Round(s.CpuUse), Round(s.Gpu), s.RamTotal > 0 ? Round(s.RamPercent) : null);

    private static int? Round(double v) => double.IsFinite(v) ? (int)Math.Round(v) : null;
}

/// <summary>
/// Cápsula flutuante (DESIGN.md). Não rouba o foco, fica fora da barra de tarefas e do Alt+Tab.
/// Vertical (presa a borda lateral) ou horizontal (topo/base), com o mesmo layout (<see cref="PillLayout"/>).
/// </summary>
internal sealed partial class PillForm : Form
{
    private const int WS_EX_TOOLWINDOW = 0x80, WS_EX_NOACTIVATE = 0x08000000;
    private const int WM_MOUSEACTIVATE = 0x21, MA_NOACTIVATE = 3;
    private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33, DWMWCP_ROUND = 2;
    private static readonly Color Bg = Color.Black, Border = Color.FromArgb(0x24, 0x24, 0x24), Fg = Color.FromArgb(0xF4, 0xF4, 0xF6);

    private static readonly Color Muted = Color.FromArgb(0xA9, 0xAB, 0xB5), Track = Color.FromArgb(0x24, 0x24, 0x24),
        CpuColor = Color.FromArgb(0x40, 0x9C, 0xFF), GpuColor = Color.FromArgb(0xB4, 0x82, 0xFF), RamColor = Color.FromArgb(0x3C, 0xC8, 0xC8);
    private static readonly string[] RowNames = ["Temperatura", "Processador", "GPU", "Memória RAM", "Modo de energia"];

    private PillState? _state;
    private PowerMode? _mode;
    private bool _background;
    private Orientation _orientation = Orientation.Vertical;
    private PillItem[] _items = [];
    private float _scale = 1f;

    public PillForm()
    {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        BackColor = Bg;
        DoubleBuffered = true;
        // Tamanhos em pixels de 96 DPI escalados pelo DPI do monitor (PerMonitorV2 no csproj).
        float k = _scale = DeviceDpi / 96f;
        Relayout();
        Font = new Font("Segoe UI Variable", 11 * k, FontStyle.Bold, GraphicsUnit.Pixel);
        if (Font.Name != "Segoe UI Variable") Font = new Font("Segoe UI", 11 * k, FontStyle.Bold, GraphicsUnit.Pixel);
        // Posição inicial (A3): borda direita, centro vertical, monitor principal.
        var wa = Screen.PrimaryScreen!.WorkingArea;
        Location = new Point(wa.Right - Width - (int)(8 * k), wa.Top + (wa.Height - Height) / 2);
    }

    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public Orientation PillOrientation
    {
        get => _orientation;
        set { if (_orientation == value) return; _orientation = value; Relayout(); Invalidate(); }
    }

    private void Relayout()
    {
        (var size, _items) = PillLayout.Compute(_orientation, _scale);
        ClientSize = size;
    }

    /// <summary>Modo atual (null = Personalizado) e se o segundo plano está ligado.</summary>
    public void SetMode(PowerMode? mode, bool background)
    {
        if (_mode == mode && _background == background) return;
        _mode = mode;
        _background = background;
        Invalidate();
    }

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE;
            return cp;
        }
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        int pref = DWMWCP_ROUND;
        // Windows 10 não conhece o atributo (erro): plano B com Region.
        if (DwmSetWindowAttribute(Handle, DWMWA_WINDOW_CORNER_PREFERENCE, ref pref, sizeof(int)) != 0) ApplyRegion();
    }

    protected override void OnSizeChanged(EventArgs e)
    {
        base.OnSizeChanged(e);
        if (IsHandleCreated && Region is not null) ApplyRegion();
    }

    private void ApplyRegion()
    {
        using var path = new GraphicsPath();
        int d = Math.Min(Width, Height) / 2 * 2;
        path.AddArc(0, 0, d, d, 180, 90);
        path.AddArc(Width - d, 0, d, d, 270, 90);
        path.AddArc(Width - d, Height - d, d, d, 0, 90);
        path.AddArc(0, Height - d, d, d, 90, 90);
        path.CloseFigure();
        Region = new Region(path);
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WM_MOUSEACTIVATE) { m.Result = MA_NOACTIVATE; return; } // clicar não tira o foco do app da frente
        base.WndProc(ref m);
    }

    /// <summary>Chamado a cada segundo; só redesenha se algum valor arredondado mudou.</summary>
    public void Push(Snapshot s)
    {
        var next = PillState.From(s);
        if (_state == next) return;
        _state = next;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
        using (var pen = new Pen(Border))
            g.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);

        var st = _state ?? new PillState(null, null, null, null);
        int?[] values = [st.Temp, st.Cpu, st.Gpu, st.Ram];
        Color[] colors = [st.Temp is { } t ? TrayIconRenderer.TempColor(t) : Muted, CpuColor, GpuColor, RamColor];
        using var fmt = new StringFormat(StringFormat.GenericTypographic)
            { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center, FormatFlags = StringFormatFlags.NoWrap };
        for (int i = 0; i < 4; i++)
        {
            var item = _items[i];
            string text = values[i] is { } v ? (i == PillLayout.Temp ? $"{v}°" : v.ToString()) : "–";
            using (var brush = new SolidBrush(values[i] is null ? Muted : i == PillLayout.Temp ? colors[i] : Fg))
                g.DrawString(text, Font, brush, item.Value, fmt);
            if (item.Meter is { } m) DrawMeter(g, m, values[i], colors[i]);
        }
        DrawModeDot(g, _items[PillLayout.Mode].Value);
    }

    private void DrawMeter(Graphics g, Rectangle m, int? percent, Color color)
    {
        using (var track = new SolidBrush(Track)) g.FillRectangle(track, m);
        if (percent is not { } p) return;
        double f = Math.Clamp(p, 0, 100) / 100.0;
        bool lying = _orientation == Orientation.Vertical; // deitado na vertical; em pé na horizontal
        var fill = lying
            ? new Rectangle(m.X, m.Y, (int)Math.Round(m.Width * f), m.Height)
            : new Rectangle(m.X, m.Bottom - (int)Math.Round(m.Height * f), m.Width, (int)Math.Round(m.Height * f));
        using var brush = new SolidBrush(color);
        g.FillRectangle(brush, fill);
    }

    private void DrawModeDot(Graphics g, Rectangle dot)
    {
        using (var brush = new SolidBrush(_mode is null ? Muted : ModeColor(_mode.Name)))
            g.FillEllipse(brush, dot);
        if (!_background) return;
        // Anel ao redor: lembrete de que a tampa não suspende.
        int grow = (int)Math.Round(3 * _scale);
        var ring = Rectangle.Inflate(dot, grow, grow);
        using var pen = new Pen(Fg, Math.Max(1f, _scale));
        g.DrawEllipse(pen, ring);
    }

    // Sem vermelho nem laranja: essas cores são só de temperatura e alertas (DESIGN.md).
    private static Color ModeColor(string name) => name switch
    {
        "Economia" => Color.FromArgb(0x5A, 0xD2, 0x78),
        "Frio" => CpuColor,
        "Eficiência" => RamColor,
        "Equilibrado" => Fg,
        _ => GpuColor, // Alto e Máximo
    };

    /// <summary>Texto lido pelo leitor de tela para cada linha.</summary>
    internal static string RowName(int index, PillState? st, PowerMode? mode, bool background)
    {
        int? v = index switch { PillLayout.Temp => st?.Temp, PillLayout.Cpu => st?.Cpu, PillLayout.Gpu => st?.Gpu, PillLayout.Ram => st?.Ram, _ => null };
        return index switch
        {
            PillLayout.Temp => $"Temperatura: {(v is { } t ? $"{t} graus" : "indisponível")}",
            PillLayout.Mode => $"Modo de energia: {mode?.Name ?? "Personalizado"}{(background ? ", segundo plano ligado" : "")}",
            _ => $"{RowNames[index]}: {(v is { } p ? $"{p} por cento" : "indisponível")}",
        };
    }

    protected override AccessibleObject CreateAccessibilityInstance() => new PillAccessible(this);

    private sealed class PillAccessible(PillForm form) : ControlAccessibleObject(form)
    {
        public override int GetChildCount() => PillLayout.ItemCount;
        public override AccessibleObject GetChild(int index) => new RowAccessible(this, form, index);
    }

    private sealed class RowAccessible(AccessibleObject parent, PillForm form, int index) : AccessibleObject
    {
        public override string Name { get => RowName(index, form._state, form._mode, form._background); set { } }
        public override AccessibleRole Role => AccessibleRole.StaticText;
        public override AccessibleObject Parent => parent;
        public override Rectangle Bounds => form.RectangleToScreen(form._items[index].Row);
    }

    [LibraryImport("dwmapi.dll")]
    private static partial int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);
}
