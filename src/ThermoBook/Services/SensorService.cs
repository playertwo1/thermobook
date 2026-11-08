using System.Management;
using System.Text.RegularExpressions;

namespace ThermoBook.Services;

/// <summary>Uma leitura de todos os sensores.</summary>
internal sealed record Snapshot(
    double CpuUse, double CpuGHz, double CpuPerf,
    double Gpu, double Gpu3D, double GpuVideo, double GpuMem,
    double RamUsed, double RamTotal,
    double DiskActive, double DiskRead, double DiskWrite, double CFree,
    double[] Temps)
{
    public double TempMax => Temps.Length > 0 ? Temps.Max() : 0;
    public double RamPercent => RamTotal > 0 ? RamUsed / RamTotal * 100 : 0;
    public bool InTurbo => CpuPerf > 101;
}

/// <summary>
/// Lê os contadores de desempenho do Windows via WMI numa thread de fundo
/// (as consultas de GPU e disco levam ~1 s e travariam a interface).
/// Não precisa de administrador nem de driver.
/// </summary>
internal sealed class SensorService : IDisposable
{
    private readonly Thread _thread;
    private volatile bool _stop;
    private volatile Snapshot? _latest;

    public Snapshot? Latest => _latest;
    public string? LastError { get; private set; }

    public SensorService()
    {
        _thread = new Thread(Loop) { IsBackground = true, Name = "ThermoBook sensores", Priority = ThreadPriority.BelowNormal };
        _thread.Start();
    }

    private void Loop()
    {
        while (!_stop)
        {
            try
            {
                _latest = Read();
                LastError = null;
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
            }
            Thread.Sleep(700);
        }
    }

    public static Snapshot Read()
    {
        var cpu = First("SELECT PercentProcessorUtility, PercentProcessorPerformance, ProcessorFrequency FROM Win32_PerfFormattedData_Counters_ProcessorInformation WHERE Name='_Total'");
        double perf = D(cpu?["PercentProcessorPerformance"]);

        var engines = Query("SELECT Name, UtilizationPercentage FROM Win32_PerfFormattedData_GPUPerformanceCounters_GPUEngine")
            .Select(o => ((string)o["Name"], D(o["UtilizationPercentage"])));
        var gpu = GpuAggregator.Aggregate(engines);

        double gpuMem = Query("SELECT DedicatedUsage, SharedUsage FROM Win32_PerfFormattedData_GPUPerformanceCounters_GPUAdapterMemory")
            .Select(o => D(o["DedicatedUsage"]) + D(o["SharedUsage"]))
            .DefaultIfEmpty(0).Max();

        var os = First("SELECT TotalVisibleMemorySize, FreePhysicalMemory FROM Win32_OperatingSystem");
        double ramTotal = D(os?["TotalVisibleMemorySize"]) * 1024;
        double ramFree = D(os?["FreePhysicalMemory"]) * 1024;

        var disk = First("SELECT PercentIdleTime, DiskReadBytesPersec, DiskWriteBytesPersec FROM Win32_PerfFormattedData_PerfDisk_PhysicalDisk WHERE Name='_Total'");
        var c = First("SELECT FreeSpace FROM Win32_LogicalDisk WHERE DeviceID='C:'");

        // HighPrecisionTemperature vem em décimos de Kelvin.
        var temps = Query("SELECT HighPrecisionTemperature FROM Win32_PerfFormattedData_Counters_ThermalZoneInformation")
            .Select(o => Math.Round((D(o["HighPrecisionTemperature"]) - 2732) / 10.0))
            .Where(t => t > 0 && t < 150)
            .ToArray();

        return new Snapshot(
            CpuUse: Math.Min(100, D(cpu?["PercentProcessorUtility"])),
            CpuGHz: D(cpu?["ProcessorFrequency"]) * perf / 100_000.0,
            CpuPerf: perf,
            Gpu: gpu.All, Gpu3D: gpu.ThreeD, GpuVideo: gpu.Video, GpuMem: gpuMem,
            RamUsed: ramTotal - ramFree, RamTotal: ramTotal,
            DiskActive: Math.Clamp(100 - D(disk?["PercentIdleTime"]), 0, 100),
            DiskRead: D(disk?["DiskReadBytesPersec"]),
            DiskWrite: D(disk?["DiskWriteBytesPersec"]),
            CFree: D(c?["FreeSpace"]),
            Temps: temps);
    }

    private static List<ManagementBaseObject> Query(string wql)
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(wql);
            using var results = searcher.Get();
            return results.Cast<ManagementBaseObject>().ToList();
        }
        catch (ManagementException)
        {
            return []; // classe inexistente nesta máquina (ex.: VM sem contadores de GPU): só esse cartão fica zerado
        }
    }

    private static ManagementBaseObject? First(string wql) => Query(wql).FirstOrDefault();

    private static double D(object? v) => v is null ? 0 : Convert.ToDouble(v);

    public void Dispose() => _stop = true;
}

/// <summary>Uso da GPU como no Gerenciador de Tarefas: soma por motor, máximo por tipo.</summary>
internal static partial class GpuAggregator
{
    public readonly record struct GpuUsage(double All, double ThreeD, double Video);

    private static readonly string[] VideoTypes = ["VideoDecode", "VideoEncode", "VideoProcessing", "Video Codec"];

    [GeneratedRegex(@"(luid_.+_eng_\d+)_engtype_(.*)$")]
    private static partial Regex EngineName();

    public static GpuUsage Aggregate(IEnumerable<(string Name, double Util)> instances)
    {
        var perEngine = new Dictionary<string, (string Type, double Sum)>();
        foreach (var (name, util) in instances)
        {
            var m = EngineName().Match(name);
            if (!m.Success) continue;
            var key = m.Groups[1].Value;
            var prev = perEngine.GetValueOrDefault(key, (m.Groups[2].Value, 0));
            perEngine[key] = (prev.Item1, prev.Item2 + util);
        }

        double MaxOf(Func<string, bool> typeFilter) =>
            Math.Min(100, perEngine.Values.Where(e => typeFilter(e.Type)).Select(e => e.Sum).DefaultIfEmpty(0).Max());

        return new GpuUsage(
            All: MaxOf(_ => true),
            ThreeD: MaxOf(t => t == "3D"),
            Video: MaxOf(t => VideoTypes.Contains(t)));
    }
}
