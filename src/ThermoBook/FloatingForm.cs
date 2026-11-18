using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace ThermoBook;

/// <summary>
/// Base das janelas flutuantes (pílula e flyout): sem borda, sempre no topo, sem foco, fora da barra de tarefas e do Alt+Tab,
/// com cantos arredondados (DWM no Windows 11; <see cref="Region"/> como plano B).
/// </summary>
internal abstract partial class FloatingForm : Form
{
    private const int WS_EX_TOOLWINDOW = 0x80, WS_EX_NOACTIVATE = 0x08000000;
    private const int WM_MOUSEACTIVATE = 0x21, MA_NOACTIVATE = 3;
    private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33, DWMWCP_ROUND = 2;

    protected FloatingForm()
    {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        BackColor = Color.Black;
        DoubleBuffered = true;
    }

    /// <summary>Raio do plano B (Region). A pílula é uma cápsula completa.</summary>
    protected virtual int RegionRadius => Math.Min(Width, Height) / 2;

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
        int d = Math.Max(2, RegionRadius * 2);
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

    [LibraryImport("dwmapi.dll")]
    private static partial int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);
}
