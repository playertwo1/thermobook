using Microsoft.Win32;

namespace ThermoBook.Services;

/// <summary>
/// O que o desinstalador desfaz (<c>ThermoBook.exe --restaurar</c>): segundo plano, ajustes da otimização e o modo.
/// Cada passo é independente: um que falha não impede os outros. Sem janela de erro no meio da desinstalação.
/// </summary>
internal static class Uninstall
{
    /// <returns>0 se tudo deu certo, 1 se algum passo falhou ou o UAC foi recusado.</returns>
    internal static int Run(SettingsStore store, RegistryKey userRoot, RegistryKey machineRoot, BackgroundMode.WriteFn writePlan,
        Action applyDefaultMode, Func<string, TaskReport> elevated)
    {
        bool ok = true;
        void Step(Action a) { try { a(); } catch (Exception) { ok = false; } }
        var s = store.Load();

        // Primeiro o segundo plano: a tampa volta a suspender como antes, em todos os planos que ele tocou.
        if (BackgroundMode.IsOn(s)) Step(() => s = BackgroundMode.Disable(s, writePlan, store.Save));

        // Ajustes da conta (HKCU): sem administrador.
        if (s.TweakBackup is { Count: > 0 } backup)
            Step(() =>
            {
                Tweaks.Undo(userRoot, Tweaks.All.Where(t => !t.Machine), backup, () => store.Save(s with { TweakBackup = new(backup) }));
                if (backup.Count > 0) ok = false; // algum não voltou
            });

        Step(applyDefaultMode);

        // Ajustes de máquina só existem se o usuário os aplicou com UAC; desfazer pede o UAC de novo. Recusar deixa como está.
        // A limpeza agendada também precisa do UAC (tarefa e cópia em %ProgramFiles%): com os dois, um só pedido.
        bool machine = Tweaks.LoadMachineBackup(machineRoot).Count > 0, scheduled = s.CleanupAuto is not null;
        string? task = (machine, scheduled) switch
        {
            (true, true) => "desinstalar-tudo",
            (true, false) => "otimizacao-desfazer",
            (false, true) => "limpeza-desagendar",
            _ => null,
        };
        if (task is not null) Step(() => { if (elevated(task).Outcome != TaskOutcome.Ok) ok = false; });

        return ok ? 0 : 1;
    }
}
