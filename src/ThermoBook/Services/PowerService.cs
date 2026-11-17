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

    // Subgrupos
    public static readonly Guid SubProcessor = new("54533251-82be-4824-96c1-47b60b740d00");
    public static readonly Guid SubDisk = new("0012ee47-9041-4b5d-9b77-535fba8b1442");
    // Configurações do processador
    public static readonly Guid ProcThrottleMin = new("893dee8e-2bef-41e0-89c6-b55d0929964c");
    public static readonly Guid ProcThrottleMax = new("bc5038f7-23e0-4960-96da-33abaf5935ec");
    public static readonly Guid PerfBoostMode = new("be337238-0d82-4146-a960-4f3749d470c7");
    public static readonly Guid PerfEpp = new("36687f9e-e3a5-4dbf-b1dc-15eb381c6863");
    public static readonly Guid PerfEpp1 = new("36687f9e-e3a5-4dbf-b1dc-15eb381c6864"); // núcleos de eficiência
    public static readonly Guid CpMinCores = new("0cc5b647-c1df-4637-891a-dec35c318583");
    public static readonly Guid CpMaxCores = new("ea062031-0e34-4ff1-9b6d-eb1059334028");
    public static readonly Guid CpIncreaseTime = new("2ddd5a84-5a71-437e-912a-db0b8c788732");
    public static readonly Guid CpDecreaseTime = new("dfd10d17-d5eb-45dd-877a-9a34ddd15c82");
    public static readonly Guid PerfIncreaseThreshold = new("06cadf0e-64ed-448a-8927-ce7bf90eb35d");
    public static readonly Guid PerfDecreaseThreshold = new("12a0ab44-fe28-4fa9-b3bd-4b64f44960a6");
    // Disco
    public static readonly Guid DiskIdle = new("6738e2c4-e8a5-4a42-b16a-e040e769756e");
    // Personalidade do plano (GUID_POWERSCHEME_PERSONALITY, usado na fase 7.3)
    public static readonly Guid SchemePersonality = new("245d8541-3943-4422-b025-13a784f679b7");
    // Overlays do modo de energia do Windows
    public static readonly Guid OverlayBestEfficiency = new("961cc777-2547-4f9d-8174-7d86181b8a7a");
    public static readonly Guid OverlayBalanced = Guid.Empty;
    public static readonly Guid OverlayBestPerformance = new("ded574b5-45a0-4f42-8737-46345c09c238");
    public static readonly Guid OverlayMaxPerformance = new("3af9b8d9-7c97-431d-ad78-34a8bfea439f");

    private const uint ErrorFileNotFound = 2;

    /// <summary>Configuração ausente neste hardware/plano (ex.: PERFEPP1 sem núcleos E).</summary>
    internal static bool IsMissing(uint err) => err is ErrorFileNotFound or 0x57 /* INVALID_PARAMETER */ or 0x490 /* NOT_FOUND */;

    public static uint GetTurbo() => Read(SubProcessor, PerfBoostMode)?.Ac ?? 0;
    public static uint GetLimit() => Read(SubProcessor, ProcThrottleMax)?.Ac ?? 100;

    /// <summary>Lê o valor em tomada (AC) e bateria (DC) no plano ativo; null se a configuração não existe.</summary>
    public static (uint Ac, uint Dc)? Read(Guid sub, Guid setting)
    {
        var scheme = ActiveScheme();
        var e1 = PowerReadACValueIndex(IntPtr.Zero, ref scheme, ref sub, ref setting, out uint ac);
        var e2 = PowerReadDCValueIndex(IntPtr.Zero, ref scheme, ref sub, ref setting, out uint dc);
        if (IsMissing(e1) || IsMissing(e2)) return null;
        Check(e1); Check(e2);
        return (ac, dc);
    }

    /// <summary>Grava AC e DC no plano ativo (não ativa o plano). Devolve false se a configuração não existe.</summary>
    public static bool Write(Guid sub, Guid setting, uint ac, uint dc)
    {
        var scheme = ActiveScheme();
        var e1 = PowerWriteACValueIndex(IntPtr.Zero, ref scheme, ref sub, ref setting, ac);
        if (IsMissing(e1)) return false;
        Check(e1);
        Check(PowerWriteDCValueIndex(IntPtr.Zero, ref scheme, ref sub, ref setting, dc));
        return true;
    }

    /// <summary>Faz o plano ativo valer agora; sem isso a mudança só vale no próximo boot.</summary>
    public static void Commit()
    {
        var scheme = ActiveScheme();
        Check(PowerSetActiveScheme(IntPtr.Zero, ref scheme));
    }

    public static Guid GetOverlay()
    {
        Check(PowerGetEffectiveOverlayScheme(out Guid g));
        return g;
    }

    public static void SetOverlay(Guid overlay) => Check(PowerSetActiveOverlayScheme(overlay));

    public static PowerProfile? Current() => Match(GetTurbo(), GetLimit());

    public static PowerProfile? Match(uint turbo, uint limit) =>
        Profiles.FirstOrDefault(p => p.Turbo == turbo && p.Limit == limit);

    public static void Apply(uint turbo, uint limit)
    {
        Write(SubProcessor, PerfBoostMode, turbo, turbo);
        Write(SubProcessor, ProcThrottleMax, limit, limit);
        Commit();
    }

    public static void Apply(PowerProfile p) => Apply(p.Turbo, p.Limit);

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
    private static partial uint PowerReadDCValueIndex(IntPtr rootPowerKey, ref Guid scheme, ref Guid subGroup, ref Guid setting, out uint value);

    [LibraryImport("powrprof.dll")]
    private static partial uint PowerGetEffectiveOverlayScheme(out Guid overlay);

    [LibraryImport("powrprof.dll")]
    private static partial uint PowerSetActiveOverlayScheme(Guid overlay);

    [LibraryImport("powrprof.dll")]
    private static partial uint PowerWriteACValueIndex(IntPtr rootPowerKey, ref Guid scheme, ref Guid subGroup, ref Guid setting, uint value);

    [LibraryImport("powrprof.dll")]
    private static partial uint PowerWriteDCValueIndex(IntPtr rootPowerKey, ref Guid scheme, ref Guid subGroup, ref Guid setting, uint value);

    [LibraryImport("kernel32.dll")]
    private static partial IntPtr LocalFree(IntPtr mem);
}
