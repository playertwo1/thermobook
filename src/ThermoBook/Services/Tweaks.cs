using System.Globalization;
using System.Management;
using System.Runtime.InteropServices;
using System.Security;
using System.Text.Json;
using Microsoft.Win32;
using Windows.Management.Deployment;

namespace ThermoBook.Services;

/// <summary>Um ajuste de registro (DWORD). <paramref name="Machine"/> = HKLM, só no processo elevado; senão HKCU, sem administrador.</summary>
internal sealed record Tweak(string Group, string Label, bool Machine, string Path, string Name, int Value)
{
    /// <summary>Chave do backup: caminho + nome do valor.</summary>
    internal string Key => $@"{Path}\{Name}";
}

/// <summary>App pré-instalado encontrado na conta do usuário.</summary>
internal sealed record BloatApp(string Name, string DisplayName, string FullName);

/// <summary>
/// Otimização do Windows (fase 11.3), porta do Otimizar_e_Debloat_Windows11.ps1.
/// Todo ajuste de registro guarda o valor anterior antes de gravar e tem Desfazer.
/// </summary>
internal static class Tweaks
{
    internal const string Privacy = "Privacidade e IA", Performance = "Desempenho";
    private const string Cv = @"Software\Microsoft\Windows\CurrentVersion\";

    internal static readonly Tweak[] All =
    [
        new(Privacy, "Pesquisa do Iniciar sem resultados do Bing", false, Cv + "Search", "BingSearchEnabled", 0),
        new(Privacy, "Esconder o botão do Copilot", false, Cv + @"Explorer\Advanced", "ShowCopilotButton", 0),
        new(Privacy, "Esconder Widgets da barra de tarefas", false, Cv + @"Explorer\Advanced", "TaskbarDa", 0),
        new(Privacy, "Desligar o ID de anúncios", false, Cv + "AdvertisingInfo", "Enabled", 0),
        new(Privacy, "Desligar experiências personalizadas com dados de diagnóstico", false, Cv + "Privacy", "TailoredExperiencesWithDiagnosticDataEnabled", 0),
        new(Performance, "Desligar a transparência", false, Cv + @"Themes\Personalize", "EnableTransparency", 0),
        new(Performance, "Bloquear apps em segundo plano", false, Cv + "BackgroundAccessApplications", "GlobalUserDisabled", 1),
        // O valor de HKCU que o .ps1 grava é ignorado pelo Windows; o que vale é a política de máquina.
        new(Performance, "Não compartilhar atualizações com outros PCs (Delivery Optimization, administrador)", true,
            @"SOFTWARE\Policies\Microsoft\Windows\DeliveryOptimization", "DODownloadMode", 0),
    ];

    /// <summary>Apps que o .ps1 remove. Na tela todos vêm desmarcados (não tem volta pelo ThermoBook).</summary>
    internal static readonly string[] BloatNames =
    [
        "Microsoft.Windows.Ai.Copilot.Provider", "Microsoft.GetHelp", "Microsoft.PowerAutomateDesktop", "Microsoft.Todos",
        "Microsoft.YourPhone", "MicrosoftCorporationII.QuickAssist", "Microsoft.Windows.PeopleExperienceHost",
        "microsoft.windowscommunicationsapps", "Microsoft.ZuneMusic", "Microsoft.Xbox.TCUI", "Microsoft.XboxGameCallableUI",
        "Microsoft.XboxGameOverlay", "Microsoft.XboxGamingOverlay", "Microsoft.XboxIdentityProvider", "Microsoft.XboxSpeechToTextOverlay",
    ];

    /// <summary>
    /// Grava o ajuste em <paramref name="root"/>. O valor anterior (null = não existia) vai para <paramref name="backup"/>
    /// e <paramref name="save"/> é chamado ANTES de gravar. Aplicar de novo não sobrescreve o backup. Falha devolve false.
    /// </summary>
    internal static bool Apply(RegistryKey root, Tweak t, Dictionary<string, int?> backup, Action save)
    {
        try
        {
            using var key = root.CreateSubKey(t.Path, writable: true);
            object? current = key.GetValue(t.Name);
            if (current is not (null or int)) return false; // tipo inesperado: não mexe para não perder o valor
            bool fresh = backup.TryAdd(t.Key, (int?)current);
            if (fresh) save();
            try { key.SetValue(t.Name, t.Value, RegistryValueKind.DWord); }
            catch
            {
                if (fresh) { backup.Remove(t.Key); save(); }
                throw;
            }
            return true;
        }
        catch (Exception e) when (e is UnauthorizedAccessException or SecurityException or IOException) { return false; }
    }

    /// <summary>Volta cada ajuste de <paramref name="tweaks"/> que está no backup ao valor anterior. Devolve quantos voltaram.</summary>
    internal static int Undo(RegistryKey root, IEnumerable<Tweak> tweaks, Dictionary<string, int?> backup, Action save)
    {
        int undone = 0;
        foreach (var t in tweaks)
        {
            if (!backup.TryGetValue(t.Key, out var previous)) continue;
            try
            {
                using var key = root.CreateSubKey(t.Path, writable: true);
                if (previous is int v) key.SetValue(t.Name, v, RegistryValueKind.DWord);
                else key.DeleteValue(t.Name, throwOnMissingValue: false);
                backup.Remove(t.Key);
                save();
                undone++;
            }
            catch (Exception e) when (e is UnauthorizedAccessException or SecurityException or IOException) { }
        }
        return undone;
    }

    // Backup dos ajustes de máquina: fica em HKLM (só administrador grava), não no settings.json,
    // porque o processo elevado pode ser de outro usuário (UAC com senha de administrador).
    private const string MachineStore = @"SOFTWARE\ThermoBook", MachineValue = "Desfazer";

    internal static Dictionary<string, int?> LoadMachineBackup(RegistryKey root)
    {
        try
        {
            using var key = root.OpenSubKey(MachineStore);
            return key?.GetValue(MachineValue) is string json ? JsonSerializer.Deserialize<Dictionary<string, int?>>(json) ?? [] : [];
        }
        catch (Exception e) when (e is JsonException or SecurityException or IOException) { return []; }
    }

    private static void SaveMachineBackup(RegistryKey root, Dictionary<string, int?> backup)
    {
        using var key = root.CreateSubKey(MachineStore, writable: true);
        if (backup.Count == 0) key.DeleteValue(MachineValue, throwOnMissingValue: false);
        else key.SetValue(MachineValue, JsonSerializer.Serialize(backup), RegistryValueKind.String);
    }

    internal static bool ApplyMachine(RegistryKey root)
    {
        var backup = LoadMachineBackup(root);
        bool ok = true;
        foreach (var t in All.Where(t => t.Machine)) ok &= Apply(root, t, backup, () => SaveMachineBackup(root, backup));
        return ok;
    }

    internal static int UndoMachine(RegistryKey root)
    {
        var backup = LoadMachineBackup(root);
        return Undo(root, All.Where(t => t.Machine), backup, () => SaveMachineBackup(root, backup));
    }

    /// <summary>Tarefa elevada: ajustes de máquina e/ou TRIM.</summary>
    internal static TaskReport RunMachine(bool delivery, bool trim)
    {
        var parts = new List<string>();
        if (delivery)
            parts.Add(ApplyMachine(Registry.LocalMachine) ? "Delivery Optimization desligado." : "Não consegui desligar o Delivery Optimization.");
        if (trim) parts.Add(Trim());
        return new(TaskOutcome.Ok, string.Join(" ", parts));
    }

    /// <summary>Tarefa elevada: desfaz os ajustes de máquina.</summary>
    internal static TaskReport RunUndoMachine() =>
        new(TaskOutcome.Ok, UndoMachine(Registry.LocalMachine) > 0 ? "Ajustes de máquina desfeitos." : "Nada de máquina para desfazer.");

    /// <summary>Re-TRIM em cada volume fixo com letra (o Optimize-Volume -ReTrim do .ps1, via WMI). Só no processo elevado.</summary>
    internal static string Trim()
    {
        var results = new List<string>();
        try
        {
            using var search = new ManagementObjectSearcher(@"root\Microsoft\Windows\Storage", "SELECT * FROM MSFT_Volume WHERE DriveType = 3");
            foreach (ManagementObject volume in search.Get())
                using (volume)
                {
                    if (volume["DriveLetter"] is not char letter || letter == '\0') continue;
                    using var input = volume.GetMethodParameters("Optimize");
                    input["ReTrim"] = true;
                    using var output = volume.InvokeMethod("Optimize", input, null);
                    results.Add(Convert.ToUInt32(output["ReturnValue"], CultureInfo.InvariantCulture) == 0 ? $"{letter}: ok" : $"{letter}: não suportado");
                }
        }
        catch (Exception e) when (e is ManagementException or COMException or UnauthorizedAccessException)
        {
            return $"TRIM falhou: {e.Message}";
        }
        return results.Count == 0 ? "TRIM: nenhuma unidade." : $"TRIM: {string.Join(", ", results)}.";
    }

    /// <summary>Status do Defender, só leitura (fora da thread da interface: é WMI).</summary>
    internal static string DefenderStatus()
    {
        try
        {
            using var search = new ManagementObjectSearcher(@"root\Microsoft\Windows\Defender",
                "SELECT AntivirusEnabled, RealTimeProtectionEnabled, AntivirusSignatureLastUpdated FROM MSFT_MpComputerStatus");
            foreach (ManagementObject s in search.Get())
                using (s)
                {
                    DateTime? updated = s["AntivirusSignatureLastUpdated"] is string dmtf ? ManagementDateTimeConverter.ToDateTime(dmtf) : null;
                    return DescribeDefender(s["AntivirusEnabled"] is true, s["RealTimeProtectionEnabled"] is true, updated);
                }
        }
        catch (Exception e) when (e is ManagementException or COMException or UnauthorizedAccessException) { }
        return "Defender: não consegui ler o status (outro antivírus instalado?).";
    }

    internal static string DescribeDefender(bool antivirus, bool realTime, DateTime? updated)
    {
        string when = updated is { } d ? $" Assinaturas de {d.ToString("d", CultureInfo.GetCultureInfo("pt-BR"))}." : "";
        return antivirus && realTime ? $"Defender ativo, proteção em tempo real ligada.{when}"
            : antivirus ? $"Defender ativo, mas a proteção em tempo real está DESLIGADA.{when}"
            : "Defender desligado.";
    }

    /// <summary>Apps da lista instalados na conta atual (sem administrador). Fora da thread da interface.</summary>
    internal static IReadOnlyList<BloatApp> InstalledBloat()
    {
        var wanted = BloatNames.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var list = new List<BloatApp>();
        foreach (var p in new PackageManager().FindPackagesForUser(string.Empty))
        {
            // App do sistema o Windows não deixa remover por usuário: nem oferece.
            if (!wanted.Contains(p.Id.Name) || p.SignatureKind == Windows.ApplicationModel.PackageSignatureKind.System) continue;
            string display;
            try { display = p.DisplayName; } catch (Exception) { display = ""; } // pacote sem nome legível
            list.Add(new(p.Id.Name, string.IsNullOrWhiteSpace(display) ? p.Id.Name : display, p.Id.FullName));
        }
        return list;
    }

    /// <summary>Remove um app da conta atual. Lança se o Windows recusar (app do sistema, em uso).</summary>
    internal static async Task RemoveApp(string fullName) => await new PackageManager().RemovePackageAsync(fullName);
}
