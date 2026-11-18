using System.Runtime.InteropServices;

namespace ThermoBook.Services;

/// <summary>Detecta app em tela cheia (jogo, vídeo, apresentação) pelo estado de notificação do usuário do Windows.</summary>
internal static partial class FullscreenGuard
{
    // QUERY_USER_NOTIFICATION_STATE: 2 = BUSY, 3 = RUNNING_D3D_FULL_SCREEN, 4 = PRESENTATION_MODE
    internal static bool ShouldHide(int state) => state is 2 or 3 or 4;

    /// <summary>true se a pílula deve se esconder agora. Falha na consulta não esconde (melhor aparecer do que sumir sem motivo).</summary>
    public static bool IsFullscreenAppActive() => SHQueryUserNotificationState(out int state) == 0 && ShouldHide(state);

    [LibraryImport("shell32.dll")]
    private static partial int SHQueryUserNotificationState(out int state);
}
