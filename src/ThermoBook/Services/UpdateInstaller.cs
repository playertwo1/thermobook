using System.Diagnostics;
using System.Net.Http.Headers;
using System.Security.Cryptography;

namespace ThermoBook.Services;

/// <param name="Path">Instalador baixado e conferido; null se falhou.</param>
/// <param name="Error">Texto para o usuário quando <paramref name="Path"/> é null.</param>
internal sealed record DownloadResult(string? Path, string? Error);

/// <summary>
/// Atualização automática, parte 2 (fase 17): baixa o instalador, confere o SHA-256 que a API da Release informa e o roda em modo silencioso.
/// Sem SHA-256 na Release ou com hash diferente, nada é executado. O instalador é por usuário (sem UAC) e não é assinado:
/// a confiança vem do HTTPS do GitHub e desse hash (que vem da mesma API).
/// </summary>
internal static class UpdateInstaller
{
    internal const long MaxBytes = 400L << 20; // o instalador tem ~40 MB; muito acima disso não é ele

    internal static string DefaultDir => Path.Combine(Path.GetTempPath(), "ThermoBook-Update");

    internal static async Task<DownloadResult> DownloadAsync(UpdateInfo info, string dir, IProgress<int>? progress = null,
        HttpMessageHandler? handler = null, CancellationToken cancel = default)
    {
        if (info.Sha256 is null) return new(null, "A Release não informa a impressão digital (SHA-256) do instalador; por segurança não instalo sem conferir.");
        if (!info.DownloadUrl.StartsWith($"https://github.com/{UpdateService.Repo}/releases/download/", StringComparison.Ordinal))
            return new(null, "Endereço de download inesperado.");
        string name = Path.GetFileName(info.FileName);
        if (name.Length == 0 || name != info.FileName) return new(null, "Nome de instalador inesperado.");

        string final = Path.Combine(dir, name), part = final + ".part";
        try
        {
            Directory.CreateDirectory(dir);
            using var http = handler is null ? new HttpClient() : new HttpClient(handler, disposeHandler: false);
            http.Timeout = TimeSpan.FromMinutes(10);
            http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("ThermoBook", "1"));
            using var response = await http.GetAsync(info.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, cancel);
            if (!response.IsSuccessStatusCode) return new(null, $"O download falhou (HTTP {(int)response.StatusCode}).");
            long? length = response.Content.Headers.ContentLength;
            if (length > MaxBytes || (info.Size > 0 && length is { } l && l != info.Size)) return new(null, "O tamanho do instalador não bate com o da Release.");

            string hash;
            long total = 0;
            using (var sha = SHA256.Create())
            {
                await using var source = await response.Content.ReadAsStreamAsync(cancel);
                await using var file = new FileStream(part, FileMode.Create, FileAccess.Write, FileShare.None);
                var buffer = new byte[81920];
                int read, lastPercent = -1;
                while ((read = await source.ReadAsync(buffer, cancel)) > 0)
                {
                    total += read;
                    if (total > MaxBytes) return Fail(part, "O instalador é maior do que o esperado.");
                    sha.TransformBlock(buffer, 0, read, null, 0);
                    await file.WriteAsync(buffer.AsMemory(0, read), cancel);
                    int percent = info.Size > 0 ? (int)Math.Min(100, total * 100 / info.Size) : 0;
                    if (percent != lastPercent) { lastPercent = percent; progress?.Report(percent); }
                }
                sha.TransformFinalBlock([], 0, 0);
                hash = Convert.ToHexStringLower(sha.Hash!);
            }
            if (info.Size > 0 && total != info.Size) return Fail(part, "O download veio incompleto.");
            if (hash != info.Sha256) return Fail(part, "O instalador baixado não confere com o SHA-256 da Release; não vou executá-lo.");

            File.Move(part, final, overwrite: true);
            return new(final, null);
        }
        catch (Exception e) when (e is HttpRequestException or IOException or UnauthorizedAccessException or TaskCanceledException or OperationCanceledException)
        {
            try { File.Delete(part); } catch (Exception) { }
            return new(null, $"Não consegui baixar a atualização: {e.Message}");
        }
    }

    private static DownloadResult Fail(string part, string error)
    {
        try { File.Delete(part); } catch (Exception) { }
        return new(null, error);
    }

    /// <summary>Argumentos do Inno Setup: sem janelas, sem reiniciar o Windows e fechando o app se ainda estiver aberto.</summary>
    internal static readonly string[] SilentArgs = ["/VERYSILENT", "/SUPPRESSMSGBOXES", "/NORESTART", "/CLOSEAPPLICATIONS"];

    /// <summary>Roda o instalador conferido. O app deve sair logo depois; o instalador o abre de novo no fim.</summary>
    internal static bool Start(string installer, Func<ProcessStartInfo, Process?>? start = null)
    {
        var psi = new ProcessStartInfo(installer) { UseShellExecute = false };
        foreach (var a in SilentArgs) psi.ArgumentList.Add(a);
        try { return (start ?? Process.Start)(psi) is not null; }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException) { return false; }
    }
}
