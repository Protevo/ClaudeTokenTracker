using Microsoft.Win32;

namespace ClaudeTokenTracker.Services;

/// <summary>
/// Toggles "launch at login" by writing the current executable to the per-user
/// Run key (HKCU\...\Run). No admin rights required.
/// </summary>
public static class StartupManager
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "ClaudeTokenTracker";

    private static string ExecutablePath =>
        Environment.ProcessPath ?? Application.ExecutablePath;

    public static bool IsEnabled()
    {
        try
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
            string? existing = key?.GetValue(ValueName) as string;
            return !string.IsNullOrEmpty(existing);
        }
        catch
        {
            return false;
        }
    }

    public static void SetEnabled(bool enabled)
    {
        using RegistryKey key = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true)
            ?? throw new InvalidOperationException("Unable to open the Windows startup registry key.");

        if (enabled)
            key.SetValue(ValueName, $"\"{ExecutablePath}\"");
        else if (key.GetValue(ValueName) is not null)
            key.DeleteValue(ValueName, throwOnMissingValue: false);
    }
}
