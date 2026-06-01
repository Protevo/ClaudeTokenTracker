using Microsoft.Win32;

namespace ClaudeTokenTracker.Services;

/// <summary>
/// Forces our tray icon to stay visible on the taskbar instead of being tucked
/// away in the "⌃" overflow flyout.
///
/// Windows 11 (22H2+) records every notify icon it has seen under
/// <c>HKCU\Control Panel\NotifyIconSettings</c>, keyed by a random per-icon id.
/// Each subkey carries an <c>ExecutablePath</c> (REG_SZ) and an <c>IsPromoted</c>
/// (REG_DWORD) flag: <c>1</c> = always shown on the taskbar, <c>0</c> = hidden in
/// the overflow. Flipping it takes effect immediately (no Explorer restart).
///
/// Caveats handled by the caller: the subkey only appears *after* our icon has been
/// shown at least once, and Windows can regenerate it, so we (re)apply on launch.
/// Older Windows (pre-22H2) has no such key — there the icon must be pinned by hand.
/// </summary>
public static class TaskbarPinner
{
    private const string KeyPath = @"Control Panel\NotifyIconSettings";

    public enum PinResult
    {
        /// <summary>We changed IsPromoted to the requested value.</summary>
        Changed,

        /// <summary>The icon was already in the requested state.</summary>
        AlreadySet,

        /// <summary>Our icon hasn't been registered yet — retry shortly.</summary>
        NotFound,

        /// <summary>This Windows version doesn't expose NotifyIconSettings.</summary>
        Unsupported,

        /// <summary>A registry error occurred.</summary>
        Failed,
    }

    /// <summary>Promotes (or demotes) this app's tray icon on the taskbar.</summary>
    public static PinResult SetPinned(bool pinned)
    {
        try
        {
            string? exe = Environment.ProcessPath;
            if (string.IsNullOrEmpty(exe))
                return PinResult.Failed;

            using RegistryKey? root = Registry.CurrentUser.OpenSubKey(KeyPath, writable: true);
            if (root is null)
                return PinResult.Unsupported;

            List<string> matches = FindMatchingSubKeys(root, exe);
            if (matches.Count == 0)
                return PinResult.NotFound;

            int desired = pinned ? 1 : 0;
            bool changed = false;
            foreach (string name in matches)
            {
                using RegistryKey? sub = root.OpenSubKey(name, writable: true);
                if (sub is null)
                    continue;

                int current = sub.GetValue("IsPromoted") is int v ? v : 0;
                if (current != desired)
                {
                    sub.SetValue("IsPromoted", desired, RegistryValueKind.DWord);
                    changed = true;
                }
            }

            return changed ? PinResult.Changed : PinResult.AlreadySet;
        }
        catch
        {
            return PinResult.Failed;
        }
    }

    /// <summary>True if this app's icon is currently set to always show on the taskbar.</summary>
    public static bool IsPinned()
    {
        try
        {
            string? exe = Environment.ProcessPath;
            if (string.IsNullOrEmpty(exe))
                return false;

            using RegistryKey? root = Registry.CurrentUser.OpenSubKey(KeyPath, writable: false);
            if (root is null)
                return false;

            foreach (string name in FindMatchingSubKeys(root, exe))
            {
                using RegistryKey? sub = root.OpenSubKey(name, writable: false);
                if (sub?.GetValue("IsPromoted") is int v && v == 1)
                    return true;
            }

            return false;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Finds the NotifyIconSettings subkeys that belong to this executable. Prefers an
    /// exact path match; falls back to a filename match (handles short/8.3 or differently
    /// cased paths Windows may have stored) only when no exact match exists.
    /// </summary>
    private static List<string> FindMatchingSubKeys(RegistryKey root, string exe)
    {
        string exeName = Path.GetFileName(exe);
        var exact = new List<string>();
        var byName = new List<string>();

        foreach (string name in root.GetSubKeyNames())
        {
            using RegistryKey? sub = root.OpenSubKey(name, writable: false);
            if (sub?.GetValue("ExecutablePath") is not string path || path.Length == 0)
                continue;

            if (string.Equals(path, exe, StringComparison.OrdinalIgnoreCase))
                exact.Add(name);
            else if (string.Equals(Path.GetFileName(path), exeName, StringComparison.OrdinalIgnoreCase))
                byName.Add(name);
        }

        return exact.Count > 0 ? exact : byName;
    }
}
