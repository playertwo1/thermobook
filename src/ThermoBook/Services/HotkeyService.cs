using System.Runtime.InteropServices;

namespace ThermoBook.Services;

/// <summary>Atalho global ("Ctrl+Alt+Shift+D"): lê o texto do settings.json e registra com RegisterHotKey.</summary>
internal static partial class HotkeyService
{
    public const string Default = "Ctrl+Alt+Shift+D";
    private const uint MOD_ALT = 1, MOD_CONTROL = 2, MOD_SHIFT = 4, MOD_WIN = 8, MOD_NOREPEAT = 0x4000;

    /// <summary>Aceita Ctrl/Alt/Shift/Win + uma letra, dígito ou F1–F24. Exige ao menos um modificador.</summary>
    public static bool TryParse(string? text, out uint modifiers, out uint vk)
    {
        modifiers = 0; vk = 0;
        if (string.IsNullOrWhiteSpace(text)) return false;
        var parts = text.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2) return false;
        foreach (var mod in parts[..^1])
        {
            uint bit = mod.ToLowerInvariant() switch
            {
                "ctrl" or "control" => MOD_CONTROL, "alt" => MOD_ALT, "shift" => MOD_SHIFT, "win" => MOD_WIN, _ => 0,
            };
            if (bit == 0) return false;
            modifiers |= bit;
        }
        string key = parts[^1].ToUpperInvariant();
        if (key.Length == 1 && char.IsAsciiLetterOrDigit(key[0])) vk = key[0];
        else if (key[0] == 'F' && int.TryParse(key[1..], out int n) && n is >= 1 and <= 24) vk = (uint)(0x70 + n - 1);
        else return false;
        return true;
    }

    /// <summary>false se o texto for inválido ou o atalho já estiver em uso por outro programa.</summary>
    public static bool Register(IntPtr hwnd, int id, string? text)
        => TryParse(text, out uint mods, out uint vk) && RegisterHotKey(hwnd, id, mods | MOD_NOREPEAT, vk);

    public static void Unregister(IntPtr hwnd, int id) => UnregisterHotKey(hwnd, id);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool RegisterHotKey(IntPtr hWnd, int id, uint modifiers, uint vk);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool UnregisterHotKey(IntPtr hWnd, int id);
}
