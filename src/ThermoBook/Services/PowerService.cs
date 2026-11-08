using System.Runtime.InteropServices;

namespace ThermoBook.Services;

internal sealed record PowerProfile(string Name, uint Turbo, uint Limit, string Description);

/// <summary>
/// Lê e grava o Turbo Boost e o limite da CPU no plano de energia ativo (tomada e bateria),
/// pela mesma API que o powercfg usa. Não precisa de administrador.
/// </summary>
internal static partial class PowerService
{
    public static readonly PowerProfile[] Profiles =
    [
        new("Frio", 0, 100, "Turbo desligado. Bem mais frio, quase sem perda no dia a dia."),
        new("Silencioso", 0, 80, "Turbo desligado e CPU limitada a 80%. O mais frio e silencioso."),
        new("Normal", 3, 100, "Padrão de fábrica: turbo em modo eficiência."),
        new("Máximo", 2, 100, "Turbo agressivo: mais desempenho, mais calor."),
    ];

    public static readonly Dictionary<uint, string> TurboNames = new()
    {
        [0] = "desligado", [1] = "ligado", [2] = "agressivo", [3] = "eficiência", [4] = "eficiência agressiva",
    };

    private static Guid SubProcessor = new("54533251-82be-4824-96c1-47b60b740d00");
    private static Guid PerfBoostMode = new("be337238-0d82-4146-a960-4f3749d470c7");
    private static Guid ProcThrottleMax = new("bc5038f7-23e0-4960-96da-33abaf5935ec");

    public static uint GetTurbo() => ReadAc(PerfBoostMode);
    public static uint GetLimit() => ReadAc(ProcThrottleMax);

    /// <summary>Perfil cujos valores batem com o plano ativo, ou null se for uma combinação personalizada.</summary>
    public static PowerProfile? Current() => Match(GetTurbo(), GetLimit());

    public static PowerProfile? Match(uint turbo, uint limit) =>
        Profiles.FirstOrDefault(p => p.Turbo == turbo && p.Limit == limit);

    public static void Apply(uint turbo, uint limit)
    {
        var scheme = ActiveScheme();
        Check(PowerWriteACValueIndex(IntPtr.Zero, ref scheme, ref SubProcessor, ref PerfBoostMode, turbo));
        Check(PowerWriteDCValueIndex(IntPtr.Zero, ref scheme, ref SubProcessor, ref PerfBoostMode, turbo));
        Check(PowerWriteACValueIndex(IntPtr.Zero, ref scheme, ref SubProcessor, ref ProcThrottleMax, limit));
        Check(PowerWriteDCValueIndex(IntPtr.Zero, ref scheme, ref SubProcessor, ref ProcThrottleMax, limit));
        Check(PowerSetActiveScheme(IntPtr.Zero, ref scheme)); // sem isso a mudança só vale no próximo boot
    }

    public static void Apply(PowerProfile p) => Apply(p.Turbo, p.Limit);

    private static uint ReadAc(Guid setting)
    {
        var scheme = ActiveScheme();
        Check(PowerReadACValueIndex(IntPtr.Zero, ref scheme, ref SubProcessor, ref setting, out uint value));
        return value;
    }

    private static Guid ActiveScheme()
    {
        Check(PowerGetActiveScheme(IntPtr.Zero, out IntPtr ptr));
        try { return Marshal.PtrToStructure<Guid>(ptr); }
        finally { LocalFree(ptr); }
    }

    private static void Check(uint err)
    {
        if (err != 0) throw new System.ComponentModel.Win32Exception((int)err);
    }

    [LibraryImport("powrprof.dll")]
    private static partial uint PowerGetActiveScheme(IntPtr userRootPowerKey, out IntPtr activePolicyGuid);

    [LibraryImport("powrprof.dll")]
    private static partial uint PowerSetActiveScheme(IntPtr userRootPowerKey, ref Guid schemeGuid);

    [LibraryImport("powrprof.dll")]
    private static partial uint PowerReadACValueIndex(IntPtr rootPowerKey, ref Guid scheme, ref Guid subGroup, ref Guid setting, out uint value);

    [LibraryImport("powrprof.dll")]
    private static partial uint PowerWriteACValueIndex(IntPtr rootPowerKey, ref Guid scheme, ref Guid subGroup, ref Guid setting, uint value);

    [LibraryImport("powrprof.dll")]
    private static partial uint PowerWriteDCValueIndex(IntPtr rootPowerKey, ref Guid scheme, ref Guid subGroup, ref Guid setting, uint value);

    [LibraryImport("kernel32.dll")]
    private static partial IntPtr LocalFree(IntPtr mem);
}
