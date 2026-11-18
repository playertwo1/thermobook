using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;

namespace ThermoBook.Services;

internal enum TaskOutcome { Ok, Cancelled, Failed }

/// <summary>Resultado de uma tarefa elevada, gravado em JSON pelo processo elevado e lido pelo app normal.</summary>
internal sealed record TaskReport(TaskOutcome Outcome, string Message);

/// <summary>
/// Tarefas que precisam de administrador (fase 11). O app relança ele mesmo com
/// <c>--tarefa &lt;nome&gt; --relatorio &lt;arquivo&gt;</c> via UAC; o processo elevado faz só a tarefa, grava o relatório e sai.
/// O processo elevado é uma fronteira de confiança: só aceita as tarefas da lista e nunca sobrescreve um arquivo.
/// </summary>
internal static class ElevatedTask
{
    private const int ErrorCancelled = 1223; // usuário disse "Não" no UAC

    /// <summary>Lista fechada de tarefas aceitas. A 11.2 e a 11.3 trocam os corpos.</summary>
    internal static readonly Dictionary<string, Func<TaskReport>> Tasks = new(StringComparer.Ordinal)
    {
        ["limpeza"] = () => new(TaskOutcome.Failed, "A limpeza ainda não foi implementada."),
        ["otimizacao"] = () => new(TaskOutcome.Failed, "A otimização ainda não foi implementada."),
    };

    internal sealed record Args(string Task, string ReportPath);

    /// <summary>Aceita exatamente <c>--tarefa &lt;conhecida&gt; --relatorio &lt;caminho completo .json&gt;</c>; qualquer outra coisa é null.</summary>
    internal static Args? Parse(string[] args)
    {
        if (args is not ["--tarefa", var task, "--relatorio", var report]) return null;
        if (!Tasks.ContainsKey(task)) return null;
        if (report.IndexOfAny(Path.GetInvalidPathChars()) >= 0 || !Path.IsPathFullyQualified(report)) return null;
        if (!string.Equals(Path.GetExtension(report), ".json", StringComparison.OrdinalIgnoreCase)) return null;
        return new(task, report);
    }

    /// <summary>Lado elevado: roda a tarefa e grava o relatório. Sem janela; o código de saída é só informativo.</summary>
    internal static int RunElevated(string[] args)
    {
        if (Parse(args) is not { } a) return 2;
        TaskReport report;
        try { report = Tasks[a.Task](); }
        catch (Exception ex) { report = new(TaskOutcome.Failed, ex.Message); }
        try
        {
            // CreateNew: o processo elevado nunca sobrescreve um arquivo que já existe (nem segue um link plantado ali).
            using var file = new FileStream(a.ReportPath, FileMode.CreateNew, FileAccess.Write);
            JsonSerializer.Serialize(file, report);
        }
        catch (Exception) { return 3; }
        return report.Outcome == TaskOutcome.Ok ? 0 : 1;
    }

    /// <summary>Lado normal: pede o UAC, espera o processo elevado e devolve o relatório. Não lança.</summary>
    internal static async Task<TaskReport> LaunchAsync(string task, Func<ProcessStartInfo, Process?>? start = null)
    {
        if (!Tasks.ContainsKey(task)) return new(TaskOutcome.Failed, $"Tarefa desconhecida: {task}");
        string report = Path.Combine(Path.GetTempPath(), $"ThermoBook-{Guid.NewGuid():N}.json");
        var psi = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = true, Verb = "runas" };
        foreach (var a in new[] { "--tarefa", task, "--relatorio", report }) psi.ArgumentList.Add(a);
        try
        {
            using var process = (start ?? Process.Start)(psi);
            if (process is null) return new(TaskOutcome.Failed, "Não consegui iniciar a tarefa.");
            await process.WaitForExitAsync();
            if (!File.Exists(report)) return new(TaskOutcome.Failed, "A tarefa terminou sem relatório.");
            return JsonSerializer.Deserialize<TaskReport>(await File.ReadAllTextAsync(report))
                ?? new(TaskOutcome.Failed, "Relatório vazio.");
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == ErrorCancelled)
        {
            return new(TaskOutcome.Cancelled, "Cancelado");
        }
        catch (Exception ex)
        {
            return new(TaskOutcome.Failed, $"Não consegui rodar a tarefa: {ex.Message}");
        }
        finally
        {
            try { File.Delete(report); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }
}
