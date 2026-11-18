using System.Globalization;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace ThermoBook.Services;

/// <summary>Versão no formato das tags (<c>v0.2.0</c>, <c>0.2.0-beta.1</c>). Pré-lançamento é menor que a versão final, como no SemVer.</summary>
internal sealed record AppVersion(int Major, int Minor, int Patch, string[] Pre) : IComparable<AppVersion>
{
    private static readonly Regex Format = new(@"^v?(\d+)\.(\d+)\.(\d+)(?:-([0-9A-Za-z.-]+))?(?:\+.*)?$", RegexOptions.CultureInvariant);

    public bool IsPrerelease => Pre.Length > 0;

    /// <summary>Aceita o "+commit" que o .NET põe na versão do app (<c>0.1.0+dd49748…</c>); ignora-o.</summary>
    public static AppVersion? Parse(string? text)
    {
        if (text is null || Format.Match(text.Trim()) is not { Success: true } m) return null;
        return new(int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture), int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture),
            int.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture), m.Groups[4].Success ? m.Groups[4].Value.Split('.') : []);
    }

    public int CompareTo(AppVersion? other)
    {
        if (other is null) return 1;
        int c = Major.CompareTo(other.Major);
        if (c == 0) c = Minor.CompareTo(other.Minor);
        if (c == 0) c = Patch.CompareTo(other.Patch);
        if (c != 0) return c;
        if (Pre.Length == 0 || other.Pre.Length == 0) return other.Pre.Length.CompareTo(Pre.Length); // sem sufixo vale mais
        for (int i = 0; i < Math.Min(Pre.Length, other.Pre.Length); i++)
        {
            bool an = int.TryParse(Pre[i], out int x), bn = int.TryParse(other.Pre[i], out int y);
            c = an && bn ? x.CompareTo(y) : an ? -1 : bn ? 1 : string.CompareOrdinal(Pre[i], other.Pre[i]); // número < texto
            if (c != 0) return c;
        }
        return Pre.Length.CompareTo(other.Pre.Length);
    }

    public bool Equals(AppVersion? other) => other is not null && CompareTo(other) == 0;

    public override int GetHashCode() => HashCode.Combine(Major, Minor, Patch, string.Join('.', Pre));

    public override string ToString() => $"{Major}.{Minor}.{Patch}{(Pre.Length > 0 ? "-" + string.Join('.', Pre) : "")}";

    public static bool operator >(AppVersion a, AppVersion b) => a.CompareTo(b) > 0;
    public static bool operator <(AppVersion a, AppVersion b) => a.CompareTo(b) < 0;
}

/// <summary>Release mais nova com instalador. <paramref name="Sha256"/> (hex minúsculo) vem do <c>digest</c> da API; null = a API não deu.</summary>
internal sealed record UpdateInfo(AppVersion Version, string Tag, string DownloadUrl, string FileName, long Size, string? Sha256, string PageUrl);

/// <summary>
/// Atualização automática, parte 1 (fase 17): pergunta à API de Releases do GitHub se há versão mais nova. Não baixa nem instala nada.
/// Nenhum dado do usuário vai junto (User-Agent genérico). Sem rede ou resposta estranha: devolve null, em silêncio.
/// </summary>
internal static class UpdateService
{
    internal const string Repo = "playertwo1/thermobook";
    internal const string ReleasesUrl = $"https://api.github.com/repos/{Repo}/releases?per_page=10";
    private const string DownloadPrefix = $"https://github.com/{Repo}/releases/download/";

    private static readonly Regex Installer = new(@"^ThermoBook-Setup-.+\.exe$", RegexOptions.CultureInvariant);
    private static readonly Regex Digest = new(@"^sha256:([0-9a-fA-F]{64})$", RegexOptions.CultureInvariant);

    /// <summary>Hora de olhar de novo: nunca olhou, ou já passou um dia (também se o relógio foi para trás).</summary>
    internal static bool Due(DateTime? lastCheck, DateTime now) => lastCheck is not { } t || now - t >= TimeSpan.FromDays(1) || t > now;

    /// <param name="current">Versão em uso (ex.: <c>Application.ProductVersion</c>).</param>
    /// <param name="includeBeta">Considera também os pré-lançamentos.</param>
    /// <param name="handler">Só nos testes: troca a rede.</param>
    internal static async Task<UpdateInfo?> CheckAsync(string current, bool includeBeta, HttpMessageHandler? handler = null, CancellationToken cancel = default)
    {
        if (AppVersion.Parse(current) is not { } mine) return null;
        try
        {
            using var http = handler is null ? new HttpClient() : new HttpClient(handler, disposeHandler: false);
            http.Timeout = TimeSpan.FromSeconds(10);
            http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("ThermoBook", "1"));
            http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
            using var response = await http.GetAsync(ReleasesUrl, cancel);
            if (!response.IsSuccessStatusCode) return null; // inclui limite de consultas (403) e 404
            return Pick(await response.Content.ReadAsStringAsync(cancel), mine, includeBeta);
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or OperationCanceledException or JsonException or InvalidOperationException)
        {
            return null;
        }
    }

    /// <summary>Da lista de releases da API, a mais nova que <paramref name="mine"/> e que tenha instalador; null se nenhuma.</summary>
    internal static UpdateInfo? Pick(string json, AppVersion mine, bool includeBeta)
    {
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.ValueKind != JsonValueKind.Array) return null;
        UpdateInfo? best = null;
        foreach (var r in doc.RootElement.EnumerateArray())
        {
            if (r.ValueKind != JsonValueKind.Object || Flag(r, "draft") || (Flag(r, "prerelease") && !includeBeta)) continue;
            string tag = Str(r, "tag_name");
            if (AppVersion.Parse(tag) is not { } version || (version.IsPrerelease && !includeBeta)) continue;
            if (!(version > mine) || (best is not null && !(version > best.Version))) continue;
            if (Asset(r) is not { } asset) continue;
            best = new(version, tag, asset.Url, asset.Name, asset.Size, asset.Sha256, Str(r, "html_url"));
        }
        return best;
    }

    private static (string Name, string Url, long Size, string? Sha256)? Asset(JsonElement release)
    {
        if (!release.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array) return null;
        foreach (var a in assets.EnumerateArray())
        {
            string name = Str(a, "name"), url = Str(a, "browser_download_url");
            // Só instalador com o nome esperado e baixado do próprio repositório, por HTTPS.
            if (!Installer.IsMatch(name) || name.IndexOfAny(['\\', '/', ':']) >= 0 || !url.StartsWith(DownloadPrefix, StringComparison.Ordinal)) continue; // nome vira arquivo: sem caminho
            long size = a.TryGetProperty("size", out var s) && s.ValueKind == JsonValueKind.Number && s.TryGetInt64(out long n) ? n : 0;
            var m = Digest.Match(Str(a, "digest"));
            return (name, url, size, m.Success ? m.Groups[1].Value.ToLowerInvariant() : null);
        }
        return null;
    }

    private static string Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";

    private static bool Flag(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.True;
}
