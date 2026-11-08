using Microsoft.Win32;

namespace ThermoBook.Services;

/// <summary>Liga e desliga "Iniciar com o Windows" pela chave Run do usuário (sem administrador).</summary>
internal sealed class StartupService(string keyPath = StartupService.RunKey, string valueName = "ThermoBook")
{
    public const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

    public bool IsEnabled
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(keyPath);
            return key?.GetValue(valueName) is not null;
        }
    }

    public void Set(bool enabled, string exePath)
    {
        using var key = Registry.CurrentUser.CreateSubKey(keyPath);
        if (enabled) key.SetValue(valueName, $"\"{exePath}\" --bandeja"); // ao ligar o PC, fica só na bandeja
        else key.DeleteValue(valueName, throwOnMissingValue: false);
    }
}
