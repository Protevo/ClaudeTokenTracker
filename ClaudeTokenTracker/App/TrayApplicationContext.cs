using System.Drawing;
using ClaudeTokenTracker.Models;
using ClaudeTokenTracker.Services;
using ClaudeTokenTracker.UI;

namespace ClaudeTokenTracker.App;

/// <summary>
/// Owns the tray icon and the whole app lifecycle: polls claude.ai on a timer,
/// updates the tray icon (5-hour session only), tooltip/menu, fires threshold
/// notifications, and hosts the
/// Settings and Usage windows.
/// </summary>
public sealed class TrayApplicationContext : ApplicationContext
{
    private readonly ClaudeUsageClient _client = new();
    private readonly NotifyIcon _notifyIcon;
    private readonly ContextMenuStrip _menu;
    private readonly System.Windows.Forms.Timer _timer;

    private readonly ToolStripMenuItem _headerItem;
    private readonly ToolStripMenuItem _startupItem;
    private readonly ToolStripMenuItem _pinItem;

    private AppSettings _settings;
    private UsageForm? _usageForm;
    private UsageSnapshot? _lastSnapshot;

    // Last good snapshot per org uuid, so switching orgs shows data instantly
    // (a fresh refresh still follows).
    private readonly Dictionary<string, UsageSnapshot> _snapshotsByOrg = new();

    private Icon? _currentIcon;
    private IntPtr _currentIconHandle = IntPtr.Zero;

    // Windows registers our icon's NotifyIconSettings key a beat after it first
    // appears, so re-apply "pin to taskbar" a few times before giving up.
    private System.Windows.Forms.Timer? _pinTimer;
    private int _pinAttempts;

    // Keys are org-scoped (see NotifyKey) so alerts from one org never suppress
    // or fire for the other after a switch.
    private readonly HashSet<string> _notifiedKeys = new();

    // Windows we've seen hit their limit, mapped to the reset moment we'll announce.
    private readonly Dictionary<string, (DateTimeOffset ResetsAt, string DisplayName, string? OrgName)> _limitWatches = new();
    private readonly System.Windows.Forms.Timer _resetTimer;

    private bool _refreshing;
    private bool _refreshQueued;

    /// <summary>Org list for the switcher — prefer the live snapshot, fall back to persisted cache.</summary>
    private IReadOnlyList<ClaudeOrg> AvailableOrgs =>
        _lastSnapshot?.Orgs.Count > 0 ? _lastSnapshot.Orgs :
        _settings.KnownOrgs.Count > 0 ? _settings.KnownOrgs :
        Array.Empty<ClaudeOrg>();

    private bool MultiOrg => AvailableOrgs.Count > 1;

    public TrayApplicationContext()
    {
        _settings = SettingsStore.Load();

        _headerItem = new ToolStripMenuItem("Claude Token Tracker") { Enabled = false };
        _startupItem = new ToolStripMenuItem("Start with Windows") { CheckOnClick = true };
        _startupItem.Checked = StartupManager.IsEnabled();
        _startupItem.Click += OnToggleStartup;

        _pinItem = new ToolStripMenuItem("Always show icon on taskbar") { CheckOnClick = true };
        _pinItem.Checked = TaskbarPinner.IsPinned();
        _pinItem.Click += OnTogglePin;

        _menu = BuildMenu();
        _menu.Opening += (_, _) =>
        {
            PopulateWindowItems();
            PopulateOrgMenu();
            _pinItem.Checked = TaskbarPinner.IsPinned();
        };

        _notifyIcon = new NotifyIcon
        {
            Text = "Claude Token Tracker",
            Visible = true,
            ContextMenuStrip = _menu,
        };
        _notifyIcon.DoubleClick += (_, _) => ShowDetails();
        SetIcon(null);

        _timer = new System.Windows.Forms.Timer { Interval = _settings.PollSeconds * 1000 };
        _timer.Tick += async (_, _) => await RefreshAsync(userInitiated: false);

        // Announces "tokens available again" the moment a limited window's reset
        // time arrives, independent of the (possibly long) poll interval. Only runs
        // while at least one limit is being watched.
        _resetTimer = new System.Windows.Forms.Timer { Interval = 30_000 };
        _resetTimer.Tick += (_, _) => CheckResetWatches();

        if (string.IsNullOrWhiteSpace(_settings.Cookie))
        {
            _headerItem.Text = "Not connected — open Settings";
            _notifyIcon.ShowBalloonTip(5000, "Claude Token Tracker",
                "Add your claude.ai cookie in Settings to start tracking usage.", ToolTipIcon.Info);
            // First-run convenience: jump straight to setup.
            BeginInvokeOnIdle(ShowSettings);
        }
        else
        {
            _timer.Start();
            _ = RefreshAsync(userInitiated: false);
        }

        if (_settings.PinToTaskbar)
            EnsurePinnedWithRetry();
    }

    private ContextMenuStrip BuildMenu()
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add(_headerItem);                       // 0
        menu.Items.Add(new ToolStripSeparator());          // 1  (window items inserted at index 2)

        var details = new ToolStripMenuItem("Show details…", null, (_, _) => ShowDetails())
        {
            Font = new Font(menu.Font, FontStyle.Bold),
        };
        menu.Items.Add(details);
        menu.Items.Add(new ToolStripMenuItem("Refresh now", null, async (_, _) => await RefreshAsync(true)));
        menu.Items.Add(new ToolStripMenuItem("Open claude.ai usage page", null,
            (_, _) => UsageForm.OpenUrl("https://claude.ai/settings/usage")));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(new ToolStripMenuItem("Settings…", null, (_, _) => ShowSettings()));
        menu.Items.Add(_startupItem);
        menu.Items.Add(_pinItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(new ToolStripMenuItem("Exit", null, (_, _) => ExitApp()));
        return menu;
    }

    /// <summary>Rebuilds the per-window readout lines just before the menu opens.</summary>
    private void PopulateWindowItems()
    {
        for (int i = _menu.Items.Count - 1; i >= 0; i--)
        {
            if (_menu.Items[i].Tag as string == "win")
                _menu.Items.RemoveAt(i);
        }

        int idx = 2;
        if (_lastSnapshot is null)
        {
            _menu.Items.Insert(idx, MakeWindowItem("Loading…"));
            return;
        }

        if (_lastSnapshot.IsError)
        {
            _menu.Items.Insert(idx, MakeWindowItem("⚠ " + Shorten(_lastSnapshot.Error!, 60)));
            return;
        }

        UsageWindow? tray = _lastSnapshot.TrayWindow;
        if (tray is null)
        {
            _menu.Items.Insert(idx, MakeWindowItem("No 5-hour session data"));
            return;
        }

        string reset = UsageRow.FormatReset(tray.ResetsAt);
        string text = reset.Length > 0
            ? $"{tray.DisplayName}:  {tray.Percent}%   ({reset})"
            : $"{tray.DisplayName}:  {tray.Percent}%";
        _menu.Items.Insert(idx, MakeWindowItem(text));
    }

    private static ToolStripMenuItem MakeWindowItem(string text) =>
        new(text) { Enabled = false, Tag = "win" };

    /// <summary>
    /// Inserts flat, top-level org switcher items before Settings. Nested submenu
    /// clicks are unreliable on NotifyIcon context menus, so each org is its own
    /// menu row (radio-style check mark on the active one).
    /// </summary>
    private void PopulateOrgMenu()
    {
        for (int i = _menu.Items.Count - 1; i >= 0; i--)
        {
            object? tag = _menu.Items[i].Tag;
            if (tag is ClaudeOrg or "org-sep" or "org-header")
                _menu.Items.RemoveAt(i);
        }

        if (!MultiOrg)
            return;

        int settingsIdx = -1;
        for (int i = 0; i < _menu.Items.Count; i++)
        {
            if (_menu.Items[i] is ToolStripMenuItem { Text: "Settings…" })
            {
                settingsIdx = i;
                break;
            }
        }
        if (settingsIdx < 0)
            return;

        string? activeUuid = _settings.OrgUuid ?? _lastSnapshot?.ResolvedOrgUuid;

        _menu.Items.Insert(settingsIdx, new ToolStripSeparator { Tag = "org-sep" });
        settingsIdx++;

        _menu.Items.Insert(settingsIdx, new ToolStripMenuItem("Switch organization")
        {
            Enabled = false,
            Tag = "org-header",
        });
        settingsIdx++;

        foreach (ClaudeOrg org in AvailableOrgs)
        {
            var item = new ToolStripMenuItem(org.ToString())
            {
                Tag = org,
                Checked = org.Uuid == activeUuid,
                CheckOnClick = true,
            };
            item.Click += OnOrgMenuItemClick;
            _menu.Items.Insert(settingsIdx++, item);
        }
    }

    private void OnOrgMenuItemClick(object? sender, EventArgs e)
    {
        if (sender is ToolStripMenuItem { Tag: ClaudeOrg org })
            SwitchOrg(org);
    }

    /// <summary>
    /// Makes <paramref name="org"/> the tracked organization: persists the choice,
    /// shows its cached snapshot for instant feedback (when we have one), then
    /// kicks off a fresh refresh.
    /// </summary>
    private void SwitchOrg(ClaudeOrg org)
    {
        if (org.Uuid == _settings.OrgUuid)
            return;

        _settings.OrgUuid = org.Uuid;
        _settings.OrgName = org.Name;
        SettingsStore.Save(_settings);

        if (_snapshotsByOrg.TryGetValue(org.Uuid, out UsageSnapshot? cached))
        {
            _lastSnapshot = cached;
            ApplySnapshot(cached);
        }
        else
        {
            _lastSnapshot = null;
            SetIcon(null);
            _headerItem.Text = "Switching to " + Shorten(org.Name, 32) + "…";
            _notifyIcon.Text = Shorten("Claude: loading " + org.Name + "…", 127);
        }

        _ = RefreshAsync(userInitiated: true);
    }

    private async Task RefreshAsync(bool userInitiated)
    {
        if (_refreshing)
        {
            _refreshQueued = true;
            return;
        }

        if (string.IsNullOrWhiteSpace(_settings.Cookie))
        {
            if (userInitiated)
                ShowSettings();
            return;
        }

        bool switchedMidFlight = false;
        _refreshing = true;
        _usageForm?.SetBusy(true);
        try
        {
            string? requestedOrg = _settings.OrgUuid;
            UsageSnapshot snapshot = await _client.GetUsageAsync(_settings.Cookie, requestedOrg);

            // The user may have switched org while this request was running; the
            // result is then for the wrong org — keep it cached, but don't show it.
            switchedMidFlight = _settings.OrgUuid != requestedOrg;

            if (!snapshot.IsError)
            {
                if (!string.IsNullOrWhiteSpace(snapshot.ResolvedOrgUuid))
                    _snapshotsByOrg[snapshot.ResolvedOrgUuid!] = snapshot;
                SyncOrgSettings(snapshot, allowActiveOrgSync: !switchedMidFlight);
            }

            if (!switchedMidFlight)
            {
                _lastSnapshot = snapshot;
                ApplySnapshot(snapshot);
            }
        }
        finally
        {
            _refreshing = false;
            _usageForm?.SetBusy(false);
        }

        if (switchedMidFlight)
            await RefreshAsync(userInitiated);
        else if (_refreshQueued)
        {
            _refreshQueued = false;
            await RefreshAsync(userInitiated);
        }
    }

    /// <summary>
    /// Keeps the persisted org bookkeeping in line with what claude.ai just told us:
    /// caches the full org list for the switcher UIs, and auto-resolves OrgUuid only
    /// when the user hasn't picked one yet (never overwrites an explicit switch).
    /// </summary>
    private void SyncOrgSettings(UsageSnapshot snapshot, bool allowActiveOrgSync)
    {
        bool dirty = false;

        if (snapshot.Orgs.Count > 0 && !OrgListsEqual(_settings.KnownOrgs, snapshot.Orgs))
        {
            _settings.KnownOrgs = snapshot.Orgs.ToList();
            dirty = true;
        }

        if (allowActiveOrgSync && string.IsNullOrWhiteSpace(_settings.OrgUuid) &&
            !string.IsNullOrWhiteSpace(snapshot.ResolvedOrgUuid))
        {
            _settings.OrgUuid = snapshot.ResolvedOrgUuid;
            _settings.OrgName = snapshot.OrgName;
            dirty = true;
        }
        else if (allowActiveOrgSync &&
                 !string.IsNullOrWhiteSpace(_settings.OrgUuid) &&
                 _settings.OrgUuid == snapshot.ResolvedOrgUuid &&
                 _settings.OrgName != snapshot.OrgName)
        {
            _settings.OrgName = snapshot.OrgName;
            dirty = true;
        }

        if (dirty)
            SettingsStore.Save(_settings);
    }

    private static bool OrgListsEqual(IReadOnlyList<ClaudeOrg> a, IReadOnlyList<ClaudeOrg> b)
    {
        if (a.Count != b.Count)
            return false;
        for (int i = 0; i < a.Count; i++)
        {
            if (a[i].Uuid != b[i].Uuid || a[i].Name != b[i].Name || a[i].PlanLabel != b[i].PlanLabel)
                return false;
        }
        return true;
    }

    private void ApplySnapshot(UsageSnapshot snapshot)
    {
        _usageForm?.UpdateSnapshot(snapshot);

        if (snapshot.IsError)
        {
            SetIcon(null);
            _headerItem.Text = "Error — " + Shorten(snapshot.Error!, 40);
            _notifyIcon.Text = Shorten("Claude: " + snapshot.Error!, 127);
            return;
        }

        UsageWindow? tray = snapshot.TrayWindow;
        int trayPercent = tray?.Percent ?? 0;
        SetIcon(trayPercent);

        // With several orgs on one session, say which one the numbers belong to.
        string orgTag = MultiOrg && !string.IsNullOrWhiteSpace(snapshot.OrgName)
            ? Shorten(snapshot.OrgName!, 24) + " · "
            : string.Empty;

        _headerItem.Text = tray is null
            ? orgTag + "Connected — no 5-hour data"
            : $"{orgTag}5-hour: {trayPercent}%";

        _notifyIcon.Text = BuildTrayTooltip(snapshot);
        CheckThresholds(snapshot);
    }

    private void CheckThresholds(UsageSnapshot snapshot)
    {
        int threshold = _settings.WarnThresholdPercent;
        var newlyCrossed = new List<string>();

        if (snapshot.TrayWindow is not { } w)
            return;

        string key = NotifyKey(snapshot.ResolvedOrgUuid, w.Key);

        if (w.Percent >= threshold)
        {
            if (_notifiedKeys.Add(key))
            {
                string line = $"{w.DisplayName} at {w.Percent}%";
                if (MultiOrg && !string.IsNullOrWhiteSpace(snapshot.OrgName))
                    line = $"{snapshot.OrgName}: {line}";
                newlyCrossed.Add(line);
            }
        }
        else
        {
            // Allow a fresh notification next time this window climbs again.
            _notifiedKeys.Remove(key);
        }

        // Once the 5-hour window hits its limit, remember when it resets so we can
        // announce availability even if usage stays maxed right up to then — or the
        // user switches to their other org in the meantime.
        if (w.IsLimited && w.ResetsAt is { } reset && reset > DateTimeOffset.Now)
            _limitWatches[key] = (reset, w.DisplayName, snapshot.OrgName);

        if (newlyCrossed.Count > 0 && _settings.ShowNotifications)
        {
            _notifyIcon.ShowBalloonTip(
                7000,
                "Claude usage warning",
                string.Join(Environment.NewLine, newlyCrossed),
                ToolTipIcon.Warning);
        }

        CheckResetWatches();
    }

    /// <summary>
    /// Fires a "tokens available again" notification for any limited window whose
    /// reset time has arrived, then stops watching it. Called from each poll and
    /// from a dedicated 30s timer so the alert lands close to the real reset.
    /// </summary>
    private void CheckResetWatches()
    {
        if (_limitWatches.Count > 0)
        {
            DateTimeOffset now = DateTimeOffset.Now;
            List<string>? due = null;

            foreach (var (key, watch) in _limitWatches)
            {
                if (now >= watch.ResetsAt)
                    (due ??= new()).Add(key);
            }

            if (due is not null)
            {
                foreach (string key in due)
                {
                    (_, string displayName, string? orgName) = _limitWatches[key];
                    _limitWatches.Remove(key);
                    // Let the threshold warning re-arm if it climbs again.
                    _notifiedKeys.Remove(key);

                    if (_settings.ShowResetNotifications)
                    {
                        string message = MultiOrg && !string.IsNullOrWhiteSpace(orgName)
                            ? $"{orgName}: your {displayName} limit has reset — tokens are available again."
                            : $"Your {displayName} limit has reset — tokens are available again.";
                        _notifyIcon.ShowBalloonTip(10000, "Claude limit reset", message, ToolTipIcon.Info);
                    }
                }
            }
        }

        // Keep the dedicated timer alive only while something is pending.
        if (_limitWatches.Count > 0)
        {
            if (!_resetTimer.Enabled)
                _resetTimer.Start();
        }
        else if (_resetTimer.Enabled)
        {
            _resetTimer.Stop();
        }
    }

    /// <summary>Scopes notification bookkeeping to an org so switching never crosses alerts.</summary>
    private static string NotifyKey(string? orgUuid, string windowKey) => $"{orgUuid}|{windowKey}";

    private string BuildTrayTooltip(UsageSnapshot snapshot)
    {
        if (snapshot.TrayWindow is not { } w)
            return "Claude: connected (no 5-hour data)";

        string text = $"5h {w.Percent}%";
        if (MultiOrg && !string.IsNullOrWhiteSpace(snapshot.OrgName))
            text = $"{snapshot.OrgName} · {text}";

        string reset = UsageRow.FormatReset(w.ResetsAt);
        if (reset.Length > 0 && text.Length + reset.Length + 5 <= 127)
            text += $"  ·  {reset}";

        // NotifyIcon.Text throws over 127 chars; org names can push us there.
        return Shorten(text, 127);
    }

    private void SetIcon(int? percent)
    {
        (Icon icon, IntPtr handle) = TrayIconRenderer.Render(percent);
        _notifyIcon.Icon = icon;

        // Release the previously displayed icon now that the new one is active.
        _currentIcon?.Dispose();
        TrayIconRenderer.Destroy(_currentIconHandle);

        _currentIcon = icon;
        _currentIconHandle = handle;
    }

    private void ShowDetails()
    {
        if (_usageForm is null || _usageForm.IsDisposed)
        {
            _usageForm = new UsageForm(_currentIcon);
            _usageForm.RefreshRequested += async (_, _) => await RefreshAsync(true);
            _usageForm.OrgSwitchRequested += (_, org) => SwitchOrg(org);
            _usageForm.PositionNearTray();
            if (_lastSnapshot is not null)
                _usageForm.UpdateSnapshot(_lastSnapshot);
        }

        _usageForm.Show();
        _usageForm.WindowState = FormWindowState.Normal;
        _usageForm.BringToFront();
        _usageForm.Activate();

        if (_lastSnapshot is null)
            _ = RefreshAsync(true);
    }

    private void ShowSettings()
    {
        using var form = new SettingsForm(_client, _settings.Clone(), _currentIcon);
        if (form.ShowDialog() != DialogResult.OK || form.Result is null)
            return;

        AppSettings updated = form.Result;
        bool cookieChanged = !string.Equals(updated.Cookie, _settings.Cookie, StringComparison.Ordinal);

        try
        {
            if (updated.StartWithWindows != StartupManager.IsEnabled())
                StartupManager.SetEnabled(updated.StartWithWindows);
        }
        catch (Exception ex)
        {
            MessageBox.Show("Couldn't update the Windows startup setting:\n" + ex.Message,
                "Claude Token Tracker", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        SettingsStore.Save(updated);
        _settings = updated;
        _startupItem.Checked = StartupManager.IsEnabled();
        ApplyPinSetting(_settings.PinToTaskbar, announce: true);
        _pinItem.Checked = TaskbarPinner.IsPinned();
        _notifiedKeys.Clear();

        if (cookieChanged)
        {
            // A different session may see different orgs; drop anything tied to the old one.
            _snapshotsByOrg.Clear();
            _limitWatches.Clear();
        }
        else if (_settings.OrgUuid is { } uuid &&
                 _lastSnapshot?.ResolvedOrgUuid != uuid &&
                 _snapshotsByOrg.TryGetValue(uuid, out UsageSnapshot? cached))
        {
            // Org changed via the Settings dropdown — show its cached data right away.
            _lastSnapshot = cached;
            ApplySnapshot(cached);
        }

        _timer.Stop();
        _timer.Interval = _settings.PollSeconds * 1000;
        if (!string.IsNullOrWhiteSpace(_settings.Cookie))
            _timer.Start();

        _ = RefreshAsync(userInitiated: true);
    }

    private void OnToggleStartup(object? sender, EventArgs e)
    {
        try
        {
            StartupManager.SetEnabled(_startupItem.Checked);
            _settings.StartWithWindows = _startupItem.Checked;
            SettingsStore.Save(_settings);
        }
        catch (Exception ex)
        {
            _startupItem.Checked = StartupManager.IsEnabled();
            MessageBox.Show("Couldn't update the Windows startup setting:\n" + ex.Message,
                "Claude Token Tracker", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void OnTogglePin(object? sender, EventArgs e)
    {
        bool wanted = _pinItem.Checked;
        _settings.PinToTaskbar = wanted;
        SettingsStore.Save(_settings);
        ApplyPinSetting(wanted, announce: true);
    }

    /// <summary>
    /// Applies the "always show on taskbar" preference. When the icon's registry key
    /// isn't present yet we start a short retry loop; on older Windows we fall back to
    /// telling the user how to pin it by hand.
    /// </summary>
    private void ApplyPinSetting(bool pinned, bool announce)
    {
        TaskbarPinner.PinResult result = TaskbarPinner.SetPinned(pinned);

        switch (result)
        {
            case TaskbarPinner.PinResult.NotFound when pinned:
                // Icon not registered yet — keep trying for a few seconds.
                EnsurePinnedWithRetry();
                break;

            case TaskbarPinner.PinResult.Unsupported when announce:
                _pinItem.Checked = false;
                _settings.PinToTaskbar = false;
                SettingsStore.Save(_settings);
                MessageBox.Show(
                    "This Windows version doesn't let apps pin their own tray icon.\n\n" +
                    "To keep it visible, drag the icon out of the \"⌃\" overflow flyout onto the " +
                    "taskbar, or use Settings ▸ Personalization ▸ Taskbar ▸ Other system tray icons.",
                    "Claude Token Tracker", MessageBoxButtons.OK, MessageBoxIcon.Information);
                break;

            case TaskbarPinner.PinResult.Failed when announce:
                MessageBox.Show(
                    "Couldn't update the taskbar visibility setting. You can pin the icon manually " +
                    "by dragging it out of the \"⌃\" overflow flyout onto the taskbar.",
                    "Claude Token Tracker", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                break;
        }
    }

    private void EnsurePinnedWithRetry()
    {
        if (TaskbarPinner.SetPinned(true) != TaskbarPinner.PinResult.NotFound)
            return; // already applied (or unsupported/failed — no point retrying)

        _pinAttempts = 0;
        _pinTimer?.Dispose();
        _pinTimer = new System.Windows.Forms.Timer { Interval = 800 };
        _pinTimer.Tick += (_, _) =>
        {
            _pinAttempts++;
            bool done = TaskbarPinner.SetPinned(true) != TaskbarPinner.PinResult.NotFound
                        || _pinAttempts >= 8;
            if (done)
            {
                _pinTimer?.Stop();
                _pinTimer?.Dispose();
                _pinTimer = null;
            }
        };
        _pinTimer.Start();
    }

    private void ExitApp()
    {
        _timer.Stop();
        _resetTimer.Stop();
        _notifyIcon.Visible = false;
        ExitThread();
    }

    private static string Shorten(string text, int max) =>
        text.Length <= max ? text : text[..(max - 1)] + "…";

    /// <summary>Runs <paramref name="action"/> after the message loop is idle.</summary>
    private void BeginInvokeOnIdle(Action action)
    {
        void Handler(object? s, EventArgs e)
        {
            Application.Idle -= Handler;
            action();
        }
        Application.Idle += Handler;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _timer?.Dispose();
            _resetTimer?.Dispose();
            _pinTimer?.Dispose();
            _usageForm?.Dispose();
            _menu?.Dispose();

            if (_notifyIcon is not null)
            {
                _notifyIcon.Visible = false;
                _notifyIcon.Dispose();
            }

            _currentIcon?.Dispose();
            TrayIconRenderer.Destroy(_currentIconHandle);
            _client?.Dispose();
        }

        base.Dispose(disposing);
    }
}
