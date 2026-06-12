# Claude Token Tracker

A small Windows app that lives in your notification area (system tray) and shows how close you are to your **Claude.ai Pro or Max** usage limits — the same rolling caps you see on [claude.ai/settings/usage](https://claude.ai/settings/usage).

You get a color-coded **percentage on the tray icon**, a quick breakdown in the right-click menu, and a details window with progress rings and reset times. No need to keep the usage page open in your browser.

---

## What you need

| Requirement | Details |
|-------------|---------|
| **Windows** | Windows 10 (version 1803 or newer) or Windows 11, **64-bit** |
| **Claude plan** | Active **Pro** or **Max** on claude.ai (Free accounts have nothing to meter) |
| **Browser session** | A valid claude.ai login — you will copy your session cookie once during setup |

The recommended download is a **single `.exe` file** (~67 MB). It bundles the .NET 8 runtime; you do **not** need to install .NET separately.

**Important:** the portable exe is only the one built with `.\publish.ps1` (or downloaded from [Releases](https://github.com/Protevo/ClaudeTokenTracker/releases)). A ~150 KB `ClaudeTokenTracker.exe` from `dotnet build` is **not** portable — it requires .NET 8 on that PC.

---

## Install

1. Open **[Releases](https://github.com/Protevo/ClaudeTokenTracker/releases)** on GitHub and download the latest **`ClaudeTokenTracker.exe`** (or build it yourself — see [For developers](#for-developers) below).
2. Save the file anywhere you like (for example `Downloads` or `C:\Tools\ClaudeTokenTracker\`).
3. **Double-click** `ClaudeTokenTracker.exe` to start it.

On first run, **Settings** opens automatically because the app is not connected yet. Only one copy can run at a time; starting it again just brings the existing instance forward.

Windows may show SmartScreen for an unsigned app. If you trust the source, choose **More info → Run anyway**.

---

## First-time setup

You need to give the app the same session your browser uses on claude.ai. The cookie is stored **encrypted on your PC** and is only sent to `https://claude.ai`.

### Step 1 — Get your session cookie

**Cookies tab**

1. In Chrome or Edge, open [claude.ai/settings/usage](https://claude.ai/settings/usage) while logged in.
2. Press **F12** to open Developer Tools.
3. Go to **Application** (Chrome) or **Storage** (Edge) → **Cookies** → `https://claude.ai`.
4. Find **`sessionKey`**, copy its **Value**, and paste that into the app’s **Session cookie** box.

5. Paste that into the **Session cookie** box in the app.

Pasting the **full** cookie header (including `cf_clearance` if present) is the most reliable option. A bare `sessionKey` often works too.

### Step 2 — Test and save

1. In the app’s **Settings** window, paste the cookie into **Session cookie**.
2. Click **Test connection**. You should see your organization, plan (for example “Max 5x”), and current usage.
3. If you belong to more than one organization, pick the correct one from the dropdown.
4. Optionally change **Refresh interval**, **Warn at %**, or the checkboxes (see [Settings](#settings) below).
5. Click **Save**.

The tray icon should change from a gray **?** to a colored number within about a minute (or immediately if you use **Refresh now** from the menu).

---

## Reading the tray icon

The icon shows your **5-hour session** utilization only. Other rolling limits (7-day, per-model caps) and **extra usage** credits appear in the details window, not in the tray total.

| Color | Meaning |
|-------|---------|
| Green | Under 50% |
| Amber | 50–79% |
| Orange | 80–94% |
| Red | 95% or higher — close to the limit |
| Gray **?** | Not connected, expired cookie, or an error |

Hover the icon for the 5-hour percentage and reset countdown. **Right-click** for the same readout in the menu; **double-click** to open the full details window (all windows + extra usage).

---

## Everyday use

### Right-click menu

| Item | What it does |
|------|----------------|
| *(top lines)* | Live readout for each usage window (name, %, time until reset) |
| **Show details…** | Opens the usage window with progress rings and reset times |
| **Refresh now** | Fetches the latest numbers immediately |
| **Open claude.ai usage page** | Opens the official usage page in your browser |
| **Organization: …** | Quick switcher between your orgs/accounts — only shown if your session has more than one |
| **Settings…** | Cookie, organization, refresh interval, alerts |
| **Start with Windows** | Launch the app when you sign in (no admin rights needed) |
| **Always show icon on taskbar** | Keeps the icon visible on Windows 11 instead of hiding it under **⌃** (on by default) |
| **Exit** | Quits the app completely |

### Details window

Double-click the tray icon (or choose **Show details…**) to see:

- Your **plan**, **organization**, and **extra usage** credits (if your account has them)
- One **ring gauge** per usage window, with the percentage inside
- **When each window resets** (countdown plus clock time, for example `resets in 2h 30m · 6:30 PM`)
- An **organization dropdown** in the top-right corner (only if you have several — see below)
- **Refresh** and a link to the claude.ai usage page

Closing the details window only **hides** it; the app keeps running in the tray.

### Two accounts / organizations

If you have **two accounts under the same e-mail** (for example personal + work), they show up as two **organizations** on the same claude.ai session, and the app remembers both. To switch which one is tracked:

- **Right-click the tray icon** → **Organization: …** → pick the other org, or
- use the **dropdown in the details window**, or the **Organization** list in Settings.

Switching is instant when the other org was fetched before (a fresh refresh still follows), and the icon, tooltip, menu, and alerts all follow the selected org. With several orgs, notifications and the menu readout include the org name, and a "tokens available again" alert for one org still arrives while you track the other.

---

## Settings

Open **Settings…** from the tray menu.

| Setting | Default | Purpose |
|---------|---------|---------|
| **Session cookie** | *(empty)* | Your claude.ai login — required |
| **Organization** | First org found | Which org’s usage to track — all orgs your session can see are listed (also switchable from the tray menu and details window) |
| **Refresh interval** | 60 seconds | How often to poll (minimum 15 seconds) |
| **Warn at %** | 80% | Desktop notification when any window crosses this level |
| **Start with Windows** | Off | Run at sign-in |
| **Warn when limit is nearly reached** | On | Desktop notification when usage crosses **Warn at %** |
| **Notify when session limit resets** | On | “Tokens available again” alert when a maxed window resets |
| **Always show icon on taskbar** | On | Pin the tray icon so it stays visible on Windows 11 |

Use **Test connection** before **Save** whenever you paste a new cookie.

---

## Notifications

With alerts enabled (defaults):

- You get a **warning** when any usage window crosses your **Warn at %** threshold (default 80%) — toggle in Settings.
- When a window was **maxed out** and its reset time passes, you get a **“tokens available again”** alert even if the next scheduled refresh is still a while away — separate toggle in Settings.

Turn either off in Settings if you only want the tray icon.

---

## Troubleshooting

### Gray **?** icon or “Not connected”

- Your **session cookie expired** — repeat [First-time setup](#first-time-setup) and click **Save**.
- **Test connection** failed — read the error text; “account_session_invalid” means you need a fresh cookie.
- You are on the **Free** plan — this app only tracks Pro/Max quotas.
- **No internet** or claude.ai is down — try **Refresh now** or open the usage page in your browser.

### Numbers look wrong or stopped updating

- Click **Refresh now** or open **Show details…** and press **Refresh**.
- Anthropic may have changed their private usage API (this app is unofficial). Check [Releases](https://github.com/Protevo/ClaudeTokenTracker/releases) for an updated build.

### Icon hidden on Windows 11

- Enable **Always show icon on taskbar** in the tray menu or Settings.
- Or click **⌃** in the taskbar corner and drag the icon out to the visible area.

### “Already running” when you start the exe

- The app is already in the tray. Look for the colored number (or **?**) near the clock, or end it from Task Manager and start again.

### Windows asks to install .NET, or the exe only works after rebuilding on that PC

- You likely copied the **wrong exe**. The portable build is **~67 MB**; a **~150 KB** file from `bin\Release\` is a dev build that needs [.NET 8 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0) installed.
- On the build machine, run `.\publish.ps1` and copy **`release\ClaudeTokenTracker.exe`** (or use the GitHub Release asset of the same size).
- Target PC must be **64-bit Windows 10 (1803+)** or **Windows 11** (not ARM-only tablets unless x64 emulation is available).

### Cookie help inside the app

Settings includes a **“How do I get this?”** link with the same instructions as above.

---

## Privacy

- Your cookie is saved under `%APPDATA%\ClaudeTokenTracker\settings.json`, **encrypted with Windows DPAPI** (only your Windows user on this PC can decrypt it).
- Data is sent **only** to `https://claude.ai`. There is no telemetry and no other servers.
- To remove everything: quit the app, delete the folder `%APPDATA%\ClaudeTokenTracker`, and turn off **Start with Windows** if you enabled it.

---

## What this app is not

- It does **not** show API billing or team usage from [console.anthropic.com](https://console.anthropic.com) — only **consumer Pro/Max** limits on claude.ai.
- It is **not** an official Anthropic product. It reads the same usage data your logged-in browser can already see, via an undocumented endpoint that could change in the future.

---

## Disclaimer

Unofficial tool, not affiliated with or endorsed by Anthropic. Use in line with [Anthropic’s terms of service](https://www.anthropic.com/legal/consumer-terms).

---

## For developers

Source code, build steps, and release packaging are documented for contributors:

```powershell
# Build and run from source (requires .NET 8 SDK on this machine only)
cd ClaudeTokenTracker
dotnet run -c Release

# Portable exe for other PCs — no .NET install needed on the target machine
cd ..
.\publish.ps1
# → release\ClaudeTokenTracker.exe  (~67 MB; verify size before copying)
```

See [PROGRESS.md](PROGRESS.md) for implementation notes and [RELEASE_NOTES.md](RELEASE_NOTES.md) for version history.
