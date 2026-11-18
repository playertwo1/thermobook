using System.Text.Json;

namespace ThermoBook.Services;

/// <param name="Profile">Perfil reaplicado ao iniciar; null quando a última escolha foi uma combinação personalizada.</param>
/// <param name="AlertCelsius">Avisa quando a temperatura passar disto...</param>
/// <param name="AlertSeconds">...por este tempo seguido.</param>
/// <param name="AutoCool">No aviso, troca sozinho para o perfil Frio.</param>
/// <param name="Hotkey">Atalho global para desligar a tela, ex.: "Ctrl+Alt+Shift+D". Vazio desliga o atalho.</param>
/// <param name="BackgroundPlans">Modo segundo plano ligado quando não vazio: por plano (GUID), os valores originais de tampa/suspensão/hibernação.</param>
/// <param name="PillMonitor">Monitor (nome do dispositivo + resolução) onde a pílula ficou por último.</param>
/// <param name="PillPositions">Posição da pílula por monitor.</param>
/// <param name="ShowPill">Mostra a pílula flutuante (ligada por padrão).</param>
/// <param name="TweakBackup">Valores anteriores dos ajustes de HKCU da otimização (null = não existia), para o Desfazer.</param>
internal sealed record Settings(string? Profile = null, double AlertCelsius = 90, int AlertSeconds = 30, bool AutoCool = false, string Hotkey = HotkeyService.Default,
    Dictionary<string, Dictionary<string, PairValue>>? BackgroundPlans = null,
    string? PillMonitor = null, Dictionary<string, PillPosition>? PillPositions = null,
    bool ShowPill = true, Dictionary<string, int?>? TweakBackup = null);

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
