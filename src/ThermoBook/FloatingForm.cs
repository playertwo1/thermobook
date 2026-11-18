using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace ThermoBook;

/// <summary>
/// Base das janelas flutuantes (pílula e flyout): sem borda, sempre no topo, sem foco, fora da barra de tarefas e do Alt+Tab,
/// com cantos arredondados por <see cref="Region"/> própria, como o OpenUsage. O arredondamento do DWM no Windows 11 fica desligado:
/// ele arredonda os 4 cantos e desenha um contorno claro.
/// </summary>
internal abstract partial class FloatingForm : Form
{
    private const int WS_EX_TOOLWINDOW = 0x80, WS_EX_NOACTIVATE = 0x08000000;
    private const int WM_MOUSEACTIVATE = 0x21, MA_NOACTIVATE = 3;
    private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33, DWMWCP_DONOTROUND = 1, DWMWA_BORDER_COLOR = 34;
    private const uint DWMWA_COLOR_NONE = 0xFFFFFFFE;

    protected FloatingForm()
    {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        BackColor = Color.Black;
        DoubleBuffered = true;
    }

    /// <summary>Raio dos cantos. A pílula é uma cápsula completa.</summary>
    protected virtual int RegionRadius => Math.Min(Width, Height) / 2;

    /// <summary>Bordas da tela que a janela toca: esses cantos ficam retos (notch). Só a pílula encosta.</summary>
    protected virtual DockEdges DockedEdges => DockEdges.None;

    /// <summary>false (padrão): nunca recebe foco. true: janela interativa por teclado, como o painel rápido.</summary>
    protected virtual bool CanActivate => false;

    protected override bool ShowWithoutActivation => !CanActivate;

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= WS_EX_TOOLWINDOW | (CanActivate ? 0 : WS_EX_NOACTIVATE);
            return cp;
        }
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        // No Windows 10 os atributos não existem e a chamada só falha: a Region já faz os cantos.
        int pref = DWMWCP_DONOTROUND;
        DwmSetWindowAttribute(Handle, DWMWA_WINDOW_CORNER_PREFERENCE, ref pref, sizeof(int));
        int none = unchecked((int)DWMWA_COLOR_NONE);
        DwmSetWindowAttribute(Handle, DWMWA_BORDER_COLOR, ref none, sizeof(int));
        ApplyRegion();
    }

    protected override void OnSizeChanged(EventArgs e)
    {
        base.OnSizeChanged(e);
        if (IsHandleCreated) ApplyRegion();
    }

    protected override void OnLocationChanged(EventArgs e)
    {
        base.OnLocationChanged(e);
        if (IsHandleCreated) ApplyRegion(); // encostar ou desencostar da borda muda os cantos
    }

    private DockEdges _regionEdges = (DockEdges)(-1);
    private Size _regionSize;

    private void ApplyRegion()
    {
        var edges = DockedEdges;
        if (edges == _regionEdges && Size == _regionSize) return; // arrastar muda a posição a todo instante; os cantos quase nunca
        (_regionEdges, _regionSize) = (edges, Size);
        int max = Math.Min(Width, Height) / 2;
        var (tl, tr, br, bl) = PillDock.Corners(Math.Clamp(RegionRadius, 0, max), edges);
        using var path = new GraphicsPath();
        path.AddLine(tl, 0, Width - tr, 0);
        if (tr > 0) path.AddArc(Width - 2 * tr, 0, 2 * tr, 2 * tr, 270, 90);
        path.AddLine(Width, tr, Width, Height - br);
        if (br > 0) path.AddArc(Width - 2 * br, Height - 2 * br, 2 * br, 2 * br, 0, 90);
        path.AddLine(Width - br, Height, bl, Height);
        if (bl > 0) path.AddArc(0, Height - 2 * bl, 2 * bl, 2 * bl, 90, 90);
        path.AddLine(0, Height - bl, 0, tl);
        if (tl > 0) path.AddArc(0, 0, 2 * tl, 2 * tl, 180, 90);
        path.CloseFigure();
        Region = new Region(path); // o setter descarta a Region anterior
    }

    protected override void WndProc(ref Message m)
    {
        if (!CanActivate && m.Msg == WM_MOUSEACTIVATE) { m.Result = MA_NOACTIVATE; return; } // clicar não tira o foco do app da frente
        base.WndProc(ref m);
    }

    [LibraryImport("dwmapi.dll")]
    private static partial int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);
}
