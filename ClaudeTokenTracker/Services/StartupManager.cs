using Microsoft.Win32;

namespace ClaudeTokenTracker.Services;

/// <summary>
/// Toggles "launch at login" by writing the current executable to the per-user
/// Run key (HKCU\...\Run). No admin rights required.
///
/// Windows lets the user disable a Run entry from Task Manager → Startup (or
/// Settings → Apps → Startup) <i>without</i> removing it: it records a "disabled" flag
/// in the parallel <c>StartupApproved\Run</c> key and then silently ignores the Run
/// entry at sign-in. If we don't account for that flag the app looks enabled (the
/// Run value is still there) yet never launches, and re-toggling can't fix it. So we
/// treat that flag as authoritative: report the <i>effective</i> state and clear the
/// flag whenever the user re-enables, so the toggle actually takes effect.
/// </summary>
public static class StartupManager
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ApprovedKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";
    private const string ValueName = "ClaudeTokenTracker";

    private static string ExecutablePath =>
        Environment.ProcessPath ?? Application.ExecutablePath;

    /// <summary>
    /// True only if we launch at sign-in <i>effectively</i>: the Run entry exists AND it
    /// has not been disabled via Task Manager / Settings (the StartupApproved flag).
    /// </summary>
    public static bool IsEnabled()
    {
        try
        {
            using RegistryKey? run = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
            if (run?.GetValue(ValueName) is not string existing || string.IsNullOrEmpty(existing))
                return false;

            return !IsDisabledByWindows();
        }
        catch
        {
            return false;
        }
    }

    public static void SetEnabled(bool enabled)
    {
        using RegistryKey run = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true)
            ?? throw new InvalidOperationException("Unable to open the Windows startup registry key.");

        if (enabled)
        {
            run.SetValue(ValueName, $"\"{ExecutablePath}\"");
            // Re-enabling must also clear any "disabled" flag a prior Task Manager /
            // Settings toggle left behind, otherwise Windows keeps ignoring the entry.
            ClearStartupApprovedFlag();
        }
        else
        {
            if (run.GetValue(ValueName) is not null)
                run.DeleteValue(ValueName, throwOnMissingValue: false);
            // Drop the flag too so a future enable starts from a clean slate.
            ClearStartupApprovedFlag();
        }
    }

    /// <summary>
    /// Whether Windows has flagged our entry as disabled in StartupApproved. The value is
    /// a small binary blob whose first byte is even (0x02/0x00) when enabled and odd
    /// (0x03) when the user disabled it; an absent value counts as enabled.
    /// </summary>
    private static bool IsDisabledByWindows()
    {
        using RegistryKey? approved = Registry.CurrentUser.OpenSubKey(ApprovedKeyPath, writable: false);
        return approved?.GetValue(ValueName) is byte[] flag && flag.Length > 0 && (flag[0] & 1) != 0;
    }

    /// <summary>
    /// Removes our StartupApproved entry so Windows honours the Run key again (an absent
    /// entry counts as enabled). Best-effort: the Run value alone is enough for users who
    /// never disabled the app via Task Manager.
    /// </summary>
    private static void ClearStartupApprovedFlag()
    {
        try
        {
            using RegistryKey? approved = Registry.CurrentUser.OpenSubKey(ApprovedKeyPath, writable: true);
            if (approved?.GetValue(ValueName) is not null)
                approved.DeleteValue(ValueName, throwOnMissingValue: false);
        }
        catch
        {
            // Ignore: StartupApproved is an optimization on top of the Run key.
        }
    }
}
