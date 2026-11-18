namespace ThermoBook;

/// <summary>Posição salva da pílula num monitor (canto superior esquerdo em coordenadas de tela).</summary>
internal sealed record PillPosition(int X, int Y, bool Horizontal);

internal readonly record struct DockResult(Point Location, Orientation Orientation);

/// <summary>Encaixe e restauração da posição da pílula. Lógica pura, sem janela: testável.</summary>
internal static class PillDock
{
    public const int ThresholdLogical = 24;

    public static string MonitorKey(string deviceName, Rectangle bounds) => $"{deviceName}|{bounds.Width}x{bounds.Height}";

    /// <summary>
    /// Solta a pílula em <paramref name="topLeft"/>: a até <paramref name="threshold"/> de uma borda encaixa nela
    /// (borda lateral → vertical, topo/base → horizontal); perto de duas, encaixa no canto. Longe de tudo, só mantém dentro da área.
    /// </summary>
    public static DockResult Snap(Point topLeft, Size current, Orientation currentOrientation, Size vertical, Size horizontal, Rectangle area, int threshold)
    {
        int dl = topLeft.X - area.Left, dr = area.Right - (topLeft.X + current.Width);
        int dt = topLeft.Y - area.Top, db = area.Bottom - (topLeft.Y + current.Height);
        bool left = dl <= threshold && dl <= dr, right = dr <= threshold && dr < dl;
        bool top = dt <= threshold && dt <= db, bottom = db <= threshold && db < dt;

        var o = currentOrientation;
        bool side = left || right, edge = top || bottom;
        if (side && !edge) o = Orientation.Vertical;
        else if (edge && !side) o = Orientation.Horizontal;
        else if (side && edge) // canto: vale a borda mais próxima; empate fica vertical
            o = (left ? dl : dr) <= (top ? dt : db) ? Orientation.Vertical : Orientation.Horizontal;

        var size = o == Orientation.Vertical ? vertical : horizontal;
        int x = left ? area.Left : right ? area.Right - size.Width : topLeft.X;
        int y = top ? area.Top : bottom ? area.Bottom - size.Height : topLeft.Y;
        return new DockResult(Clamp(new Point(x, y), size, area), o);
    }

    /// <summary>
    /// Posição ao abrir: a salva do monitor atual se ele ainda existe; senão o canto padrão do monitor principal
    /// (borda direita, centro vertical). Devolve também o monitor usado.
    /// </summary>
    public static (string Monitor, DockResult Result) Resolve(
        string? monitor, IReadOnlyDictionary<string, PillPosition>? positions,
        IReadOnlyList<(string Key, Rectangle Area)> screens, string primaryKey, Size vertical, Size horizontal)
    {
        if (monitor is not null && positions is not null && positions.TryGetValue(monitor, out var saved)
            && screens.FirstOrDefault(s => s.Key == monitor) is { Key: not null } scr)
        {
            var o = saved.Horizontal ? Orientation.Horizontal : Orientation.Vertical;
            var size = saved.Horizontal ? horizontal : vertical;
            return (monitor, new DockResult(Clamp(new Point(saved.X, saved.Y), size, scr.Area), o));
        }
        var primary = screens.FirstOrDefault(s => s.Key == primaryKey);
        var area = primary.Key is null ? screens[0].Area : primary.Area;
        var key = primary.Key ?? screens[0].Key;
        return (key, new DockResult(new Point(area.Right - vertical.Width, area.Top + (area.Height - vertical.Height) / 2), Orientation.Vertical));
    }

    private static Point Clamp(Point p, Size size, Rectangle area) => new(
        Math.Clamp(p.X, area.Left, Math.Max(area.Left, area.Right - size.Width)),
        Math.Clamp(p.Y, area.Top, Math.Max(area.Top, area.Bottom - size.Height)));
}
