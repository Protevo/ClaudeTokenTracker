# Release notes

## Claude Token Tracker 1.13.0

**June 2026** — multiple organizations: remember both accounts, switch in two clicks.

### Highlights

- **Two accounts under one e-mail are now first-class.** Both organizations on your
  claude.ai session are remembered, and you can switch the tracked one from the
  tray menu (**Organization: …**), a new dropdown in the details window, or Settings —
  no more re-running "Test connection" just to change org.
- **Instant switching.** The app caches the last numbers per org, so flipping back and
  forth shows data immediately while a fresh refresh runs in the background.
- **Org-aware alerts.** With more than one org, warnings and "tokens available again"
  alerts say which org they belong to — and a pending reset alert for one org still
  fires after you switch to the other. The tray tooltip and menu name the org too.

### Details

- The org list is fetched alongside every usage poll (no extra requests) and persisted,
  so the switchers work right after launch.
- Notification bookkeeping is keyed per org, so thresholds/resets never leak between orgs.
- Single-org accounts see no UI change.

### Requirements

Unchanged from prior releases:

- Windows 10 (1803+) or Windows 11 (x64)
- Claude.ai **Pro** or **Max** subscription and a valid session cookie
- For the portable build: no .NET install — use `release\ClaudeTokenTracker.exe` from `.\publish.ps1`

### Upgrade

1. Quit the running app (right-click tray icon → Exit).
2. Replace `ClaudeTokenTracker.exe` with the new build and launch it.
3. Settings are preserved. The second org appears in the switchers after the first
   successful refresh (or a "Test connection" in Settings).

### Build

```powershell
.\publish.ps1
```

Output: `release\ClaudeTokenTracker.exe` (self-contained single file, ~67 MB).

---

## Claude Token Tracker 1.12.0

**June 2026** — "Start with Windows" reliability fix.

### Highlights

- **"Start with Windows" now actually launches the app at sign-in.** If you'd ever
  turned the app off under **Task Manager → Startup** (or **Settings → Apps → Startup**),
  Windows kept ignoring it even though the option still looked enabled — and re-ticking
  the box did nothing. The toggle now reports its real state and re-enabling forces the
  entry back on.

### Details

- The app now reads and writes Windows' `StartupApproved` flag alongside the `Run` key, so
  the menu/Settings checkbox reflects the *effective* startup state.
- Turning "Start with Windows" on clears any leftover "disabled" flag; turning it off
  removes both registry entries for a clean slate. Still per-user, no admin rights.
- Verified that the startup path resolves correctly for the single-file portable build.

### Requirements

Unchanged from prior releases:

- Windows 10 (1803+) or Windows 11 (x64)
- Claude.ai **Pro** or **Max** subscription and a valid session cookie
- For the portable build: no .NET install — use `release\ClaudeTokenTracker.exe` from `.\publish.ps1`

### Upgrade

1. Quit the running app (right-click tray icon → Exit, or close from Task Manager if needed).
2. Replace `ClaudeTokenTracker.exe` with the new build.
3. Launch again — settings and your encrypted cookie in `%APPDATA%\ClaudeTokenTracker` are preserved.
4. If "Start with Windows" wasn't working before, open the tray menu and toggle it **on** once to repair the entry.

### Build

```powershell
.\publish.ps1
```

Output: `release\ClaudeTokenTracker.exe` (self-contained single file, ~67 MB).

---

## Claude Token Tracker 1.11.0

- **Extra usage shown in dollars** — the "Extra usage" subtitle divides the API's cents values by 100 and formats as USD (e.g. `$17.20 / $50.00`) to match your claude.ai account. Accepts `monthly_credit_limit` as an alias for `monthly_limit`, and shows "used (no cap)" when only spend is reported.

## Claude Token Tracker 1.10.0

- **Separate toggle for reset notifications** — "limit reset / tokens available again" alerts now have their own setting (default on), independent of the threshold warnings. Existing users keep prior behavior via a one-time migration from the old combined notifications toggle.

## Claude Token Tracker 1.9.0

- **Tray reflects the 5-hour session only** — metered `extra_usage` credits are no longer mixed into the tray peak/icon; they stay informational in the details subtitle. The tray icon, tooltip, menu readout, and alerts all use the `five_hour` window. The details window still lists every rolling window.

## Claude Token Tracker 1.8.0

- **Fixes HTTP 0 on Test connection / polling** — `curl` now uses `ssl-no-revoke`, so Schannel no longer aborts TLS with `CRYPT_E_NO_REVOCATION_CHECK` on networks that can't reach OCSP/CRL revocation servers. Clearer message if a request still returns HTTP 0.
- **Portable build hardening** — `dotnet build -c Release` no longer emits a misleading ~150 KB framework-dependent exe; publish settings are mirrored in the `.csproj`, `publish.ps1` fails if the output is under 40 MB, ReadyToRun is disabled for broader CPU compatibility, and a `release.yml` workflow builds the portable asset on tag push.

## Claude Token Tracker 1.7.0

**June 2026** — clearer usage details and a more reliable header.

### Highlights

- **Circular progress rings** in the details window replace the old pill bar. Each usage window shows a compact ring with the percentage inside, plus the window name and reset time beside it — easier to read at a glance and no more awkward split end caps on the old bar.
- **Extra usage always visible** — plan and organization stay on the first line of the header; credit-based overage (`Extra usage: …`) moves to its own line below. Dollar amounts match claude.ai (API values are cents ÷ 100, e.g. `$17.20 / $50.00`). Long values wrap inside the window instead of clipping off the right when they update on refresh.

### Details

- New `UsageRing` control: coloured arc on a neutral track, same semantic colour ramp as the tray icon (olive → ochre → clay → brick).
- Usage rows are slightly shorter (58 px) with ring + text laid out on one horizontal line.
- Header subtitle uses fixed width and word-wrap (`TextRenderer`) so dynamic extra-usage text reflows correctly on every refresh and resize.

### Requirements

Unchanged from prior releases:

- Windows 10 (1803+) or Windows 11 (x64)
- Claude.ai **Pro** or **Max** subscription and a valid session cookie
- For the portable build: no .NET install — use `release\ClaudeTokenTracker.exe` from `.\publish.ps1` (~63–67 MB). Do **not** copy the ~150 KB exe from `bin\` after `dotnet build`.

### Upgrade

1. Quit the running app (right-click tray icon → Exit, or close from Task Manager if needed).
2. Replace `ClaudeTokenTracker.exe` with the new build.
3. Launch again — settings and your encrypted cookie in `%APPDATA%\ClaudeTokenTracker` are preserved.

### Build

```powershell
.\publish.ps1
```

Output: `release\ClaudeTokenTracker.exe` (self-contained single file, ~67 MB).

---

## Claude Token Tracker 1.6.0

- **Reset datetime** in each detail row (e.g. `resets in 2h 30m · 6:30 PM`), not just a countdown.
- **“Tokens available again”** balloon when a maxed-out window’s reset time passes (30 s timer + poll; respects the notifications toggle).
- `IsLimited` flag on usage windows for consistent limit detection.

## Claude Token Tracker 1.5.0

- **Single-file release** — `publish.ps1` and `SingleFile-win-x64` publish profile; self-contained ~67 MB exe, no .NET runtime required on target PCs.

## Claude Token Tracker 1.4.0

- **UI redesign** — Anthropic-inspired palette (ivory canvas, clay accent, serif headings), `Theme` + `FlatButton`, refined Settings and Usage windows.
- Footer layout fix on the Usage window; initial subtitle wrapping for long headers.

## Claude Token Tracker 1.3.0

- **Pin to taskbar** — “Always show icon on taskbar” keeps the tray icon out of the Windows 11 overflow flyout (default on).

## Earlier (1.0–1.2)

- System-tray peak utilization with colour-coded icon and per-window menu.
- Cloudflare-safe HTTP via built-in `curl.exe`; correct 0–100% utilization scaling.
- DPAPI-encrypted cookie storage, threshold notifications, start-with-Windows, details window, unofficial claude.ai usage API.

---

*Claude Token Tracker is unofficial and not affiliated with Anthropic.*
