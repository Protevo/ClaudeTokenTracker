using System.Drawing;
using ClaudeTokenTracker.Models;
using ClaudeTokenTracker.Services;

namespace ClaudeTokenTracker.UI;

/// <summary>
/// Configuration dialog: cookie entry, a live "Test connection" check, org picker,
/// poll cadence, warning threshold, and the startup/notification toggles.
/// </summary>
public sealed class SettingsForm : Form
{
    private const int PadX = 24;

    private readonly ClaudeUsageClient _client;
    private readonly AppSettings _original;

    private readonly TextBox _cookie;
    private readonly FlatButton _test;
    private readonly ComboBox _org;
    private readonly NumericUpDown _poll;
    private readonly NumericUpDown _warn;
    private readonly CheckBox _startup;
    private readonly CheckBox _notify;
    private readonly CheckBox _notifyReset;
    private readonly CheckBox _pin;
    private readonly Label _status;
    private readonly FlatButton _save;

    /// <summary>Populated on successful save.</summary>
    public AppSettings? Result { get; private set; }

    public SettingsForm(ClaudeUsageClient client, AppSettings current, Icon? icon)
    {
        _client = client;
        _original = current;

        Text = "Claude Token Tracker — Settings";
        if (icon is not null)
            Icon = icon;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterScreen;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        BackColor = Theme.Canvas;
        ForeColor = Theme.Ink;
        Font = Theme.Sans(9f);
        ClientSize = new Size(480, 656);

        int width = ClientSize.Width - PadX * 2;
        int right = PadX + width;

        // --- Header band ---
        var header = new Panel { Dock = DockStyle.Top, Height = 74, BackColor = Theme.Canvas };
        header.Paint += (s, e) => Theme.Hairline((Control)s!, e, top: false);
        header.Controls.Add(new Label
        {
            Text = "Settings",
            AutoSize = true,
            Font = Theme.Serif(16.5f),
            ForeColor = Theme.Ink,
            BackColor = Color.Transparent,
            Location = new Point(PadX, 18),
        });
        header.Controls.Add(new Label
        {
            Text = "Connect your claude.ai session and tune alerts.",
            AutoSize = true,
            Font = Theme.Sans(9f),
            ForeColor = Theme.InkMuted,
            BackColor = Color.Transparent,
            Location = new Point(PadX + 1, 48),
        });

        // --- Footer band (actions) ---
        var footer = new Panel { Dock = DockStyle.Bottom, Height = 64, BackColor = Theme.Canvas };
        footer.Paint += (s, e) => Theme.Hairline((Control)s!, e, top: true);

        _save = new FlatButton
        {
            Text = "Save",
            Variant = FlatButton.ButtonVariant.Primary,
            DialogResult = DialogResult.OK,
            Size = new Size(104, 34),
        };
        _save.Location = new Point(ClientSize.Width - PadX - _save.Width, (footer.Height - _save.Height) / 2);
        _save.Click += OnSaveClicked;

        var cancel = new FlatButton
        {
            Text = "Cancel",
            Variant = FlatButton.ButtonVariant.Secondary,
            DialogResult = DialogResult.Cancel,
            Size = new Size(96, 34),
        };
        cancel.Location = new Point(_save.Left - 12 - cancel.Width, (footer.Height - cancel.Height) / 2);

        footer.Controls.Add(_save);
        footer.Controls.Add(cancel);

        // --- Content ---
        int y = 92;

        var lblCookie = new Label
        {
            Text = "claude.ai session cookie",
            AutoSize = true,
            Font = Theme.Sans(9.5f, FontStyle.Bold),
            ForeColor = Theme.Ink,
            Location = new Point(PadX, y),
        };

        var help = new LinkLabel
        {
            Text = "How do I get this?",
            AutoSize = true,
            Font = Theme.Sans(9f),
            LinkColor = Theme.Accent,
            ActiveLinkColor = Theme.AccentPressed,
            LinkBehavior = LinkBehavior.HoverUnderline,
            Anchor = AnchorStyles.Top | AnchorStyles.Right,
        };
        help.LinkClicked += (_, _) => ShowCookieHelp();
        help.Location = new Point(right - help.PreferredSize.Width, y);

        y += 24;
        _cookie = new TextBox
        {
            Multiline = true,
            ScrollBars = ScrollBars.Vertical,
            BorderStyle = BorderStyle.FixedSingle,
            BackColor = Theme.Field,
            ForeColor = Theme.Ink,
            Location = new Point(PadX, y),
            Size = new Size(width, 84),
            Font = Theme.Mono(9f),
            Text = current.Cookie ?? string.Empty,
        };

        y += 92;
        var hint = new Label
        {
            Text = "Paste the value of the \"sessionKey\" cookie (starts with sk-ant-sid).",
            AutoSize = false,
            Location = new Point(PadX, y),
            Size = new Size(width, 18),
            ForeColor = Theme.InkMuted,
            Font = Theme.Sans(8.25f),
        };

        y += 26;
        _test = new FlatButton
        {
            Text = "Test connection",
            Variant = FlatButton.ButtonVariant.Secondary,
            Size = new Size(150, 32),
            Location = new Point(PadX, y),
        };
        _test.Click += async (_, _) => await TestAsync();

        // --- Options ---
        int labelX = PadX;
        int fieldX = PadX + 150;
        int fieldW = right - fieldX;

        y += 50;
        var lblOrg = new Label { Text = "Organization", AutoSize = true, ForeColor = Theme.InkSecondary, Location = new Point(labelX, y + 3) };
        _org = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            FlatStyle = FlatStyle.Flat,
            BackColor = Theme.Field,
            ForeColor = Theme.Ink,
            Font = Theme.Sans(9f),
            Location = new Point(fieldX, y),
            Width = fieldW,
        };
        // Seed with every org we've already seen so switching doesn't require a new
        // "Test connection" round-trip; fall back to the single saved org.
        foreach (ClaudeOrg org in current.KnownOrgs)
            _org.Items.Add(org);
        if (_org.Items.Count == 0 && !string.IsNullOrEmpty(current.OrgName) && !string.IsNullOrEmpty(current.OrgUuid))
            _org.Items.Add(new ClaudeOrg { Uuid = current.OrgUuid!, Name = current.OrgName! });

        if (_org.Items.Count > 0)
        {
            int selected = 0;
            for (int i = 0; i < _org.Items.Count; i++)
            {
                if (((ClaudeOrg)_org.Items[i]!).Uuid == current.OrgUuid)
                {
                    selected = i;
                    break;
                }
            }
            _org.SelectedIndex = selected;
        }

        y += 42;
        var lblPoll = new Label { Text = "Refresh every (seconds)", AutoSize = true, ForeColor = Theme.InkSecondary, Location = new Point(labelX, y + 3) };
        _poll = new NumericUpDown
        {
            Minimum = 15,
            Maximum = 3600,
            Value = Math.Clamp(current.PollSeconds, 15, 3600),
            BorderStyle = BorderStyle.FixedSingle,
            BackColor = Theme.Field,
            ForeColor = Theme.Ink,
            Font = Theme.Sans(9f),
            Location = new Point(fieldX, y),
            Width = 90,
        };

        y += 38;
        var lblWarn = new Label { Text = "Warn at utilization (%)", AutoSize = true, ForeColor = Theme.InkSecondary, Location = new Point(labelX, y + 3) };
        _warn = new NumericUpDown
        {
            Minimum = 1,
            Maximum = 100,
            Value = Math.Clamp(current.WarnThresholdPercent, 1, 100),
            BorderStyle = BorderStyle.FixedSingle,
            BackColor = Theme.Field,
            ForeColor = Theme.Ink,
            Font = Theme.Sans(9f),
            Location = new Point(fieldX, y),
            Width = 90,
        };

        y += 42;
        _startup = new CheckBox
        {
            Text = "Start automatically when I log in to Windows",
            AutoSize = true,
            ForeColor = Theme.Ink,
            BackColor = Color.Transparent,
            Checked = current.StartWithWindows,
            Location = new Point(labelX, y),
        };

        y += 28;
        _notify = new CheckBox
        {
            Text = "Show a notification when a limit is nearly reached",
            AutoSize = true,
            ForeColor = Theme.Ink,
            BackColor = Color.Transparent,
            Checked = current.ShowNotifications,
            Location = new Point(labelX, y),
        };

        y += 28;
        _notifyReset = new CheckBox
        {
            Text = "Show a notification when the session limit resets",
            AutoSize = true,
            ForeColor = Theme.Ink,
            BackColor = Color.Transparent,
            Checked = current.ShowResetNotifications,
            Location = new Point(labelX, y),
        };

        y += 28;
        _pin = new CheckBox
        {
            Text = "Always show the icon on the taskbar",
            AutoSize = true,
            ForeColor = Theme.Ink,
            BackColor = Color.Transparent,
            Checked = current.PinToTaskbar,
            Location = new Point(labelX, y),
        };

        y += 34;
        _status = new Label
        {
            Text = string.Empty,
            AutoSize = false,
            Location = new Point(PadX, y),
            Size = new Size(width, 40),
            ForeColor = Theme.InkSecondary,
            Font = Theme.Sans(8.75f),
        };

        Controls.Add(lblCookie);
        Controls.Add(help);
        Controls.Add(_cookie);
        Controls.Add(hint);
        Controls.Add(_test);
        Controls.Add(lblOrg);
        Controls.Add(_org);
        Controls.Add(lblPoll);
        Controls.Add(_poll);
        Controls.Add(lblWarn);
        Controls.Add(_warn);
        Controls.Add(_startup);
        Controls.Add(_notify);
        Controls.Add(_notifyReset);
        Controls.Add(_pin);
        Controls.Add(_status);
        Controls.Add(footer);
        Controls.Add(header);

        AcceptButton = _save;
        CancelButton = cancel;
    }

    private async Task TestAsync()
    {
        string cookie = _cookie.Text.Trim();
        if (cookie.Length == 0)
        {
            SetStatus("Enter a cookie first.", error: true);
            return;
        }

        _test.Enabled = false;
        _save.Enabled = false;
        SetStatus("Testing…", error: false);

        try
        {
            IReadOnlyList<ClaudeOrg> orgs = await _client.GetOrgsAsync(cookie);
            if (orgs.Count == 0)
            {
                SetStatus("Connected, but no organizations were found for this session.", error: true);
                return;
            }

            string? previousUuid = (_org.SelectedItem as ClaudeOrg)?.Uuid ?? _original.OrgUuid;
            _org.Items.Clear();
            int selectIndex = 0;
            for (int i = 0; i < orgs.Count; i++)
            {
                _org.Items.Add(orgs[i]);
                if (orgs[i].Uuid == previousUuid)
                    selectIndex = i;
            }
            _org.SelectedIndex = selectIndex;

            var chosen = (ClaudeOrg)_org.SelectedItem!;
            UsageSnapshot snapshot = await _client.GetUsageAsync(cookie, chosen.Uuid);
            if (snapshot.IsError)
            {
                SetStatus(snapshot.Error!, error: true);
                return;
            }

            int fiveHour = snapshot.TrayWindow?.Percent ?? 0;
            string plan = string.IsNullOrWhiteSpace(snapshot.PlanLabel) ? "" : $" · {snapshot.PlanLabel}";
            SetStatus($"Connected to {chosen.Name}{plan}. 5-hour session at {fiveHour}% ({snapshot.Windows.Count} windows in details).",
                error: false);
        }
        catch (Exception ex)
        {
            SetStatus("Test failed: " + ex.Message, error: true);
        }
        finally
        {
            _test.Enabled = true;
            _save.Enabled = true;
        }
    }

    private void OnSaveClicked(object? sender, EventArgs e)
    {
        var chosen = _org.SelectedItem as ClaudeOrg;
        Result = new AppSettings
        {
            Cookie = _cookie.Text.Trim(),
            OrgUuid = chosen?.Uuid ?? _original.OrgUuid,
            OrgName = chosen?.Name ?? _original.OrgName,
            KnownOrgs = _org.Items.Count > 0
                ? _org.Items.Cast<ClaudeOrg>().ToList()
                : new List<ClaudeOrg>(_original.KnownOrgs),
            PollSeconds = (int)_poll.Value,
            WarnThresholdPercent = (int)_warn.Value,
            StartWithWindows = _startup.Checked,
            ShowNotifications = _notify.Checked,
            ShowResetNotifications = _notifyReset.Checked,
            PinToTaskbar = _pin.Checked,
        };
    }

    private void SetStatus(string text, bool error)
    {
        _status.Text = text;
        _status.ForeColor = error ? Theme.Danger : Theme.Safe;
    }

    private static void ShowCookieHelp()
    {
        const string steps =
            "Recommended way — the Application/Storage tab (the cookie is NOT hidden here):\n\n" +
            "1. Open https://claude.ai in your browser and make sure you're logged in.\n" +
            "2. Press F12 to open Developer Tools.\n" +
            "3. Open the \"Application\" tab (in Firefox it's called \"Storage\").\n" +
            "4. On the left, expand \"Cookies\" and select \"https://claude.ai\".\n" +
            "5. Click the \"sessionKey\" row and copy its full Value — a long string that\n" +
            "   starts with \"sk-ant-sid\". Be sure to copy the WHOLE value.\n" +
            "6. Paste it into the box here and click \"Test connection\".\n\n" +
            "Why not the Network tab? If you use \"Copy as cURL\" / \"Copy as fetch\" there,\n" +
            "Chrome deliberately hides the cookie (you'll see credentials: \"omit\" and no\n" +
            "Cookie header). The cookie IS still sent by the browser — that's just privacy\n" +
            "redaction. Use the Application tab above instead. (You may also paste a full\n" +
            "copied cURL command here; the app will extract the cookie if it's included.)\n\n" +
            "The sessionKey expires periodically. If usage stops loading later, just copy a\n" +
            "fresh value the same way.\n\n" +
            "Your cookie is stored encrypted on this PC (Windows DPAPI) and is sent only to claude.ai.";

        MessageBox.Show(steps, "Getting your sessionKey cookie", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }
}
