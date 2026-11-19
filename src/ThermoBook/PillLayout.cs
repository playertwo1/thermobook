namespace ThermoBook;

/// <summary>Uma linha da pílula: retângulo total, onde vai o valor e (nas métricas) o medidor de 3 px.</summary>
internal readonly record struct PillItem(Rectangle Row, Rectangle Value, Rectangle? Meter);

/// <summary>
/// Layout da pílula (DESIGN.md): vertical com 40 px de largura e linhas de 32 px, ou horizontal com os mesmos
/// itens lado a lado. Um só código para as duas orientações; tudo em pixels de 96 DPI vezes <c>scale</c>.
/// Itens: 0 temperatura, 1 CPU, 2 GPU, 3 RAM, 4 disco, 5 modo (o ponto fica em <see cref="PillItem.Value"/>).
/// </summary>
internal static class PillLayout
{
    public const int ItemCount = 6, Temp = 0, Cpu = 1, Gpu = 2, Ram = 3, Disk = 4, Mode = 5;

    private const int Thickness = 40, MetricLong = 32, ModeLong = 22, HorizontalMetric = 36, HorizontalMode = 26, MeterPx = 3, DotPx = 7;

    public static (Size Size, PillItem[] Items) Compute(Orientation orientation, float scale)
    {
        int S(int v) => (int)Math.Round(v * scale);
        bool vertical = orientation == Orientation.Vertical;
        int thick = S(Thickness), meter = Math.Max(1, S(MeterPx)), pad = S(2);
        var items = new PillItem[ItemCount];
        int pos = 0;
        for (int i = 0; i < ItemCount; i++)
        {
            bool isMode = i == Mode;
            int len = S(vertical ? (isMode ? ModeLong : MetricLong) : (isMode ? HorizontalMode : HorizontalMetric));
            var row = vertical ? new Rectangle(0, pos, thick, len) : new Rectangle(pos, 0, len, thick);
            pos += len;
            if (isMode)
            {
                int d = S(DotPx);
                items[i] = new PillItem(row, new Rectangle(row.X + (row.Width - d) / 2, row.Y + (row.Height - d) / 2, d, d), null);
            }
            else if (i == Temp)
                items[i] = new PillItem(row, Rectangle.Inflate(row, -pad, -pad), null);
            else if (vertical) // medidor embaixo do número
                items[i] = new PillItem(row,
                    new Rectangle(row.X + pad, row.Y + pad, row.Width - 2 * pad, row.Height - 3 * pad - meter),
                    new Rectangle(row.X + S(6), row.Bottom - pad - meter, row.Width - S(12), meter));
            else // medidor à esquerda do número
                items[i] = new PillItem(row,
                    new Rectangle(row.X + pad + meter + pad, row.Y + pad, row.Width - 3 * pad - meter, row.Height - 2 * pad),
                    new Rectangle(row.X + pad, row.Y + S(6), meter, row.Height - S(12)));
        }
        var size = vertical ? new Size(thick, pos) : new Size(pos, thick);
        return (size, items);
    }
}
