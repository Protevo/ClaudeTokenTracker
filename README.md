# Claude Token Tracker

A lightweight **Windows system-tray app** that tracks your **Claude.ai (Pro / Max) usage limits**.
It sits quietly in the notification area and shows, at a glance, how close you are to hitting
your rolling usage caps — the same numbers you'd see on
[claude.ai/settings/usage](https://claude.ai/settings/usage).

The tray icon shows your **peak utilization** as a colored number:

| Color | Meaning |
|-------|---------|
| 🟢 Green | under 50% |
| 🟡 Amber | 50–79% |
| 🟠 Orange | 80–94% |
| 🔴 Red | 95%+ (about to be rate-limited) |
| ⚪ Gray `?` | not connected / error |

Right-click the icon for a per-window breakdown, or double-click to open the details window
with live progress bars and reset countdowns.

---

## How it works (and an important caveat)

Claude.ai **does not have an official public usage API**. The Settings → Usage page in the web
app reads from a private, undocumented endpoint:

```
GET https://claude.ai/api/organizations/{org_uuid}/usage
```

This app calls that same endpoint using **your own claude.ai session cookie**. It returns a
utilization fraction (0–100%) and a reset time for several rolling windows — for example
`five_hour`, `seven_day`, `seven_day_sonnet`, `seven_day_opus`. You get **rate-limited when any
one** of these hits 100%, so the app surfaces the peak across all of them.

> ⚠️ Because the endpoint is unofficial, Anthropic could change or remove it at any time. If usage
> stops loading after a Claude update, that's the likely cause. The app fails gracefully (gray `?`
> icon) rather than crashing.

### Why it uses `curl.exe`

claude.ai sits behind **Cloudflare bot management**, which fingerprints .NET's TLS/HTTP stack and
answers every request from `HttpClient` with a "Just a moment…" challenge (`cf-mitigated: challenge`).
The `curl.exe` that ships with Windows passes that same check, so the app shells out to it as its
HTTP transport (the cookie is passed via a short-lived curl config file, never on the command line).
This needs no extra dependencies — `curl.exe` is built into Windows 10 (1803+) and Windows 11.

Note: usage `utilization` from the API is a **percentage (0–100)**, e.g. `20.0` = 20% — verified
against a live account. The plan name (e.g. "Max 5x") is derived from the org's rate-limit tier.

This is **not** the API billing/cost data from `console.anthropic.com` — that's a separate product
with its own [Admin API](https://platform.claude.com/docs/en/manage-claude/rate-limits-api). This
app is specifically for **consumer Pro/Max plan** limits.

---

## Requirements

- Windows 10 (1803+) / 11 (x64) — includes the built-in `curl.exe` the app uses for requests.
- **.NET runtime:** *nothing to install* if you run the self-contained `release\ClaudeTokenTracker.exe`
  (it bundles the runtime). The lean framework-dependent build instead needs the
  [.NET 8 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0); *building* from source
  needs the .NET 8 SDK.
- An active **Claude.ai Pro or Max** subscription (Free accounts have no quota to meter).

> **Minimal by design:** the project uses **zero NuGet packages** — only the .NET 8 Windows
> Desktop SDK. DPAPI encryption is done via direct P/Invoke, so no package restore (and no NuGet
> source) is needed to build.

---

## Build & run

From the repo root:

```bash
cd ClaudeTokenTracker
dotnet build -c Release
dotnet run -c Release
```

### Produce a distributable .exe

**Recommended for sharing — one self-contained file, no .NET install needed.** From the repo
root, run the release script:

```powershell
.\publish.ps1
```

It produces a single **`release\ClaudeTokenTracker.exe`** (~67 MB) that bundles the .NET 8 runtime,
so it runs on any **Windows 10 (1803+) / 11 x64** PC with nothing pre-installed — just hand someone
that one file and they double-click it. (The script locates the .NET SDK automatically even if
`dotnet` isn't on your `PATH`.)

Equivalent manual command:

```powershell
dotnet publish ClaudeTokenTracker\ClaudeTokenTracker.csproj -p:PublishProfile=SingleFile-win-x64 -o release
```

In Visual Studio you can instead right-click the project → **Publish** → **SingleFile-win-x64**. All
the packaging settings live in `ClaudeTokenTracker/Properties/PublishProfiles/SingleFile-win-x64.pubxml`.

> The single-file build **downloads the .NET 8 runtime packs from NuGet** the first time (the repo's
> `NuGet.config` points at nuget.org for exactly this). The app still ships **zero NuGet *package*
> dependencies** — only the runtime itself is bundled. Trimming is intentionally left off because
> WinForms relies on reflection.

<details>
<summary>Alternative: lean framework-dependent build (~230 KB, but needs the .NET 8 Desktop Runtime)</summary>

If the target PC already has the [.NET 8 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0),
this is the smallest option and downloads nothing extra:

```bash
cd ClaudeTokenTracker
dotnet publish -c Release -o publish
```

Output lands in `ClaudeTokenTracker/publish/` (`ClaudeTokenTracker.exe` + a small DLL). Copy that
folder anywhere on a PC that has the runtime and double-click the `.exe`.

</details>

---

## First-time setup

On first launch the app opens **Settings** automatically (there's no cookie yet).

1. Open [claude.ai/settings/usage](https://claude.ai/settings/usage) in your browser, logged in.
2. Press **F12** → **Network** tab → reload the page (**F5**).
3. Click the request named **`usage`**, find the **`Cookie:`** request header, and copy its value.
   - *Or:* in **Application → Cookies → https://claude.ai**, copy just the **`sessionKey`** value.
4. Paste it into the **session cookie** box in Settings.
5. Click **Test connection** — it should report your org, plan, and peak usage.
6. Adjust the refresh interval / warning threshold if you like, then **Save**.

> 💡 Pasting the *full* Cookie header (which includes `cf_clearance`) is the most reliable, because
> it satisfies Cloudflare. Just the `sessionKey` usually works too.

The session cookie typically lasts a while but **will eventually expire**. When it does, the icon
turns gray and the menu shows a "session rejected" message — just repeat the steps above to refresh it.

---

## Features

- **Tray icon** with live, color-coded peak-utilization percentage.
- **Right-click menu**: per-window readouts (5-hour, 7-day, per-model…), refresh, open usage page.
- **Details window**: progress bars + "resets in …" countdowns for every window.
- **Desktop notifications** when any window crosses your warning threshold (default 80%).
- **Auto-refresh** on a configurable interval (default 60s; minimum 15s).
- **Start with Windows** toggle (per-user, no admin needed).
- **Pin to taskbar** — keeps the icon always visible instead of hidden in the Windows 11 "⌃"
  overflow flyout, so you can check usage with a glance (on by default; toggle in the menu/Settings).
- **Single instance** — won't stack multiple icons.

---

## Privacy & security

- Your cookie is stored **encrypted at rest** using **Windows DPAPI** (current-user scope) in
  `%APPDATA%\ClaudeTokenTracker\settings.json`. It can only be decrypted by *you* on *this* PC.
- The cookie is sent **only** to `https://claude.ai`. There is no telemetry and no other network
  access.
- To wipe everything, delete the `%APPDATA%\ClaudeTokenTracker` folder and turn off
  "Start with Windows".

---

## Project layout

```
ClaudeTokenTracker/
├─ Program.cs                       # entry point, single-instance guard
├─ App/
│  ├─ TrayApplicationContext.cs     # tray icon, menu, polling loop, notifications
│  └─ ErrorReporter.cs              # last-resort exception logging
├─ Services/
│  ├─ ClaudeUsageClient.cs          # calls claude.ai's private usage endpoints
│  ├─ SettingsStore.cs              # load/save settings (DPAPI-encrypted cookie)
│  ├─ DataProtection.cs             # DPAPI via P/Invoke (no NuGet dependency)
│  ├─ StartupManager.cs             # "start with Windows" registry toggle
│  ├─ TaskbarPinner.cs              # keeps the tray icon always visible (Win11 IsPromoted)
│  └─ TrayIconRenderer.cs           # draws the % icon at runtime
├─ UI/
│  ├─ SettingsForm.cs               # cookie entry + test connection
│  ├─ UsageForm.cs                  # details window
│  ├─ UsageRow.cs / UsageBar.cs     # row + colored progress-bar controls
└─ Models/                          # AppSettings, UsageWindow, UsageSnapshot, …
```

No external NuGet packages — just the .NET 8 Windows Desktop SDK.

---

## Disclaimer

This is an unofficial tool, not affiliated with or endorsed by Anthropic. It only reads usage data
that your own logged-in browser session can already see. Use it in accordance with Anthropic's
terms of service.
