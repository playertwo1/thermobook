namespace ThermoBook;

/// <summary>Cores do DESIGN.md e o botão escuro usado pelas abas do painel completo.</summary>
internal static class Theme
{
    public static readonly Color Bg = Color.Black, Raised = Color.FromArgb(0x14, 0x14, 0x14), Border = Color.FromArgb(0x24, 0x24, 0x24),
        Fg = Color.FromArgb(0xF4, 0xF4, 0xF6), Muted = Color.FromArgb(0xA9, 0xAB, 0xB5), Accent = Color.FromArgb(0x40, 0x9C, 0xFF);

    /// <summary>Botão chapado em <see cref="Raised"/>, com anel de foco do teclado (mesmo estilo do painel rápido).</summary>
    public static Button Button(string text, int width = 0)
    {
        var b = new Button
        {
            Text = text, FlatStyle = FlatStyle.Flat, BackColor = Raised, ForeColor = Fg, UseVisualStyleBackColor = false,
            Height = 32, Margin = new Padding(4, 3, 4, 3),
        };
        if (width > 0) b.Width = width; else b.AutoSize = true;
        b.FlatAppearance.BorderSize = 0;
        b.FlatAppearance.MouseOverBackColor = Border;
        b.GotFocus += (_, _) => { b.FlatAppearance.BorderSize = 2; b.FlatAppearance.BorderColor = Fg; };
        b.LostFocus += (_, _) => b.FlatAppearance.BorderSize = 0;
        return b;
    }

    /// <summary>Selecionado em <see cref="Accent"/> com texto preto (contraste melhor que branco sobre o azul).</summary>
    public static void Highlight(Button b, bool on)
    {
        b.BackColor = on ? Accent : Raised;
        b.ForeColor = on ? Color.Black : Fg;
        b.FlatAppearance.MouseOverBackColor = on ? Accent : Border;
    }

    public static Label Header(string text) =>
        new() { Text = text, AutoSize = true, Font = new Font("Segoe UI Semibold", 11f), ForeColor = Fg, Margin = new Padding(0, 10, 0, 4) };

    public static Label Note(string text) =>
        new() { Text = text, AutoSize = true, ForeColor = Muted, MaximumSize = new Size(640, 0), Margin = new Padding(3, 0, 3, 6) };
}
