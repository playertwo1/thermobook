using ThermoBook.Services;

namespace ThermoBook;

/// <summary>Uma linha do flyout; <paramref name="Temp"/> != null pinta o texto pela cor de temperatura.</summary>
internal readonly record struct FlyoutLine(string Text, double? Temp = null);

/// <summary>Texto do flyout (DESIGN.md): o detalhe que não cabe na pílula.</summary>
internal static class FlyoutText
{
    public static string Heat(double celsius) => celsius >= 90 ? "quente demais" : celsius >= 75 ? "quente" : "normal";

    private static string Pct(double v) => double.IsFinite(v) ? $"{v:0}%" : "–";

    public static List<FlyoutLine> Lines(Snapshot s, PowerMode? mode, bool background, bool auto = false)
    {
        var l = new List<FlyoutLine>();
        if (s.Temps.Length == 0) l.Add(new("Temperatura: indisponível"));
        else
            for (int i = 0; i < s.Temps.Length; i++)
            {
                double t = s.Temps[i];
                string zone = s.Temps.Length > 1 ? $" (zona {i + 1})" : "";
                l.Add(new($"Temperatura{zone}: {t:0} °C, {Heat(t)}", t));
            }
        l.Add(new($"CPU: {Pct(s.CpuUse)} · {s.CpuGHz:0.00} GHz{(s.InTurbo ? " · em turbo" : "")}"));
        l.Add(new($"GPU: 3D {Pct(s.Gpu3D)} · vídeo {Pct(s.GpuVideo)}"));
        if (s.GpuTemp is { } g) l.Add(new($"GPU NVIDIA: {g:0} °C, {Heat(g)}", g));
        l.Add(new(s.RamTotal > 0 ? $"RAM: {MainForm.Bytes(s.RamUsed)} de {MainForm.Bytes(s.RamTotal)}" : "RAM: indisponível"));
        l.Add(new($"Disco: {Pct(s.DiskActive)}"));
        l.Add(new($"Modo: {mode?.Name ?? "Personalizado"}{(auto ? " · automático" : "")}"));
        l.Add(new($"Segundo plano: {(background ? "ligado" : "desligado")}"));
        return l;
    }
}

/// <summary>Posição do flyout: ao lado da pílula, do lado oposto à borda, sempre dentro da área de trabalho.</summary>
internal static class FlyoutPlacement
{
    public static Point Place(Rectangle pill, Orientation orientation, Size flyout, Rectangle area, int gap)
    {
        int x, y;
        if (orientation == Orientation.Vertical)
        {
            bool onLeft = pill.X + pill.Width / 2 < area.Left + area.Width / 2;
            x = onLeft ? pill.Right + gap : pill.Left - gap - flyout.Width;
            y = pill.Top + (pill.Height - flyout.Height) / 2;
        }
        else
        {
            bool onTop = pill.Y + pill.Height / 2 < area.Top + area.Height / 2;
            y = onTop ? pill.Bottom + gap : pill.Top - gap - flyout.Height;
            x = pill.Left + (pill.Width - flyout.Width) / 2;
        }
        return new Point(
            Math.Clamp(x, area.Left, Math.Max(area.Left, area.Right - flyout.Width)),
            Math.Clamp(y, area.Top, Math.Max(area.Top, area.Bottom - flyout.Height)));
    }
}

/// <summary>Janela do flyout. Não rouba foco (herda de <see cref="FloatingForm"/>).</summary>
internal sealed class FlyoutForm : FloatingForm
{
    private static readonly Color Border = Color.FromArgb(0x24, 0x24, 0x24), Fg = Color.FromArgb(0xF4, 0xF4, 0xF6);
    private List<FlyoutLine> _lines = [];
    private float _scale = 1f;
    private Font? _font;

    protected override int RegionRadius => (int)Math.Round(14 * _scale);

    public void SetLines(List<FlyoutLine> lines, float scale)
    {
        if (_lines.SequenceEqual(lines) && _scale == scale) return;
        // A fonte só muda com a escala (DPI). Nunca descarta a anterior em uso pelo controle: o WinForms ainda a referencia
        // (e antes do primeiro SetLines, Font é a DefaultFont compartilhada).
        if (_font is null || _scale != scale)
        {
            var f = new Font("Segoe UI Variable", 13 * scale, FontStyle.Regular, GraphicsUnit.Pixel);
            if (f.Name != "Segoe UI Variable") { f.Dispose(); f = new Font("Segoe UI", 13 * scale, FontStyle.Regular, GraphicsUnit.Pixel); }
            _font = f;
            Font = f;
        }
        _lines = lines;
        _scale = scale;
        int pad = (int)Math.Round(12 * scale), lineH = Font.Height + (int)Math.Round(4 * scale);
        int w = lines.Max(l => TextRenderer.MeasureText(l.Text, Font, Size.Empty, TextFormatFlags.NoPadding).Width);
        ClientSize = new Size(w + 2 * pad, lines.Count * lineH + 2 * pad - (int)Math.Round(4 * scale));
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        using (var pen = new Pen(Border)) e.Graphics.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
        int pad = (int)Math.Round(12 * _scale), lineH = Font.Height + (int)Math.Round(4 * _scale);
        for (int i = 0; i < _lines.Count; i++)
        {
            var color = _lines[i].Temp is { } t ? TrayIconRenderer.TempColor(t) : Fg;
            TextRenderer.DrawText(e.Graphics, _lines[i].Text, Font, new Point(pad, pad + i * lineH), color, TextFormatFlags.NoPadding);
        }
    }
}

/// <summary>Liga o flyout à pílula: abre 300 ms depois de o mouse entrar, fecha 150 ms depois de sair.</summary>
internal sealed class PillFlyout : IDisposable
{
    private const int GapLogical = 8;
    private readonly PillForm _pill;
    private readonly FlyoutForm _form = new();
    private readonly System.Windows.Forms.Timer _show = new() { Interval = 300 }, _hide = new() { Interval = 150 };
    private Snapshot? _latest;

    public PillFlyout(PillForm pill)
    {
        _pill = pill;
        pill.MouseEnter += (_, _) => { _hide.Stop(); if (!_form.Visible) _show.Start(); };
        pill.MouseLeave += (_, _) => { _show.Stop(); if (_form.Visible) _hide.Start(); };
        pill.MouseDown += (_, _) => Hide(); // arrastar ou clicar fecha o flyout
        pill.VisibleChanged += (_, _) => { if (!pill.Visible) Hide(); };
        _show.Tick += (_, _) => { _show.Stop(); if (pill.Visible && _latest is not null) ShowNow(); };
        _hide.Tick += (_, _) => { _hide.Stop(); _form.Hide(); };
    }

    public void Update(Snapshot s)
    {
        _latest = s;
        if (_form.Visible) Refresh();
    }

    private void Refresh()
    {
        if (_latest is null) return;
        float scale = _pill.DeviceDpi / 96f;
        _form.SetLines(FlyoutText.Lines(_latest, _pill.Mode, _pill.Background, _pill.Auto), scale);
        var area = Screen.FromControl(_pill).WorkingArea;
        _form.Location = FlyoutPlacement.Place(_pill.Bounds, _pill.PillOrientation, _form.Size, area, (int)Math.Round(GapLogical * scale));
    }

    private void ShowNow()
    {
        Refresh();
        _form.Show();
    }

    private void Hide()
    {
        _show.Stop();
        _hide.Stop();
        _form.Hide();
    }

    public void Dispose()
    {
        _show.Dispose();
        _hide.Dispose();
        _form.Dispose();
    }
}
