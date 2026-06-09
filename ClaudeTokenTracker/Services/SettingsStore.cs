using System.IO;
using System.Text;
using System.Text.Json;
using ClaudeTokenTracker.Models;

namespace ClaudeTokenTracker.Services;

/// <summary>
/// Loads and saves <see cref="AppSettings"/> to disk. The cookie is encrypted at
/// rest with Windows DPAPI (current-user scope) so it can only be read back by the
/// same Windows user on the same machine.
/// </summary>
public static class SettingsStore
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("ClaudeTokenTracker.v1.cookie");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
    };

    public static string Directory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ClaudeTokenTracker");

    public static string FilePath => Path.Combine(Directory, "settings.json");

    public static AppSettings Load()
    {
        try
        {
            if (!File.Exists(FilePath))
                return new AppSettings();

            string json = File.ReadAllText(FilePath);
            var settings = JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();

            // Older settings files only had ShowNotifications for both alert types.
            if (!JsonContainsProperty(json, nameof(AppSettings.ShowResetNotifications)))
                settings.ShowResetNotifications = settings.ShowNotifications;

            settings.Cookie = Unprotect(settings.CookieProtected);
            settings.PollSeconds = Math.Max(15, settings.PollSeconds);
            settings.WarnThresholdPercent = Math.Clamp(settings.WarnThresholdPercent, 1, 100);
            return settings;
        }
        catch
        {
            // A corrupt or unreadable file should never prevent the app from starting.
            return new AppSettings();
        }
    }

    public static void Save(AppSettings settings)
    {
        System.IO.Directory.CreateDirectory(Directory);

        // Re-encrypt from the in-memory plaintext so callers only ever touch Cookie.
        settings.CookieProtected = Protect(settings.Cookie);

        string json = JsonSerializer.Serialize(settings, JsonOptions);
        File.WriteAllText(FilePath, json);
    }

    private static string? Protect(string? plaintext)
    {
        if (string.IsNullOrEmpty(plaintext))
            return null;

        byte[] encrypted = DataProtection.Protect(Encoding.UTF8.GetBytes(plaintext), Entropy);
        return Convert.ToBase64String(encrypted);
    }

    private static bool JsonContainsProperty(string json, string propertyName)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.TryGetProperty(propertyName, out _);
        }
        catch
        {
            return false;
        }
    }

    private static string? Unprotect(string? protectedBase64)
    {
        if (string.IsNullOrEmpty(protectedBase64))
            return null;

        try
        {
            byte[] bytes = DataProtection.Unprotect(Convert.FromBase64String(protectedBase64), Entropy);
            return Encoding.UTF8.GetString(bytes);
        }
        catch
        {
            return null;
        }
    }
}
