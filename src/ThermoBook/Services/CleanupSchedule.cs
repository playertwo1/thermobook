using System.Globalization;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace ThermoBook.Services;

internal enum ScheduleFrequency { Weekly, Monthly }

/// <summary>Quando a limpeza agendada roda. <paramref name="Day"/>: dia da semana (0 = domingo) se semanal; dia do mês (1–28) se mensal.</summary>
internal sealed record CleanupSchedule(ScheduleFrequency Frequency, int Day, int Hour, int Minute)
{
    /// <summary>Proposta A3: domingo às 12 h.</summary>
    public static CleanupSchedule Default => new(ScheduleFrequency.Weekly, 0, 12, 0);

    private static readonly Regex Token = new(@"^(semanal|mensal):(\d{1,2}):(\d{1,2}):(\d{1,2})$", RegexOptions.CultureInvariant);

    /// <summary>Forma que viaja no nome da tarefa elevada, ex.: <c>semanal:0:12:00</c>. Gramática fechada: nada além disto é aceito.</summary>
    public string Format() =>
        string.Create(CultureInfo.InvariantCulture, $"{(Frequency == ScheduleFrequency.Weekly ? "semanal" : "mensal")}:{Day}:{Hour}:{Minute:00}");

    public static bool TryParse(string text, out CleanupSchedule schedule)
    {
        schedule = Default;
        var m = Token.Match(text);
        if (!m.Success) return false;
        var f = m.Groups[1].Value == "semanal" ? ScheduleFrequency.Weekly : ScheduleFrequency.Monthly;
        int day = int.Parse(m.Groups[2].Value), hour = int.Parse(m.Groups[3].Value), minute = int.Parse(m.Groups[4].Value);
        bool dayOk = f == ScheduleFrequency.Weekly ? day <= 6 : day is >= 1 and <= 28;
        if (!dayOk || hour > 23 || minute > 59) return false;
        schedule = new(f, day, hour, minute);
        return true;
    }

    public string Describe()
    {
        string at = $"às {Hour}:{Minute:00}";
        if (Frequency == ScheduleFrequency.Monthly) return $"todo dia {Day} do mês {at}";
        var pt = CultureInfo.GetCultureInfo("pt-BR");
        return $"toda semana, {pt.DateTimeFormat.GetDayName((DayOfWeek)Day)} {at}";
    }
}

/// <summary>Tudo o que o Agendador precisa para a tarefa; é dado puro para poder ser conferido em teste.</summary>
internal sealed record TaskDef(string Folder, string Name, string Description, string Exe, string Arguments, string UserId,
    CleanupSchedule Schedule, bool Highest, bool OnlyOnAc, bool StartWhenAvailable, string TimeLimit);

internal interface ISchedulerBackend
{
    void Register(TaskDef def);

    /// <returns>true se a tarefa existia e foi removida.</returns>
    bool Delete(string folder, string name);
}

/// <summary>Resultado da última limpeza agendada, em arquivo próprio (não disputa o settings.json com o app aberto).</summary>
internal sealed record CleanupLog(DateTime When, TaskOutcome Outcome, string Message);

/// <summary>
/// Limpeza agendada (fase 14): tarefa do Agendador de Tarefas que roda <c>ThermoBook.exe --limpeza-agendada</c> com privilégios mais altos.
/// A tarefa aponta para uma <b>cópia</b> do app em %ProgramFiles%\ThermoBook (só administrador grava): se apontasse para a pasta do usuário,
/// quem trocasse o .exe ali ganharia administrador sem UAC.
/// </summary>
internal static class ScheduledCleanup
{
    internal const string Folder = @"\ThermoBook", Name = "Limpeza", Flag = "--limpeza-agendada";

    internal static string InstalledDir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "ThermoBook");

    internal static string LogPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ThermoBook", "limpeza.json");

    /// <summary>Só na tomada, roda depois se o PC estava desligado no horário, e para sozinha se passar de 1 h.</summary>
    internal static TaskDef Definition(CleanupSchedule schedule, string exe, string userId) =>
        new(Folder, Name, "ThermoBook: limpeza de temporários (sem esvaziar a Lixeira).", exe, Flag, userId, schedule,
            Highest: true, OnlyOnAc: true, StartWhenAvailable: true, TimeLimit: "PT1H");

    // ---- lado elevado (tarefas limpeza-agendar:<horário> e limpeza-desagendar) ----

    internal static TaskReport Schedule(CleanupSchedule schedule, ISchedulerBackend backend, Func<string> installCopy, string userId)
    {
        string exe = installCopy();
        backend.Register(Definition(schedule, exe, userId));
        return new(TaskOutcome.Ok, $"Limpeza agendada: {schedule.Describe()}.");
    }

    internal static TaskReport Unschedule(ISchedulerBackend backend, Action removeCopy)
    {
        bool existed = backend.Delete(Folder, Name);
        string note = "";
        try { removeCopy(); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { note = " A cópia do app não pôde ser apagada (em uso); sai na próxima vez."; }
        return new(TaskOutcome.Ok, (existed ? "Limpeza agendada desligada." : "Não havia limpeza agendada.") + note);
    }

    internal static TaskReport Schedule(CleanupSchedule schedule) =>
        Schedule(schedule, new ComScheduler(), CopyApp, WindowsIdentity.GetCurrent().Name);

    internal static TaskReport Unschedule() => Unschedule(new ComScheduler(), RemoveCopy);

    /// <summary>Copia o app que está rodando (elevado) para %ProgramFiles%\ThermoBook e devolve o caminho da cópia.</summary>
    private static string CopyApp()
    {
        Directory.CreateDirectory(InstalledDir);
        string target = Path.Combine(InstalledDir, "ThermoBook.exe");
        File.Copy(Environment.ProcessPath!, target, overwrite: true);
        return target;
    }

    private static void RemoveCopy()
    {
        string target = Path.Combine(InstalledDir, "ThermoBook.exe");
        if (File.Exists(target)) File.Delete(target);
        if (Directory.Exists(InstalledDir) && !Directory.EnumerateFileSystemEntries(InstalledDir).Any()) Directory.Delete(InstalledDir);
    }

    // ---- lado da tarefa agendada (--limpeza-agendada) ----

    /// <summary>Roda a limpeza (sem a Lixeira) e grava o resultado. Não lança.</summary>
    internal static int RunScheduled(string logPath, Func<TaskReport> run, DateTime now)
    {
        TaskReport report;
        try { report = run(); }
        catch (Exception ex) { report = new(TaskOutcome.Failed, ex.Message); }
        try { WriteLog(logPath, new(now, report.Outcome, report.Message)); }
        catch (Exception) { return 3; }
        return report.Outcome == TaskOutcome.Ok ? 0 : 1;
    }

    internal static int RunScheduled() => RunScheduled(LogPath, () => Cleanup.Run(emptyRecycleBin: false), DateTime.Now);

    internal static void WriteLog(string path, CleanupLog log)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(log));
    }

    /// <summary>Último resultado, ou null se não houve limpeza agendada ou o arquivo está ilegível.</summary>
    internal static CleanupLog? ReadLog(string path)
    {
        try { return File.Exists(path) ? JsonSerializer.Deserialize<CleanupLog>(File.ReadAllText(path)) : null; }
        catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException) { return null; }
    }
}

/// <summary>Agendador de Tarefas pela API COM (Schedule.Service), com ligação tardia: sem dependência nova e sem schtasks.exe.</summary>
internal sealed class ComScheduler : ISchedulerBackend
{
    private const int TriggerWeekly = 3, TriggerMonthly = 4, ActionExec = 0, CreateOrUpdate = 6, LogonInteractiveToken = 3, RunLevelHighest = 1;
    private const int FileNotFound = unchecked((int)0x80070002), PathNotFound = unchecked((int)0x80070003);

    private static dynamic Connect()
    {
        dynamic service = Activator.CreateInstance(Type.GetTypeFromProgID("Schedule.Service", throwOnError: true)!)!;
        service.Connect();
        return service;
    }

    public void Register(TaskDef d)
    {
        dynamic service = Connect();
        dynamic root = service.GetFolder("\\");
        dynamic folder;
        try { folder = root.GetFolder(d.Folder); }
        catch (Exception e) when (e.HResult is FileNotFound or PathNotFound) { folder = root.CreateFolder(d.Folder); } // o .NET traduz o HRESULT em FileNotFoundException etc.

        dynamic task = service.NewTask(0);
        task.RegistrationInfo.Description = d.Description;
        task.Principal.UserId = d.UserId;
        task.Principal.LogonType = LogonInteractiveToken; // só com o usuário conectado: não guarda senha
        task.Principal.RunLevel = d.Highest ? RunLevelHighest : 0;
        task.Settings.Enabled = true;
        task.Settings.DisallowStartIfOnBatteries = d.OnlyOnAc;
        task.Settings.StopIfGoingOnBatteries = d.OnlyOnAc;
        task.Settings.StartWhenAvailable = d.StartWhenAvailable;
        task.Settings.ExecutionTimeLimit = d.TimeLimit;

        var s = d.Schedule;
        dynamic trigger = task.Triggers.Create(s.Frequency == ScheduleFrequency.Weekly ? TriggerWeekly : TriggerMonthly);
        trigger.StartBoundary = DateTime.Today.ToString($"yyyy-MM-dd'T'{s.Hour:00}:{s.Minute:00}:00", System.Globalization.CultureInfo.InvariantCulture);
        trigger.Enabled = true;
        if (s.Frequency == ScheduleFrequency.Weekly)
        {
            trigger.DaysOfWeek = 1 << s.Day; // domingo = 1, segunda = 2, ...
            trigger.WeeksInterval = 1;
        }
        else
        {
            trigger.DaysOfMonth = 1 << (s.Day - 1);
            trigger.MonthsOfYear = 0xFFF;
        }

        dynamic action = task.Actions.Create(ActionExec);
        action.Path = d.Exe;
        action.Arguments = d.Arguments;

        folder.RegisterTaskDefinition(d.Name, task, CreateOrUpdate, null, null, LogonInteractiveToken, null);
    }

    public bool Delete(string folderPath, string name)
    {
        dynamic service = Connect();
        dynamic root = service.GetFolder("\\");
        dynamic folder;
        try { folder = root.GetFolder(folderPath); }
        catch (Exception e) when (e.HResult is FileNotFound or PathNotFound) { return false; }
        bool existed = true;
        try { folder.DeleteTask(name, 0); }
        catch (Exception e) when (e.HResult is FileNotFound or PathNotFound) { existed = false; }
        try { root.DeleteFolder(folderPath.TrimStart('\\'), 0); } // só apaga se estiver vazia; senão, deixa
        catch (Exception e) when (e is COMException or IOException) { }
        return existed;
    }
}
