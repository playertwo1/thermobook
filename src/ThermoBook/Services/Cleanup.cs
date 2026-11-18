using System.Globalization;
using System.Runtime.InteropServices;

namespace ThermoBook.Services;

/// <summary>
/// Limpeza de temporários (fase 11.2), porta do Manutencao_e_Limpeza_Automatica.bat.
/// Roda no processo elevado. Só apaga o <b>conteúdo</b> das pastas da lista; itens em uso são pulados em silêncio.
/// </summary>
internal static partial class Cleanup
{
    /// <summary>Pastas cujo conteúdo é apagado. Nada fora delas é tocado.</summary>
    internal static IReadOnlyList<string> DefaultRoots()
    {
        string win = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        string data = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
        return
        [
            Path.GetTempPath(),
            Path.Combine(win, "Temp"),
            Path.Combine(data, @"Microsoft\Windows\WER\ReportArchive"),
            Path.Combine(data, @"Microsoft\Windows\WER\ReportQueue"),
            Path.Combine(data, @"Microsoft\Windows\WER\Temp"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CrashDumps"),
            Path.Combine(win, @"SoftwareDistribution\Download"),
        ];
    }

    /// <summary>Apaga tudo dentro de <paramref name="root"/> (a pasta em si fica). Devolve quantos itens foram apagados.</summary>
    internal static int DeleteContents(string root)
    {
        // Trava de segurança, ANTES de resolver o caminho: "C:" ou "pasta" são relativos à pasta atual e apagariam o que não devem.
        if (!Path.IsPathFullyQualified(root)) return 0;
        root = Path.GetFullPath(root);
        // Nunca limpar a raiz de um disco nem uma pasta sem pai.
        if (Path.GetDirectoryName(root.TrimEnd('\\', '/')) is null || !Directory.Exists(root)) return 0;
        return DeleteChildren(new DirectoryInfo(root));
    }

    private static int DeleteChildren(DirectoryInfo dir)
    {
        int deleted = 0;
        IEnumerable<FileSystemInfo> items;
        try { items = dir.EnumerateFileSystemInfos().ToList(); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return 0; }

        foreach (var item in items)
        {
            try
            {
                if (item is DirectoryInfo sub && !sub.Attributes.HasFlag(FileAttributes.ReparsePoint))
                {
                    deleted += DeleteChildren(sub); // desce e apaga o que der
                    sub.Delete(); // só funciona se ficou vazia; senão é pulada
                }
                else
                {
                    // Atalho/junction: apaga só o link, nunca segue para o destino.
                    if (item.Attributes.HasFlag(FileAttributes.ReadOnly)) item.Attributes = FileAttributes.Normal;
                    item.Delete();
                }
                deleted++;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { } // em uso ou sem permissão: pula
        }
        return deleted;
    }

    /// <summary>Texto do balão a partir do espaço livre medido antes e depois.</summary>
    internal static string Describe(long freedBytes)
    {
        if (freedBytes <= 0) return "Já estava limpo.";
        var pt = CultureInfo.GetCultureInfo("pt-BR");
        return freedBytes >= 1L << 30
            ? string.Create(pt, $"{freedBytes / (double)(1L << 30):0.0} GB liberados")
            : string.Create(pt, $"{freedBytes / (double)(1L << 20):0} MB liberados");
    }

    /// <summary>Tarefa completa: mede o C: antes e depois, apaga as pastas, limpa o cache DNS e (opcional) a Lixeira.</summary>
    internal static TaskReport Run(bool emptyRecycleBin)
    {
        var drive = new DriveInfo(Path.GetPathRoot(Environment.SystemDirectory)!);
        long before = drive.AvailableFreeSpace;
        foreach (var root in DefaultRoots()) DeleteContents(root);
        if (emptyRecycleBin) SHEmptyRecycleBin(IntPtr.Zero, null, 0x1 | 0x2 | 0x4); // sem confirmação, sem progresso, sem som
        DnsFlushResolverCache();
        return new(TaskOutcome.Ok, Describe(drive.AvailableFreeSpace - before));
    }

    [LibraryImport("shell32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int SHEmptyRecycleBin(IntPtr hwnd, string? rootPath, uint flags);

    [LibraryImport("dnsapi.dll", EntryPoint = "DnsFlushResolverCache")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DnsFlushResolverCache();
}
