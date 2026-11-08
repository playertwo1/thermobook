using System.Text.Json;

namespace ThermoBook.Services;

internal sealed record Settings(string? Profile = null);

/// <summary>Guarda as configurações em %AppData%\ThermoBook\settings.json.</summary>
internal sealed class SettingsStore(string path)
{
    public static string DefaultPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ThermoBook", "settings.json");

    public Settings Load()
    {
        try
        {
            return File.Exists(path) ? JsonSerializer.Deserialize<Settings>(File.ReadAllText(path)) ?? new() : new();
        }
        catch (JsonException)
        {
            return new(); // arquivo corrompido: volta ao padrão em vez de travar a inicialização
        }
    }

    public void Save(Settings settings)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true }));
    }
}
