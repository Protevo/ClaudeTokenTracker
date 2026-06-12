using System.Text.Json.Serialization;

namespace ClaudeTokenTracker.Models;

/// <summary>
/// User configuration persisted to %APPDATA%\ClaudeTokenTracker\settings.json.
/// The session cookie is stored encrypted (DPAPI) in <see cref="CookieProtected"/>;
/// the plaintext lives only in memory via <see cref="Cookie"/>.
/// </summary>
public sealed class AppSettings
{
    /// <summary>DPAPI-encrypted, base64-encoded cookie blob. Never the raw value.</summary>
    public string? CookieProtected { get; set; }

    /// <summary>Organization UUID to query. If null we use the first org found.</summary>
    public string? OrgUuid { get; set; }

    /// <summary>Friendly org name, cached for display only.</summary>
    public string? OrgName { get; set; }

    /// <summary>
    /// Every org the session can see (e.g. two accounts under the same e-mail),
    /// cached so the org switcher works immediately on launch. Refreshed on each
    /// successful poll and on "Test connection".
    /// </summary>
    public List<ClaudeOrg> KnownOrgs { get; set; } = new();

    /// <summary>How often to poll claude.ai, in seconds. Minimum enforced at load.</summary>
    public int PollSeconds { get; set; } = 60;

    /// <summary>Utilization (0-100) at which a balloon warning fires.</summary>
    public int WarnThresholdPercent { get; set; } = 80;

    public bool StartWithWindows { get; set; }

    public bool ShowNotifications { get; set; } = true;

    /// <summary>Balloon when a maxed-out usage window resets and tokens are available again.</summary>
    public bool ShowResetNotifications { get; set; } = true;

    /// <summary>
    /// Keep the tray icon permanently visible on the taskbar (Windows 11) instead of
    /// letting it hide in the "⌃" overflow flyout. Re-applied on each launch.
    /// </summary>
    public bool PinToTaskbar { get; set; } = true;

    /// <summary>Plaintext cookie header value. Not serialized to disk.</summary>
    [JsonIgnore]
    public string? Cookie { get; set; }

    public AppSettings Clone() => new()
    {
        CookieProtected = CookieProtected,
        OrgUuid = OrgUuid,
        OrgName = OrgName,
        KnownOrgs = new List<ClaudeOrg>(KnownOrgs),
        PollSeconds = PollSeconds,
        WarnThresholdPercent = WarnThresholdPercent,
        StartWithWindows = StartWithWindows,
        ShowNotifications = ShowNotifications,
        ShowResetNotifications = ShowResetNotifications,
        PinToTaskbar = PinToTaskbar,
        Cookie = Cookie,
    };
}
