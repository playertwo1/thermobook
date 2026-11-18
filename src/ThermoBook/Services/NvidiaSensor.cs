using System.Runtime.InteropServices;

namespace ThermoBook.Services;

/// <summary>O pedaço da NVML (biblioteca do driver NVIDIA) que o app usa. Retornos são os códigos da NVML: 0 = sucesso.</summary>
internal interface INvmlApi : IDisposable
{
    int Init();
    int DeviceCount(out uint count);

    /// <summary>Temperatura do núcleo da GPU <paramref name="index"/>, em °C.</summary>
    int Temperature(uint index, out uint celsius);
}

/// <summary>
/// Temperatura das GPUs NVIDIA pela NVML (<c>nvml.dll</c>, que vem com o driver). Sem driver NVIDIA, a leitura é sempre nula e a biblioteca
/// não é procurada de novo. Roda só na thread de fundo do <see cref="SensorService"/>; erro da NVML nunca sobe: vira nulo.
/// </summary>
internal sealed class NvidiaSensor : IDisposable
{
    private readonly Func<INvmlApi?> _load;
    private INvmlApi? _api;
    private bool _tried, _failed;

    public NvidiaSensor() : this(NvmlApi.TryLoad) { }

    /// <param name="load">Só nos testes: devolve a NVML falsa (ou null = sem driver NVIDIA).</param>
    public NvidiaSensor(Func<INvmlApi?> load) => _load = load;

    /// <returns>A temperatura da GPU mais quente, ou null (sem NVIDIA, NVML com erro ou nenhuma leitura válida).</returns>
    public double? Read()
    {
        try
        {
            if (!Start()) return null;
            if (_api!.DeviceCount(out uint count) != 0) return null;
            double? max = null;
            for (uint i = 0; i < count; i++)
                if (_api.Temperature(i, out uint c) == 0 && c is > 0 and < 150) // fora disso é leitura inválida
                    max = Math.Max(max ?? 0, c);
            return max;
        }
        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException or SEHException or MarshalDirectiveException or InvalidOperationException)
        {
            Disable();
            return null;
        }
    }

    private bool Start()
    {
        if (_failed) return false;
        if (_tried) return true;
        _tried = true;
        _api = _load();
        if (_api is null || _api.Init() != 0) { Disable(); return false; } // sem driver: não tenta de novo a cada leitura
        return true;
    }

    private void Disable()
    {
        _failed = true;
        _api?.Dispose();
        _api = null;
    }

    public void Dispose() => Disable();
}

/// <summary>Liga as funções da <c>nvml.dll</c> por ponteiro (sem dependência nova). Só procura nas pastas fixas do driver, nunca no PATH.</summary>
internal sealed class NvmlApi : INvmlApi
{
    private delegate int InitFn();
    private delegate int ShutdownFn();
    private delegate int CountFn(out uint count);
    private delegate int HandleFn(uint index, out IntPtr device);
    private delegate int TempFn(IntPtr device, int sensor, out uint celsius);

    private readonly IntPtr _lib;
    private readonly InitFn _init;
    private readonly ShutdownFn _shutdown;
    private readonly CountFn _count;
    private readonly HandleFn _handle;
    private readonly TempFn _temp;
    private bool _started;

    private NvmlApi(IntPtr lib)
    {
        _lib = lib;
        T Fn<T>(string name) where T : Delegate => Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(lib, name));
        _init = Fn<InitFn>("nvmlInit_v2");
        _shutdown = Fn<ShutdownFn>("nvmlShutdown");
        _count = Fn<CountFn>("nvmlDeviceGetCount_v2");
        _handle = Fn<HandleFn>("nvmlDeviceGetHandleByIndex_v2");
        _temp = Fn<TempFn>("nvmlDeviceGetTemperature");
    }

    /// <summary>Carrega a NVML do System32 ou da pasta do driver; null se não houver NVIDIA (ou faltar alguma função).</summary>
    public static INvmlApi? TryLoad()
    {
        string[] paths =
        [
            Path.Combine(Environment.SystemDirectory, "nvml.dll"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "NVIDIA Corporation", "NVSMI", "nvml.dll"),
        ];
        foreach (var path in paths)
        {
            if (!File.Exists(path) || !NativeLibrary.TryLoad(path, out var lib)) continue;
            try { return new NvmlApi(lib); }
            catch (Exception e) when (e is EntryPointNotFoundException or ArgumentException) { NativeLibrary.Free(lib); }
        }
        return null;
    }

    public int Init()
    {
        int r = _init();
        _started = r == 0;
        return r;
    }

    public int DeviceCount(out uint count) => _count(out count);

    public int Temperature(uint index, out uint celsius)
    {
        celsius = 0;
        int r = _handle(index, out var device);
        return r != 0 ? r : _temp(device, 0 /* NVML_TEMPERATURE_GPU */, out celsius);
    }

    public void Dispose()
    {
        if (_started) { _shutdown(); _started = false; }
        NativeLibrary.Free(_lib);
    }
}
