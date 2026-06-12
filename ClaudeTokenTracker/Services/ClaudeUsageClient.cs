using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using ClaudeTokenTracker.Models;

namespace ClaudeTokenTracker.Services;

/// <summary>
/// Thin client over claude.ai's private (undocumented) web endpoints that power the
/// Settings &gt; Usage page:
///   GET /api/organizations                  -&gt; orgs the session can see (+ plan tier)
///   GET /api/organizations/{uuid}/usage     -&gt; rolling utilization windows
///
/// Auth is the user's claude.ai session cookie; a Referer of /settings/usage is sent.
///
/// IMPORTANT: requests are made via the bundled Windows <c>curl.exe</c> rather than
/// .NET's HttpClient. claude.ai sits behind Cloudflare bot management, which fingerprints
/// .NET's TLS/HTTP stack and answers every request with a "Just a moment" challenge
/// (cf-mitigated: challenge). The OS curl passes that fingerprinting, so we shell out to
/// it. curl.exe ships in Windows 10 1803+ and Windows 11.
/// </summary>
public sealed class ClaudeUsageClient : IDisposable
{
    private const string BaseUrl = "https://claude.ai";
    private const string Referer = "https://claude.ai/settings/usage";

    private const string UserAgent =
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 " +
        "(KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36";

    // Appended after the body via curl's --write-out; must be newline-free (config-file
    // values are single-line), not start with '@' or '%' (curl --write-out specials), and
    // be unlikely to occur inside a JSON response body.
    private const string StatusMarker = "__CTTSTATUS__:";

    private static readonly IReadOnlyDictionary<string, (string Name, int Sort)> KnownWindows =
        new Dictionary<string, (string, int)>
        {
            ["five_hour"] = ("5-hour", 0),
            ["seven_day"] = ("7-day (all models)", 1),
            ["seven_day_sonnet"] = ("7-day (Sonnet)", 2),
            ["seven_day_opus"] = ("7-day (Opus)", 3),
            ["seven_day_oauth_apps"] = ("7-day (API apps)", 10),
            ["seven_day_omelette"] = ("7-day (Claude Code)", 11),
            ["seven_day_cowork"] = ("7-day (Cowork)", 12),
        };

    private readonly string _curlPath;

    public ClaudeUsageClient()
    {
        _curlPath = ResolveCurlPath();
    }

    /// <summary>Locates curl.exe (System32 first, then PATH).</summary>
    private static string ResolveCurlPath()
    {
        string system32 = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.System), "curl.exe");
        return File.Exists(system32) ? system32 : "curl.exe";
    }

    /// <summary>
    /// Accepts either a bare sessionKey value or a full "Cookie:" header string
    /// (copied from DevTools, which usefully also carries cf_clearance) and returns
    /// a header value usable as-is.
    /// </summary>
    public static string NormalizeCookie(string raw)
    {
        raw = (raw ?? string.Empty).Trim();
        if (raw.Length == 0)
            return raw;

        // Tolerate a pasted "Cookie: ..." / "cookie: ..." header prefix.
        if (raw.StartsWith("Cookie:", StringComparison.OrdinalIgnoreCase))
            raw = raw["Cookie:".Length..].Trim();

        raw = Unquote(raw);

        // If a larger blob was pasted (e.g. a copied cURL command or header dump) that
        // contains sessionKey= somewhere, extract just the cookies we care about.
        if (!raw.StartsWith("sessionKey=", StringComparison.OrdinalIgnoreCase) &&
            raw.Contains("sessionKey=", StringComparison.OrdinalIgnoreCase))
        {
            var pairs = new List<string>();
            foreach (string name in new[] { "sessionKey", "cf_clearance", "lastActiveOrg" })
            {
                string? value = ExtractCookieValue(raw, name);
                if (!string.IsNullOrEmpty(value))
                    pairs.Add($"{name}={value}");
            }
            if (pairs.Count > 0)
                return string.Join("; ", pairs);
        }

        // Already a cookie string (name=value pairs) -> use verbatim.
        if (raw.Contains('=') || raw.Contains(';'))
            return raw;

        // Otherwise treat the whole thing as the bare sessionKey value. Strip any stray
        // whitespace/newlines that a copy/paste may have introduced.
        raw = new string(raw.Where(c => !char.IsWhiteSpace(c)).ToArray());
        return "sessionKey=" + raw;
    }

    private static string Unquote(string s)
    {
        if (s.Length >= 2 &&
            ((s[0] == '"' && s[^1] == '"') || (s[0] == '\'' && s[^1] == '\'')))
            return s[1..^1].Trim();
        return s;
    }

    private static string? ExtractCookieValue(string blob, string name)
    {
        int start = blob.IndexOf(name + "=", StringComparison.OrdinalIgnoreCase);
        if (start < 0)
            return null;

        start += name.Length + 1;
        int end = start;
        while (end < blob.Length && blob[end] is not (';' or '"' or '\'' or '\r' or '\n' or ' '))
            end++;

        return end > start ? blob[start..end] : null;
    }

    public async Task<IReadOnlyList<ClaudeOrg>> GetOrgsAsync(string cookie, CancellationToken ct = default)
    {
        using JsonDocument doc = await GetJsonAsync(cookie, "/api/organizations", ct).ConfigureAwait(false);

        var orgs = new List<ClaudeOrg>();
        if (doc.RootElement.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement item in doc.RootElement.EnumerateArray())
            {
                string? uuid = GetString(item, "uuid") ?? GetString(item, "id");
                if (string.IsNullOrEmpty(uuid))
                    continue;

                string name = GetString(item, "name") ?? "Personal";
                var caps = ReadStringArray(item, "capabilities");
                string? tier = GetString(item, "rate_limit_tier");
                string? plan = PlanFromTier(tier, caps);

                orgs.Add(new ClaudeOrg
                {
                    Uuid = uuid,
                    Name = name,
                    PlanLabel = plan,
                    HasConsumerPlan = plan is not null || caps.Contains("chat"),
                });
            }
        }

        return orgs;
    }

    /// <summary>
    /// Fetches the usage snapshot. Resolves an org automatically when one isn't
    /// supplied. Network/auth failures are returned as <see cref="UsageSnapshot.Error"/>
    /// rather than thrown, so callers can keep the tray icon alive.
    /// </summary>
    public async Task<UsageSnapshot> GetUsageAsync(string? cookie, string? orgUuid, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(cookie))
            return UsageSnapshot.FromError("No session cookie set. Open Settings to add one.");

        try
        {
            // Always list orgs: it's a cheap call that also gives us the plan + name, and
            // lets us fall back gracefully if a previously-saved org UUID is stale.
            IReadOnlyList<ClaudeOrg> orgs = await GetOrgsAsync(cookie, ct).ConfigureAwait(false);
            if (orgs.Count == 0)
                return UsageSnapshot.FromError("No Claude organizations found for this session.");

            ClaudeOrg org;
            if (string.IsNullOrWhiteSpace(orgUuid))
            {
                org = orgs.FirstOrDefault(o => o.HasConsumerPlan) ?? orgs[0];
            }
            else
            {
                ClaudeOrg? matched = orgs.FirstOrDefault(o => o.Uuid == orgUuid);
                if (matched is null)
                {
                    return UsageSnapshot.FromError(
                        "That organization is no longer on this session. Open Settings, " +
                        "click Test connection, then pick the org again.");
                }
                org = matched;
            }

            using JsonDocument usageDoc =
                await GetJsonAsync(cookie, $"/api/organizations/{org.Uuid}/usage", ct).ConfigureAwait(false);

            List<UsageWindow> windows = ParseWindows(usageDoc.RootElement, out string? extraLabel);

            return new UsageSnapshot
            {
                Windows = windows,
                OrgName = org.Name,
                ResolvedOrgUuid = org.Uuid,
                Orgs = orgs,
                PlanLabel = org.PlanLabel,
                ExtraUsageLabel = extraLabel,
                RetrievedAt = DateTimeOffset.Now,
            };
        }
        catch (UsageException ux)
        {
            return UsageSnapshot.FromError(ux.Message);
        }
        catch (OperationCanceledException)
        {
            return UsageSnapshot.FromError("Request timed out. Check your internet connection.");
        }
        catch (Exception ex)
        {
            return UsageSnapshot.FromError("Unexpected error: " + ex.Message);
        }
    }

    private static List<UsageWindow> ParseWindows(JsonElement root, out string? extraLabel)
    {
        extraLabel = null;
        var windows = new List<UsageWindow>();

        if (root.ValueKind != JsonValueKind.Object)
            return windows;

        // Some responses nest the windows; if the root has no utilization-bearing
        // child, look one level down for an object that does.
        JsonElement container = root;
        if (!HasUtilizationChild(root))
        {
            foreach (JsonProperty prop in root.EnumerateObject())
            {
                if (prop.Value.ValueKind == JsonValueKind.Object && HasUtilizationChild(prop.Value))
                {
                    container = prop.Value;
                    break;
                }
            }
        }

        foreach (JsonProperty prop in container.EnumerateObject())
        {
            if (prop.Value.ValueKind != JsonValueKind.Object)
                continue;

            if (prop.Name.Equals("extra_usage", StringComparison.OrdinalIgnoreCase))
            {
                extraLabel = DescribeExtraUsage(prop.Value);
                continue;
            }

            if (!TryGetDouble(prop.Value, "utilization", out double utilization))
                continue;

            (string display, int sort) = KnownWindows.TryGetValue(prop.Name, out var meta)
                ? meta
                : (Prettify(prop.Name), 50);

            windows.Add(new UsageWindow
            {
                Key = prop.Name,
                DisplayName = display,
                // The API reports utilization as a percentage (e.g. 14.0 == 14%),
                // confirmed against live data. We store it as a 0-1 fraction.
                Utilization = utilization / 100.0,
                ResetsAt = GetResetTime(prop.Value),
                SortOrder = sort,
            });
        }

        windows.Sort((a, b) => a.SortOrder != b.SortOrder
            ? a.SortOrder.CompareTo(b.SortOrder)
            : string.Compare(a.DisplayName, b.DisplayName, StringComparison.Ordinal));
        return windows;
    }

    private static bool HasUtilizationChild(JsonElement obj)
    {
        foreach (JsonProperty prop in obj.EnumerateObject())
        {
            if (prop.Value.ValueKind == JsonValueKind.Object &&
                prop.Value.TryGetProperty("utilization", out _))
                return true;
        }
        return false;
    }

    private static string? DescribeExtraUsage(JsonElement el)
    {
        bool enabled = (GetBool(el, "is_enabled") ?? GetBool(el, "enabled")) ?? true;
        if (!enabled)
            return "Extra usage: off";

        // Credit-based metered billing. used_credits and monthly_limit ship in cents
        // (same as /overage_spend_limit); divide by 100 to match claude.ai/settings/usage.
        if (TryGetDouble(el, "used_credits", out double usedCents))
        {
            string? currency = GetString(el, "currency");
            double used = usedCents / 100.0;

            if (TryGetMonthlyLimitCents(el, out double limitCents) && limitCents > 0)
            {
                double limit = limitCents / 100.0;
                return $"Extra usage: {FormatMoney(used, currency)} / {FormatMoney(limit, currency)}";
            }

            return $"Extra usage: {FormatMoney(used, currency)} used (no cap)";
        }

        if (TryGetDouble(el, "utilization", out double util))
            return $"Extra usage: {(int)Math.Round(util)}%";

        return "Extra usage: on";
    }

    private static bool TryGetMonthlyLimitCents(JsonElement el, out double cents)
    {
        if (TryGetDouble(el, "monthly_limit", out cents))
            return true;
        return TryGetDouble(el, "monthly_credit_limit", out cents);
    }

    private static string FormatMoney(double amount, string? currency)
    {
        string code = (currency ?? "USD").Trim().ToUpperInvariant();
        return code == "USD" ? $"${amount:0.##}" : $"{amount:0.##} {code}";
    }

    /// <summary>
    /// Maps an org's rate-limit tier (e.g. "default_claude_max_5x") or capabilities into a
    /// friendly plan label. Returns null for API-only / unrecognized orgs.
    /// </summary>
    private static string? PlanFromTier(string? tier, IReadOnlyCollection<string> capabilities)
    {
        string t = (tier ?? string.Empty).ToLowerInvariant();

        if (t.Contains("max_20") || t.Contains("max20")) return "Max 20x";
        if (t.Contains("max_5") || t.Contains("max5")) return "Max 5x";
        if (t.Contains("max")) return "Max";
        if (t.Contains("team")) return "Team";
        if (t.Contains("enterprise")) return "Enterprise";
        if (t.Contains("pro")) return "Pro";

        bool Has(string c) => capabilities.Any(x => x.Equals(c, StringComparison.OrdinalIgnoreCase));
        if (Has("claude_max")) return "Max";
        if (Has("claude_pro")) return "Pro";
        if (Has("claude_enterprise") || Has("raven")) return "Enterprise";
        return null;
    }

    private static List<string> ReadStringArray(JsonElement el, string name)
    {
        var list = new List<string>();
        if (el.ValueKind == JsonValueKind.Object &&
            el.TryGetProperty(name, out JsonElement arr) &&
            arr.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement item in arr.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.String && item.GetString() is { } s)
                    list.Add(s);
            }
        }
        return list;
    }

    private async Task<JsonDocument> GetJsonAsync(string cookie, string path, CancellationToken ct)
    {
        (int status, string body) = await RunCurlAsync(NormalizeCookie(cookie), BaseUrl + path, ct)
            .ConfigureAwait(false);

        if (status is < 200 or >= 300)
            throw new UsageException(DescribeHttpError(status, body));

        string trimmed = body.TrimStart();
        if (trimmed.Length == 0 || (trimmed[0] != '{' && trimmed[0] != '['))
            throw new UsageException(
                "Got a non-JSON response from claude.ai (likely a Cloudflare challenge or login page). " +
                "Open claude.ai in your browser to confirm you're logged in, then re-copy the sessionKey.");

        try
        {
            return JsonDocument.Parse(body);
        }
        catch (JsonException)
        {
            throw new UsageException("Could not parse the response from claude.ai.");
        }
    }

    /// <summary>
    /// Performs a GET via curl.exe. Headers + cookie are passed through a short-lived
    /// curl config file so the cookie never appears in the process command line. Returns
    /// the HTTP status code and response body.
    /// </summary>
    private async Task<(int Status, string Body)> RunCurlAsync(string cookieHeader, string url, CancellationToken ct)
    {
        string configPath = Path.Combine(Path.GetTempPath(), "ctt_" + Guid.NewGuid().ToString("N") + ".curl");
        File.WriteAllText(configPath, BuildCurlConfig(cookieHeader, url));

        var psi = new ProcessStartInfo
        {
            FileName = _curlPath,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
        };
        psi.ArgumentList.Add("--config");
        psi.ArgumentList.Add(configPath);

        Process process;
        try
        {
            process = Process.Start(psi) ?? throw new UsageException("Could not start curl.exe.");
        }
        catch (Exception ex) when (ex is not UsageException)
        {
            throw new UsageException(
                "Could not run curl.exe, which this app uses to reach claude.ai. " +
                "curl ships with Windows 10 (1803+) and Windows 11. Details: " + ex.Message);
        }

        try
        {
            string stdout = await process.StandardOutput.ReadToEndAsync(ct).ConfigureAwait(false);
            string stderr = await process.StandardError.ReadToEndAsync(ct).ConfigureAwait(false);
            await process.WaitForExitAsync(ct).ConfigureAwait(false);

            int marker = stdout.LastIndexOf(StatusMarker, StringComparison.Ordinal);
            if (marker < 0)
            {
                string detail = string.IsNullOrWhiteSpace(stderr) ? "no response" : stderr.Trim();
                throw new UsageException($"Network request failed (curl exit {process.ExitCode}): {detail}");
            }

            string body = stdout[..marker];
            string statusText = stdout[(marker + StatusMarker.Length)..].Trim();
            _ = int.TryParse(statusText, out int status);
            return (status, body);
        }
        finally
        {
            process.Dispose();
            try { File.Delete(configPath); } catch { /* best effort */ }
        }
    }

    private static string BuildCurlConfig(string cookieHeader, string url)
    {
        // curl config syntax: one option per line; values quoted. Backslashes/quotes in
        // values must be escaped.
        static string Q(string v) => "\"" + v.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";

        var sb = new StringBuilder();
        sb.AppendLine("silent");
        sb.AppendLine("show-error");
        // Schannel tries OCSP/CRL revocation by default; on some networks (or when
        // revocation endpoints are blocked) that fails with CRYPT_E_NO_REVOCATION_CHECK
        // and curl reports HTTP 000 — browsers often still work because they cache
        // revocation state or use a different TLS stack.
        sb.AppendLine("ssl-no-revoke");
        sb.AppendLine("location");
        sb.AppendLine("max-time = 25");
        sb.AppendLine("user-agent = " + Q(UserAgent));
        sb.AppendLine("header = " + Q("Accept: */*"));
        sb.AppendLine("header = " + Q("Referer: " + Referer));
        sb.AppendLine("header = " + Q("anthropic-client-platform: web_claude_ai"));
        sb.AppendLine("header = " + Q("Cookie: " + cookieHeader));
        sb.AppendLine("write-out = " + Q(StatusMarker + "%{http_code}"));
        sb.AppendLine("url = " + Q(url));
        return sb.ToString();
    }

    private static string DescribeHttpError(int status, string body)
    {
        string? api = TryGetApiError(body);
        string suffix = api is null ? string.Empty : $" claude.ai says: {api}.";

        return status switch
        {
            0 =>
                "Could not reach claude.ai (no HTTP response). Check your internet connection, " +
                "VPN, or firewall. If those are fine, Windows may be blocking SSL certificate " +
                "revocation checks — update the app or retry after a reboot.",
            401 or 403 =>
                $"Session rejected (HTTP {status}).{suffix} Your sessionKey is missing, wrong, or " +
                "expired. In a logged-in browser, copy the full value of the \"sessionKey\" cookie " +
                "(it starts with sk-ant-sid) and paste it again.",
            429 =>
                $"Rate limited by claude.ai (HTTP 429).{suffix} Try a longer poll interval.",
            404 =>
                $"Not found (HTTP 404).{suffix} The selected organization may be wrong.",
            _ => $"claude.ai returned HTTP {status}.{suffix}",
        };
    }

    /// <summary>
    /// Pulls a human message out of claude.ai's error envelope:
    /// {"type":"error","error":{"message":"...","details":{"error_code":"..."}}}.
    /// </summary>
    private static string? TryGetApiError(string body)
    {
        if (string.IsNullOrWhiteSpace(body) || !body.TrimStart().StartsWith('{'))
            return null;

        try
        {
            using JsonDocument doc = JsonDocument.Parse(body);
            if (!doc.RootElement.TryGetProperty("error", out JsonElement err) ||
                err.ValueKind != JsonValueKind.Object)
                return null;

            string? message = GetString(err, "message");
            string? code = null;
            if (err.TryGetProperty("details", out JsonElement details) &&
                details.ValueKind == JsonValueKind.Object)
                code = GetString(details, "error_code");

            if (message is null && code is null)
                return null;
            if (message is not null && code is not null)
                return $"{message} ({code})";
            return message ?? code;
        }
        catch
        {
            return null;
        }
    }

    // ---- small JSON helpers (defensive against the schema shifting) ----

    private static string? GetString(JsonElement el, string name) =>
        el.ValueKind == JsonValueKind.Object &&
        el.TryGetProperty(name, out JsonElement v) &&
        v.ValueKind == JsonValueKind.String
            ? v.GetString()
            : null;

    private static bool? GetBool(JsonElement el, string name)
    {
        if (el.ValueKind == JsonValueKind.Object && el.TryGetProperty(name, out JsonElement v))
        {
            if (v.ValueKind == JsonValueKind.True) return true;
            if (v.ValueKind == JsonValueKind.False) return false;
        }
        return null;
    }

    private static bool TryGetDouble(JsonElement el, string name, out double value)
    {
        value = 0;
        if (el.ValueKind == JsonValueKind.Object &&
            el.TryGetProperty(name, out JsonElement v) &&
            v.ValueKind == JsonValueKind.Number &&
            v.TryGetDouble(out double d))
        {
            value = d;
            return true;
        }
        return false;
    }

    private static DateTimeOffset? GetResetTime(JsonElement el)
    {
        foreach (string name in new[] { "resets_at", "reset_at", "resets", "resetsAt" })
        {
            if (!el.TryGetProperty(name, out JsonElement v))
                continue;

            if (v.ValueKind == JsonValueKind.String &&
                DateTimeOffset.TryParse(v.GetString(), out DateTimeOffset parsed))
                return parsed.ToLocalTime();

            if (v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out long epoch))
            {
                // Heuristic: large values are milliseconds, otherwise seconds.
                return epoch > 1_000_000_000_000
                    ? DateTimeOffset.FromUnixTimeMilliseconds(epoch).ToLocalTime()
                    : DateTimeOffset.FromUnixTimeSeconds(epoch).ToLocalTime();
            }
        }
        return null;
    }

    private static string Prettify(string key)
    {
        string spaced = key.Replace('_', ' ').Replace('-', ' ').Trim();
        if (spaced.Length == 0)
            return key;
        return char.ToUpperInvariant(spaced[0]) + spaced[1..];
    }

    // Nothing persistent to release (curl runs per request); kept for the callers' using-pattern.
    public void Dispose() { }

    /// <summary>Internal control-flow exception carrying a user-friendly message.</summary>
    private sealed class UsageException(string message) : Exception(message);
}
