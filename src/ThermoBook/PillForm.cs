using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using ThermoBook.Services;

namespace ThermoBook;

/// <summary>Valores já arredondados da pílula: a janela só se redesenha quando algum deles muda.</summary>
internal readonly record struct PillState(int Temp, int Cpu, int Gpu, int Ram)
{
    public static PillState From(Snapshot s) =>
        new((int)Math.Round(s.TempMax), (int)Math.Round(s.CpuUse), (int)Math.Round(s.Gpu), (int)Math.Round(s.RamPercent));
}

/// <summary>
/// Cápsula flutuante (DESIGN.md). Não rouba o foco, fica fora da barra de tarefas e do Alt+Tab.
/// Nesta fase só a janela base e uma pintura mínima; o desenho final vem na 9.2.
/// </summary>
internal sealed partial class PillForm : Form
{
    private const int WS_EX_TOOLWINDOW = 0x80, WS_EX_NOACTIVATE = 0x08000000;
    private const int WM_MOUSEACTIVATE = 0x21, MA_NOACTIVATE = 3;
    private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33, DWMWCP_ROUND = 2;
    private static readonly Color Bg = Color.Black, Border = Color.FromArgb(0x24, 0x24, 0x24), Fg = Color.FromArgb(0xF4, 0xF4, 0xF6);

    private PillState? _state;

    public PillForm()
    {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        BackColor = Bg;
        DoubleBuffered = true;
        // Tamanhos em pixels de 96 DPI escalados pelo DPI do monitor (PerMonitorV2 no csproj).
        float k = DeviceDpi / 96f;
        ClientSize = new Size((int)(38 * k), (int)(96 * k));
        Font = new Font("Segoe UI Variable", 8.25f, FontStyle.Bold, GraphicsUnit.Point);
        if (Font.Name != "Segoe UI Variable") Font = new Font("Segoe UI", 8.25f, FontStyle.Bold, GraphicsUnit.Point);
        // Posição inicial (A3): borda direita, centro vertical, monitor principal.
        var wa = Screen.PrimaryScreen!.WorkingArea;
        Location = new Point(wa.Right - Width - (int)(8 * k), wa.Top + (wa.Height - Height) / 2);
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
        if (_state is not { } st) return;

        var tempColor = TrayIconRenderer.TempColor(st.Temp);
        string[] lines = [$"{st.Temp}°", $"{st.Cpu}", $"{st.Gpu}", $"{st.Ram}"];
        using var fmt = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
        float rowH = Height / 4f;
        for (int i = 0; i < 4; i++)
        {
            using var brush = new SolidBrush(i == 0 ? tempColor : Fg);
            g.DrawString(lines[i], Font, brush, new RectangleF(0, i * rowH, Width, rowH), fmt);
        }
    }

    [LibraryImport("dwmapi.dll")]
    private static partial int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);
}
