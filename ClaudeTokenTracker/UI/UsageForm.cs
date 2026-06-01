using System.Diagnostics;
using System.Drawing;
using ClaudeTokenTracker.Models;

namespace ClaudeTokenTracker.UI;

/// <summary>
/// The detail window: shows every usage window with a coloured bar and reset time.
/// Refreshing is delegated to the owner via <see cref="RefreshRequested"/>.
/// </summary>
public sealed class UsageForm : Form
{
    private const int PadX = 20;

    private readonly Label _title;
    private readonly Label _subtitle;
    private readonly Panel _header;
    private readonly Panel _list;
    private readonly Panel _footer;
    private readonly Label _status;
    private readonly FlatButton _refresh;
    private readonly LinkLabel _openSite;

    // Guards layout work that touches child controls from firing during base
    // construction (e.g. the ClientSize assignment below raises OnResize before
    // the header/subtitle fields are assigned).
    private bool _initialized;

    public event EventHandler? RefreshRequested;

    public UsageForm(Icon? icon)
    {
        Text = "Claude Usage";
        if (icon is not null)
            Icon = icon;
        StartPosition = FormStartPosition.Manual;
        FormBorderStyle = FormBorderStyle.Sizable;
        MinimizeBox = false;
        MaximizeBox = false;
        ShowInTaskbar = false;
        BackColor = Theme.Canvas;
        ForeColor = Theme.Ink;
        Font = Theme.Sans(9f);
        ClientSize = new Size(420, 500);
        MinimumSize = new Size(380, 360);

        _header = new Panel
        {
            Dock = DockStyle.Top,
            Height = 74,
            BackColor = Theme.Canvas,
        };
        _header.Paint += (s, e) => Theme.Hairline((Control)s!, e, top: false);

        _title = new Label
        {
            Text = "Claude Usage",
            AutoSize = true,
            Font = Theme.Serif(16.5f),
            ForeColor = Theme.Ink,
            BackColor = Color.Transparent,
            Location = new Point(PadX, 18),
        };
        _subtitle = new Label
        {
            Text = "—",
            AutoSize = true,
            Font = Theme.Sans(9f),
            ForeColor = Theme.InkMuted,
            BackColor = Color.Transparent,
            Location = new Point(PadX + 1, 48),
        };
        _header.Controls.Add(_subtitle);
        _header.Controls.Add(_title);

        _footer = new Panel
        {
            Dock = DockStyle.Bottom,
            Height = 60,
            BackColor = Theme.Canvas,
        };
        _footer.Paint += (s, e) => Theme.Hairline((Control)s!, e, top: true);

        _refresh = new FlatButton
        {
            Text = "Refresh",
            Variant = FlatButton.ButtonVariant.Primary,
            Size = new Size(108, 34),
        };
        _refresh.Click += (_, _) => RefreshRequested?.Invoke(this, EventArgs.Empty);

        _openSite = new LinkLabel
        {
            Text = "Open usage page",
            AutoSize = true,
            Font = Theme.Sans(9f),
            LinkColor = Theme.Accent,
            ActiveLinkColor = Theme.AccentPressed,
            VisitedLinkColor = Theme.Accent,
            LinkBehavior = LinkBehavior.HoverUnderline,
            BackColor = Color.Transparent,
        };
        _openSite.LinkClicked += (_, _) => OpenUrl("https://claude.ai/settings/usage");

        _status = new Label
        {
            Text = string.Empty,
            AutoSize = false,
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = Theme.InkMuted,
            BackColor = Color.Transparent,
            Font = Theme.Sans(8.5f),
        };

        _footer.Controls.Add(_status);
        _footer.Controls.Add(_openSite);
        _footer.Controls.Add(_refresh);
        // Position from the footer's real width (it starts at the default panel size and
        // only grows to the form width once docked) so the action bar tracks resizes.
        _footer.Layout += (_, _) => LayoutFooter();

        _list = new Panel
        {
            Dock = DockStyle.Fill,
            AutoScroll = true,
            BackColor = Theme.Canvas,
            Padding = new Padding(0, 6, 0, 6),
        };

        // Order matters: Fill must be added before the docked bars for correct layout.
        Controls.Add(_list);
        Controls.Add(_footer);
        Controls.Add(_header);

        _initialized = true;
        LayoutHeader();
    }

    /// <summary>
    /// Sizes the subtitle to the window width so a long "plan · org · extra usage"
    /// line wraps onto a second line instead of running off the right edge — which
    /// previously clipped the trailing extra-usage segment. The header band grows to
    /// fit the wrapped text so nothing is hidden behind the list.
    /// </summary>
    private void LayoutHeader()
    {
        if (!_initialized)
            return;

        int avail = Math.Max(80, ClientSize.Width - (PadX + 1) - PadX);
        _subtitle.MaximumSize = new Size(avail, 0);

        // Bottom padding chosen so a single-line subtitle keeps the original 74px
        // header; the band only grows once the text wraps to a second line.
        int subtitleHeight = _subtitle.GetPreferredSize(new Size(avail, 0)).Height;
        _header.Height = Math.Max(74, _subtitle.Top + subtitleHeight + 11);
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        LayoutHeader();
    }

    private void LayoutFooter()
    {
        int w = _footer.ClientSize.Width;
        int h = _footer.ClientSize.Height;
        if (w <= 0)
            return;

        _refresh.Location = new Point(w - PadX - _refresh.Width, (h - _refresh.Height) / 2);

        Size linkSize = _openSite.PreferredSize;
        _openSite.Location = new Point(_refresh.Left - 18 - linkSize.Width, (h - linkSize.Height) / 2);

        _status.Bounds = new Rectangle(PadX, 0, Math.Max(60, _openSite.Left - PadX - 12), h);
    }

    public void SetBusy(bool busy)
    {
        _refresh.Enabled = !busy;
        _refresh.Text = busy ? "Refreshing…" : "Refresh";
    }

    public void UpdateSnapshot(UsageSnapshot snapshot)
    {
        _list.SuspendLayout();
        _list.Controls.Clear();

        if (snapshot.IsError)
        {
            _subtitle.Text = "Not connected";
            LayoutHeader();
            _list.Controls.Add(new Label
            {
                Text = snapshot.Error,
                Dock = DockStyle.Top,
                AutoSize = false,
                Height = 120,
                Padding = new Padding(PadX, 18, PadX, 18),
                ForeColor = Theme.Danger,
                BackColor = Color.Transparent,
                Font = Theme.Sans(9.5f),
            });
            _status.Text = "Failed " + snapshot.RetrievedAt.ToString("HH:mm:ss");
            _list.ResumeLayout();
            return;
        }

        // Dock=Top stacks by reverse insertion order, so add bottom rows first.
        for (int i = snapshot.Windows.Count - 1; i >= 0; i--)
        {
            var row = new UsageRow(snapshot.Windows[i])
            {
                Dock = DockStyle.Top,
            };
            _list.Controls.Add(row);
        }

        if (snapshot.Windows.Count == 0)
        {
            _list.Controls.Add(new Label
            {
                Text = "No usage windows returned. If you're on the free plan there is nothing to meter.",
                Dock = DockStyle.Top,
                AutoSize = false,
                Height = 80,
                Padding = new Padding(PadX, 16, PadX, 16),
                ForeColor = Theme.InkMuted,
                BackColor = Color.Transparent,
                Font = Theme.Sans(9.5f),
            });
        }

        _subtitle.Text = BuildSubtitle(snapshot);
        LayoutHeader();
        _status.Text = "Updated " + snapshot.RetrievedAt.ToString("HH:mm:ss");
        _list.ResumeLayout();
    }

    private static string BuildSubtitle(UsageSnapshot s)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(s.PlanLabel)) parts.Add(s.PlanLabel!);
        if (!string.IsNullOrWhiteSpace(s.OrgName)) parts.Add(s.OrgName!);
        if (!string.IsNullOrWhiteSpace(s.ExtraUsageLabel)) parts.Add(s.ExtraUsageLabel!);
        return parts.Count == 0 ? "Connected" : string.Join("   ·   ", parts);
    }

    public static void OpenUrl(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch
        {
            // Ignore: opening a browser is best-effort.
        }
    }

    /// <summary>Position the window near the tray (bottom-right) before first show.</summary>
    public void PositionNearTray()
    {
        Rectangle wa = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1280, 720);
        Location = new Point(wa.Right - Width - 12, wa.Bottom - Height - 12);
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        // Keep the instance alive; the tray just hides it so state/position persist.
        if (e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            Hide();
            return;
        }
        base.OnFormClosing(e);
    }
}
