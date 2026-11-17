using System.Runtime.InteropServices;

namespace ThermoBook.Services;

/// <summary>Desliga o monitor sem suspender o PC (equivalente ao desligar_tela.bat).</summary>
internal static partial class DisplayService
{
    internal const int DelayMs = 500; // o soltar do clique/tecla não pode religar a tela na hora
    private static readonly IntPtr HwndBroadcast = new(0xFFFF);
    private const uint WM_SYSCOMMAND = 0x0112;
    private static readonly IntPtr SC_MONITORPOWER = new(0xF170), MonitorOff = new(2);

    /// <summary>Não bloqueia a interface: espera o atraso numa tarefa e posta a mensagem.</summary>
    public static void TurnOff() => Task.Run(async () =>
    {
        await Task.Delay(DelayMs);
        // PostMessage e não SendMessage: SendMessage trava esperando janela que não responde.
        PostMessage(HwndBroadcast, WM_SYSCOMMAND, SC_MONITORPOWER, MonitorOff);
    });

    [LibraryImport("user32.dll", EntryPoint = "PostMessageW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
}
