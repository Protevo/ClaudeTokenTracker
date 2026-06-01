namespace ClaudeTokenTracker.Models;

/// <summary>
/// A single rolling usage window returned by claude.ai's private usage endpoint.
/// The API only ever gives us a utilization fraction (0.0-1.0) and a reset time;
/// there are no token counts.
/// </summary>
public sealed class UsageWindow
{
    /// <summary>Raw API key, e.g. "five_hour", "seven_day_opus".</summary>
    public required string Key { get; init; }

    /// <summary>Human-friendly label, e.g. "5-hour", "7-day (Opus)".</summary>
    public required string DisplayName { get; init; }

    /// <summary>Utilization as a fraction in [0, 1+]. Can exceed 1 once a limit is hit.</summary>
    public double Utilization { get; init; }

    /// <summary>When this window's oldest usage ages out / the bucket replenishes.</summary>
    public DateTimeOffset? ResetsAt { get; init; }

    public int Percent => (int)Math.Round(Math.Clamp(Utilization, 0, 9.99) * 100);

    /// <summary>True once usage has reached (or passed) this window's rate limit.</summary>
    public bool IsLimited => Utilization >= 1.0;

    /// <summary>Lower = shown first. 5-hour and overall 7-day float to the top.</summary>
    public int SortOrder { get; init; }
}

/// <summary>
/// Snapshot of everything we managed to read in a single refresh, plus any error.
/// </summary>
public sealed class UsageSnapshot
{
    public IReadOnlyList<UsageWindow> Windows { get; init; } = Array.Empty<UsageWindow>();

    public DateTimeOffset RetrievedAt { get; init; } = DateTimeOffset.Now;

    public string? PlanLabel { get; init; }

    public string? OrgName { get; init; }

    /// <summary>The org UUID actually queried (useful when it was auto-resolved).</summary>
    public string? ResolvedOrgUuid { get; init; }

    /// <summary>Extra-usage (metered overage) info, when present. Free-form display string.</summary>
    public string? ExtraUsageLabel { get; init; }

    /// <summary>Non-null when the refresh failed; <see cref="Windows"/> will be empty.</summary>
    public string? Error { get; init; }

    public bool IsError => Error is not null;

    /// <summary>The highest utilization across all windows (this is what gets you limited).</summary>
    public UsageWindow? Peak =>
        Windows.Count == 0 ? null : Windows.Aggregate((a, b) => b.Utilization > a.Utilization ? b : a);

    public static UsageSnapshot FromError(string message) => new() { Error = message };
}

/// <summary>A claude.ai organization (account) the session has access to.</summary>
public sealed class ClaudeOrg
{
    public required string Uuid { get; init; }
    public required string Name { get; init; }

    /// <summary>Friendly plan derived from rate-limit tier / capabilities, e.g. "Max 5x".</summary>
    public string? PlanLabel { get; init; }

    /// <summary>True if this org looks like a consumer (Pro/Max) plan with usage to meter.</summary>
    public bool HasConsumerPlan { get; init; }

    public override string ToString() => PlanLabel is null ? Name : $"{Name} ({PlanLabel})";
}
