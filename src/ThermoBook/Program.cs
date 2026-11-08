using System.Runtime.InteropServices;
using ThermoBook.Services;

namespace ThermoBook;

static partial class Program
{
    [STAThread]
    static int Main(string[] args)
    {
        if (args.Contains("--diagnostico")) return Diagnose();
        if (args.Contains("--restaurar")) return Restore();

        using var mutex = new Mutex(true, @"Local\ThermoBook", out bool createdNew);
        using var openPanel = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\ThermoBook.AbrirPainel");
        if (!createdNew)
        {
            // Já está na bandeja: pede para ela abrir o painel (ex.: atalho do Menu Iniciar clicado de novo).
            AllowSetForegroundWindow(-1); // ASFW_ANY: deixa a outra instância trazer a janela para a frente
            openPanel.Set();
            return 0;
        }

        ApplicationConfiguration.Initialize();
        Application.Run(new TrayApp(new SettingsStore(SettingsStore.DefaultPath), openPanel, showPanel: !args.Contains("--bandeja")));
        return 0;
    }

    /// <summary>Imprime uma leitura dos sensores e do plano de energia, para conferir sem abrir a bandeja.</summary>
    private static int Diagnose()
    {
        AttachConsole(-1); // WinExe não tem console próprio: usa o do terminal que chamou
        var s = SensorService.Read();
        uint turbo = PowerService.GetTurbo(), limit = PowerService.GetLimit();
        Console.WriteLine($"""

            ThermoBook {Application.ProductVersion} - diagnostico
            Temperaturas: {string.Join(" / ", s.Temps)} C (max {s.TempMax})
            CPU: {s.CpuUse:N0}% | {s.CpuGHz:N2} GHz | {s.CpuPerf:N0}% da base{(s.InTurbo ? " (turbo)" : "")}
            GPU: {s.Gpu:N0}% | 3D {s.Gpu3D:N0}% | video {s.GpuVideo:N0}% | mem {s.GpuMem / 1e9:N2} GB
            RAM: {s.RamUsed / 1e9:N1} / {s.RamTotal / 1e9:N1} GB ({s.RamPercent:N0}%)
            Disco: {s.DiskActive:N0}% | ler {s.DiskRead / 1e6:N1} MB/s | gravar {s.DiskWrite / 1e6:N1} MB/s | C: livre {s.CFree / 1e9:N0} GB
            Energia: turbo {turbo} ({PowerService.TurboNames.GetValueOrDefault(turbo, "?")}), limite {limit}% -> perfil {PowerService.Current()?.Name ?? "personalizado"}
            """);
        return 0;
    }

    /// <summary>Volta ao perfil Normal (padrão de fábrica). Usado pelo desinstalador.</summary>
    private static int Restore()
    {
        try
        {
            PowerService.Apply(PowerService.Profiles.First(p => p.Name == "Normal"));
            return 0;
        }
        catch { return 1; } // sem janela de erro no meio da desinstalação
    }

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool AttachConsole(int processId);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool AllowSetForegroundWindow(int processId);
}
