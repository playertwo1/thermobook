using System.Text;

namespace ThermoBook.Services;

/// <summary>Uma configuração lida do plano ativo ao lado do valor esperado pelo modo. Null = não existe neste hardware.</summary>
internal sealed record DiagRow(string Name, (uint Ac, uint Dc)? Actual, (uint Ac, uint Dc) Expected)
{
    public bool? Ok => Actual is null ? null : Actual.Value == Expected;
}

internal static class PowerDiagnostics
{
    public static List<DiagRow> Rows(PowerMode m, Func<Guid, Guid, (uint, uint)?> read) =>
    [
        new("CPU mínima (%)", read(PowerService.SubProcessor, PowerService.ProcThrottleMin), (m.CpuMinAc, m.CpuMinDc)),
        new("CPU máxima (%)", read(PowerService.SubProcessor, PowerService.ProcThrottleMax), (m.CpuMax, m.CpuMax)),
        new("Turbo Boost", read(PowerService.SubProcessor, PowerService.PerfBoostMode), (m.Turbo, m.Turbo)),
        new("EPP", read(PowerService.SubProcessor, PowerService.PerfEpp), (m.Epp, m.Epp)),
        new("Núcleos mínimos (%)", read(PowerService.SubProcessor, PowerService.CpMinCores), (m.CoresMinAc, m.CoresMinDc)),
        new("Disco ocioso (s)", read(PowerService.SubDisk, PowerService.DiskIdle), (m.DiskIdleSeconds, m.DiskIdleSeconds)),
    ];

    public static string Format(PowerMode m, IEnumerable<DiagRow> rows, Guid overlay)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Modo comparado: {m.Name}");
        sb.AppendLine($"{"Configuração",-22}{"Atual AC/DC",-14}{"Esperado AC/DC",-16}Situação");
        foreach (var r in rows)
        {
            string actual = r.Actual is { } a ? $"{a.Ac}/{a.Dc}" : "-";
            string state = r.Ok switch { true => "ok", false => "DIFERENTE", null => "ausente no hardware" };
            sb.AppendLine($"{r.Name,-22}{actual,-14}{$"{r.Expected.Ac}/{r.Expected.Dc}",-16}{state}");
        }
        sb.Append($"{"Overlay",-22}{OverlayName(overlay),-30}esperado {OverlayName(m.Overlay)}{(overlay == m.Overlay ? " (ok)" : " (DIFERENTE)")}");
        return sb.ToString();
    }

    public static string OverlayName(Guid g) =>
        g == PowerService.OverlayBestEfficiency ? "melhor eficiência"
        : g == PowerService.OverlayBalanced ? "equilibrado"
        : g == PowerService.OverlayBestPerformance ? "melhor desempenho"
        : g == PowerService.OverlayMaxPerformance ? "máximo desempenho"
        : g.ToString();
}
