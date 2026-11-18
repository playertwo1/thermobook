using ThermoBook.Services;

namespace ThermoBook;

/// <summary>
/// Painel rápido (DESIGN.md): 6 modos, segundo plano, desligar tela e atalho para o painel completo.
/// Ao contrário da pílula e do flyout, pode receber foco (Tab, setas, Enter, Esc) e fecha ao perder a ativação.
/// </summary>
internal sealed class QuickPanel : FloatingForm
{
    private static readonly Color Bg = Color.Black, Raised = Color.FromArgb(0x14, 0x14, 0x14), Accent = Color.FromArgb(0x40, 0x9C, 0xFF),
        Fg = Color.FromArgb(0xF4, 0xF4, 0xF6), Muted = Color.FromArgb(0xA9, 0xAB, 0xB5), BorderColor = Color.FromArgb(0x24, 0x24, 0x24);

    private readonly Dictionary<PowerMode, Button> _modeButtons = [];
    private readonly Button _background;
    private readonly ToolTip _tip = new();

    protected override bool CanActivate => true;
    protected override int RegionRadius => (int)Math.Round(14 * DeviceDpi / 96f);

    public QuickPanel(Action<PowerMode> applyMode, Action toggleBackground, Action turnOffDisplay, Action openFull)
    {
        SuspendLayout();
        // Tamanhos em pixels de 96 DPI; o WinForms escala para o DPI do monitor (PerMonitorV2 no csproj).
        AutoScaleDimensions = new SizeF(96, 96);
        AutoScaleMode = AutoScaleMode.Dpi;
        Font = new Font("Segoe UI Variable", 13, FontStyle.Regular, GraphicsUnit.Pixel);
        if (Font.Name != "Segoe UI Variable") Font = new Font("Segoe UI", 13, FontStyle.Regular, GraphicsUnit.Pixel);
        BackColor = Bg;
        ForeColor = Fg;
        KeyPreview = true;
        ClientSize = new Size(280, 244);

        const int pad = 10, gap = 4, rowH = 36;
        int inner = 280 - 2 * pad;
        var small = new Font(Font.FontFamily, 12, GraphicsUnit.Pixel); // rótulos longos das ações cabem em 84 px
        Controls.Add(Caption("Modo de energia", pad, 10));

        int bw = (inner - 2 * gap) / 3;
        for (int i = 0; i < PowerService.Modes.Length; i++)
        {
            var m = PowerService.Modes[i];
            var b = MakeButton(m.Name, pad + i % 3 * (bw + gap), 32 + i / 3 * (rowH + gap), bw, rowH);
            b.Click += (_, _) => applyMode(m);
            _tip.SetToolTip(b, m.Description);
            _modeButtons[m] = b;
            Controls.Add(b);
        }

        int ay = 32 + 2 * (rowH + gap) + 8;
        Controls.Add(Caption("Ações", pad, ay - 18 + 8));
        int aw = (inner - 2 * gap) / 3, ah = 52;
        ay += 12;
        _background = MakeButton("Segundo plano", pad, ay, aw, ah);
        _background.Click += (_, _) => toggleBackground();
        _tip.SetToolTip(_background, "Tampa fechada e inatividade não suspendem o PC.");
        var screen = MakeButton("Desligar tela", pad + aw + gap, ay, aw, ah);
        screen.Click += (_, _) => { Hide(); turnOffDisplay(); };
        var tools = MakeButton("Ferramentas", pad + 2 * (aw + gap), ay, aw, ah);
        _tip.SetToolTip(tools, "Ferramentas… (abre o painel completo)");
        tools.Click += (_, _) => { Hide(); openFull(); };
        foreach (var b in new[] { _background, screen, tools }) b.Font = small;
        Controls.AddRange([_background, screen, tools]);

        var full = MakeButton("Abrir painel completo", pad, ay + ah + gap + 4, inner, rowH);
        full.Click += (_, _) => { Hide(); openFull(); };
        Controls.Add(full);
        ResumeLayout(true);
    }

    private Label Caption(string text, int x, int y) =>
        new() { Text = text, Location = new Point(x, y), AutoSize = true, ForeColor = Muted, Font = new Font(Font.FontFamily, 11, GraphicsUnit.Pixel), BackColor = Bg };

    private static Button MakeButton(string text, int x, int y, int w, int h)
    {
        var b = new Button
        {
            Text = text, Bounds = new Rectangle(x, y, w, h), FlatStyle = FlatStyle.Flat, BackColor = Raised, ForeColor = Fg,
            UseVisualStyleBackColor = false, TabStop = true,
        };
        b.FlatAppearance.BorderSize = 0;
        b.FlatAppearance.MouseOverBackColor = Color.FromArgb(0x24, 0x24, 0x24);
        // Anel de foco visível para o teclado.
        b.GotFocus += (_, _) => { b.FlatAppearance.BorderSize = 2; b.FlatAppearance.BorderColor = Fg; };
        b.LostFocus += (_, _) => b.FlatAppearance.BorderSize = 0;
        return b;
    }

    /// <summary>Marca o modo atual (null = Personalizado) e o estado do segundo plano.</summary>
    public void SetState(PowerMode? current, bool background)
    {
        foreach (var (m, b) in _modeButtons) Highlight(b, m == current);
        _background.Text = background ? "Segundo\nplano ligado" : "Segundo\nplano desligado";
        Highlight(_background, background);
    }

    private static void Highlight(Button b, bool on)
    {
        b.BackColor = on ? Accent : Raised;
        b.ForeColor = on ? Color.Black : Fg; // preto sobre o azul: contraste muito melhor que branco
        b.FlatAppearance.MouseOverBackColor = on ? Accent : Color.FromArgb(0x24, 0x24, 0x24);
    }

    protected override void OnDeactivate(EventArgs e)
    {
        base.OnDeactivate(e);
        Hide(); // clicar fora fecha
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == Keys.Escape) { Hide(); return true; }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        using var pen = new Pen(BorderColor);
        e.Graphics.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _tip.Dispose();
        base.Dispose(disposing);
    }
}
