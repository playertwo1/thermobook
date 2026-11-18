using System.Drawing.Drawing2D;
using System.Drawing.Text;
using Microsoft.Win32;
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
internal sealed class PillForm : FloatingForm
{
    private static readonly Color Bg = Color.Black, Border = Color.FromArgb(0x24, 0x24, 0x24), Fg = Color.FromArgb(0xF4, 0xF4, 0xF6);

    private static readonly Color Muted = Color.FromArgb(0xA9, 0xAB, 0xB5), Track = Color.FromArgb(0x24, 0x24, 0x24),
        CpuColor = Color.FromArgb(0x40, 0x9C, 0xFF), GpuColor = Color.FromArgb(0xB4, 0x82, 0xFF), RamColor = Color.FromArgb(0x3C, 0xC8, 0xC8);
    private static readonly string[] RowNames = ["Temperatura", "Processador", "GPU", "Memória RAM", "Modo de energia"];

    private PillState? _state;
    private string? _monitor;
    private Dictionary<string, PillPosition>? _positions;
    private Point _dragOffset;
    private bool _dragging;

    /// <summary>Posição final escolhida pelo usuário (monitor + posição) para salvar no settings.json.</summary>
    public event Action<string, PillPosition>? PlacementChanged;

    /// <summary>Clique simples (soltou sem arrastar).</summary>
    public event Action? Clicked;
    private Point _downAt;
    private bool _moved;
    private PowerMode? _mode;
    internal PowerMode? Mode => _mode;
    internal bool Background => _background;
    private bool _background;
    private Orientation _orientation = Orientation.Vertical;
    private PillItem[] _items = [];
    private float _scale = 1f;

    public PillForm()
    {
        // Tamanhos em pixels de 96 DPI escalados pelo DPI do monitor (PerMonitorV2 no csproj).
        float k = _scale = DeviceDpi / 96f;
        Relayout();
        Font = MakeFont();
        SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
    }

    private Font MakeFont()
    {
        var f = new Font("Segoe UI Variable", 13 * _scale, FontStyle.Bold, GraphicsUnit.Pixel);
        return f.Name == "Segoe UI Variable" ? f : new Font("Segoe UI", 13 * _scale, FontStyle.Bold, GraphicsUnit.Pixel);
    }

    private (Size Vertical, Size Horizontal) Sizes() =>
        (PillLayout.Compute(Orientation.Vertical, _scale).Size, PillLayout.Compute(Orientation.Horizontal, _scale).Size);

    private static List<(string Key, Rectangle Area)> Screens() =>
        Screen.AllScreens.Select(s => (PillDock.MonitorKey(s.DeviceName, s.Bounds), s.WorkingArea)).ToList();

    /// <summary>Posiciona a pílula: a salva do monitor atual, ou o canto padrão do monitor principal se ele sumiu.</summary>
    public void Restore(string? monitor, Dictionary<string, PillPosition>? positions)
    {
        _monitor = monitor;
        _positions = positions;
        var (v, h) = Sizes();
        var primary = Screen.PrimaryScreen!;
        var (key, r) = PillDock.Resolve(_monitor, _positions, Screens(), PillDock.MonitorKey(primary.DeviceName, primary.Bounds), v, h);
        _monitor = key;
        PillOrientation = r.Orientation;
        Location = r.Location;
    }

    /// <summary>Cabo desligado ou resolução trocada: a posição antiga pode estar fora de qualquer tela.</summary>
    private void OnDisplaySettingsChanged(object? sender, EventArgs e)
    {
        if (IsHandleCreated && !IsDisposed) BeginInvoke(() => Restore(_monitor, _positions)); // o evento vem de outra thread
    }

    protected override void OnDpiChanged(DpiChangedEventArgs e)
    {
        base.OnDpiChanged(e);
        _scale = e.DeviceDpiNew / 96f;
        Font = MakeFont(); // não descarta a anterior: o WinForms ainda pode referenciá-la (vazamento mínimo, só em troca de DPI)
        Relayout();
        SnapToEdge();
        Invalidate();
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left) return;
        _dragging = true;
        _downAt = MousePosition;
        _moved = false;
        _dragOffset = new Point(MousePosition.X - Left, MousePosition.Y - Top);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_dragging && (Math.Abs(MousePosition.X - _downAt.X) > 3 || Math.Abs(MousePosition.Y - _downAt.Y) > 3)) _moved = true;
        if (_dragging && _moved) Location = new Point(MousePosition.X - _dragOffset.X, MousePosition.Y - _dragOffset.Y);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (!_dragging) return;
        _dragging = false;
        if (!_moved) { Clicked?.Invoke(); return; }
        SnapToEdge();
        var scr = Screen.FromControl(this);
        var key = PillDock.MonitorKey(scr.DeviceName, scr.Bounds);
        var pos = new PillPosition(Left, Top, _orientation == Orientation.Horizontal);
        _monitor = key;
        (_positions ??= [])[key] = pos;
        PlacementChanged?.Invoke(key, pos);
    }

    /// <summary>Encaixa na borda mais próxima (a até 24 px lógicos) do monitor onde a pílula está.</summary>
    protected override int RegionRadius => (int)Math.Round(10 * _scale); // menos redondo que a cápsula completa (pedido do dono)

    protected override DockEdges DockedEdges => PillDock.Edges(Bounds, Screen.FromControl(this).WorkingArea);

    private void SnapToEdge()
    {
        var (v, h) = Sizes();
        var area = Screen.FromControl(this).WorkingArea;
        var r = PillDock.Snap(Location, Size, _orientation, v, h, area, (int)Math.Round(PillDock.ThresholdLogical * _scale));
        PillOrientation = r.Orientation;
        Location = r.Location;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
        base.Dispose(disposing);
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

    /// <summary>Modo atual (null = Personalizado), se o segundo plano está ligado e se o modo veio do modo automático.</summary>
    public void SetMode(PowerMode? mode, bool background, bool auto = false)
    {
        Auto = auto; // só texto (flyout e leitor de tela): não muda o desenho
        if (_mode == mode && _background == background) return;
        _mode = mode;
        _background = background;
        Invalidate();
    }

    internal bool Auto { get; private set; }

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
    internal static string RowName(int index, PillState? st, PowerMode? mode, bool background, bool auto = false)
    {
        int? v = index switch { PillLayout.Temp => st?.Temp, PillLayout.Cpu => st?.Cpu, PillLayout.Gpu => st?.Gpu, PillLayout.Ram => st?.Ram, _ => null };
        return index switch
        {
            PillLayout.Temp => $"Temperatura: {(v is { } t ? $"{t} graus" : "indisponível")}",
            PillLayout.Mode => $"Modo de energia: {mode?.Name ?? "Personalizado"}{(auto ? ", automático" : "")}{(background ? ", segundo plano ligado" : "")}",
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
        public override string Name { get => RowName(index, form._state, form._mode, form._background, form.Auto); set { } }
        public override AccessibleRole Role => AccessibleRole.StaticText;
        public override AccessibleObject Parent => parent;
        public override Rectangle Bounds => form.RectangleToScreen(form._items[index].Row);
    }
}
