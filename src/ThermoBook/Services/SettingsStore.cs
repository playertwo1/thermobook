using System.Text.Json;

namespace ThermoBook.Services;

/// <param name="Profile">Perfil reaplicado ao iniciar; null quando a última escolha foi uma combinação personalizada.</param>
/// <param name="AlertCelsius">Avisa quando a temperatura passar disto...</param>
/// <param name="AlertSeconds">...por este tempo seguido.</param>
/// <param name="AutoCool">No aviso, troca sozinho para o perfil Frio.</param>
internal sealed record Settings(string? Profile = null, double AlertCelsius = 90, int AlertSeconds = 30, bool AutoCool = false);

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
