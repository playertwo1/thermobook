using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Runtime.InteropServices;

namespace ThermoBook;

/// <summary>Desenha a temperatura como ícone da bandeja.</summary>
internal static partial class TrayIconRenderer
{
    public static Color TempColor(double celsius) => celsius switch
    {
        >= 90 => Color.FromArgb(255, 85, 85),
        >= 75 => Color.FromArgb(255, 165, 60),
        _ => Color.FromArgb(90, 210, 120),
    };

    /// <param name="celsius">null enquanto ainda não houver leitura.</param>
    public static Icon Render(double? celsius, int size)
    {
        using var bmp = new Bitmap(size, size);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;

            // Fundo escuro arredondado: o número fica legível em barra de tarefas clara ou escura.
            using (var bg = new SolidBrush(Color.FromArgb(235, 28, 28, 32)))
            using (var path = RoundedRect(new RectangleF(0, 0, size - 1, size - 1), size * 0.22f))
                g.FillPath(bg, path);

            string text = celsius is null ? "--" : Math.Round(celsius.Value).ToString("0");
            using var fmt = new StringFormat(StringFormat.GenericTypographic)
            {
                Alignment = StringAlignment.Center,
                LineAlignment = StringAlignment.Center,
                FormatFlags = StringFormatFlags.NoWrap | StringFormatFlags.NoClip,
            };
            // Maior fonte que cabe na largura: 2 dígitos ficam grandes, "100" encolhe.
            float fontPx = size * 0.8f;
            Font font = new("Segoe UI", fontPx, FontStyle.Bold, GraphicsUnit.Pixel);
            while (fontPx > 6 && g.MeasureString(text, font, int.MaxValue, fmt).Width > size * 0.92f)
            {
                font.Dispose();
                fontPx -= 0.5f;
                font = new Font("Segoe UI", fontPx, FontStyle.Bold, GraphicsUnit.Pixel);
            }
            using (font)
            using (var brush = new SolidBrush(celsius is null ? Color.Gainsboro : TempColor(celsius.Value)))
                g.DrawString(text, font, brush, new RectangleF(0, size * 0.03f, size, size), fmt);
        }

        // Icon.FromHandle não copia: clona e libera o HICON para não vazar a cada atualização.
        IntPtr hIcon = bmp.GetHicon();
        try { using var tmp = Icon.FromHandle(hIcon); return (Icon)tmp.Clone(); }
        finally { DestroyIcon(hIcon); }
    }

    private static GraphicsPath RoundedRect(RectangleF r, float radius)
    {
        var p = new GraphicsPath();
        float d = radius * 2;
        p.AddArc(r.X, r.Y, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DestroyIcon(IntPtr hIcon);
}
