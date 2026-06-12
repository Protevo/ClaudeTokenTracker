# Progress

## Project: Claude Token Tracker (Windows system-tray app)

Tracks **Claude.ai Pro/Max** rolling usage limits via claude.ai's private
`/api/organizations/{org}/usage` endpoint, authenticated with the user's session cookie.

Stack: **C# / .NET 8 WinForms** (`net8.0-windows`), no external NuGet packages.

---

## Status: v1.13 — multi-org support: remember every org, switch in two clicks

### v1.13 — track two accounts (orgs) under one e-mail and switch easily
- **Use case:** two claude.ai accounts under the same e-mail = two organizations visible to
  the **same session cookie** (`/api/organizations` lists both). Previously only the active
  org was persisted and switching meant Settings → Test connection → pick → Save.
- **`AppSettings.KnownOrgs`** (new) — every org the session can see, persisted to
  settings.json so the switchers work right after launch. Refreshed on each successful poll
  and by "Test connection"; `Clone()` copies the list. `UsageSnapshot.Orgs` (new) carries
  the live org list with each fetch — the client already listed orgs every poll, so this
  adds **zero extra HTTP calls**.
- **Tray menu switcher** — new "Organization: {name}" submenu (above Settings…) listing all
  known orgs with the active one checked; hidden for single-org sessions. Clicking an org
  persists it and refreshes.
- **Details-window switcher** — `UsageForm` header got a top-right org dropdown (visible only
  with ≥2 orgs). It raises `OrgSwitchRequested`; the tray context performs the switch. The
  combo is positioned in `LayoutHeader()` (not via `Anchor` — the header only grows to form
  width once docked, the same pitfall fixed for the footer in v1.4) and is only rebuilt when
  the org list actually changes, so an open dropdown isn't snapped shut by a poll landing.
  Selection events are deferred via `BeginInvoke` because the switch path repopulates the
  combo itself. Verified with off-screen renders at 420/380 px (temporary harness, removed).
- **Instant switching** — snapshots are cached per org uuid; switching shows the cached data
  immediately (truthful `RetrievedAt` in the footer) while a fresh refresh runs. If an org
  was never fetched, the icon shows gray + "Switching to {org}…" until data lands. A switch
  while a request is **in flight** is handled: the stale result is cached but not displayed,
  and a follow-up refresh for the new org fires (`switchedMidFlight` in `RefreshAsync`).
- **Org-aware alerts** — `_notifiedKeys` / `_limitWatches` keys are now `{orgUuid}|{window}`
  so thresholds/resets never cross orgs; limit watches survive switching (max out org A,
  switch to B, still get "A's tokens are available again"). With >1 known org, balloons,
  the menu header ("Personal · 5-hour: 20%") and the tooltip name the org; single-org users
  see no change. `BuildTrayTooltip` clamps to NotifyIcon's 127-char cap.
- **Org sync** — `SyncOrgSettings` replaces the old auto-resolve block: persists the org list
  when it changes and adopts the actually-queried org when the saved uuid was stale/blank
  (settings writes only when something changed). Changing the cookie in Settings clears the
  per-org snapshot cache and limit watches (a different login may see different orgs);
  picking a different org in Settings applies its cached snapshot immediately.
- **SettingsForm** — org dropdown is seeded from `KnownOrgs` (no Test connection needed just
  to switch); saving persists the dropdown's org list.
- **Verification:** `dotnet build -c Release` → **0 warnings / 0 errors**; UsageForm rendered
  off-screen at 420/380 px with a fake 2-org snapshot to confirm the header layout. Version
  bumped to **1.13.0**.

---

## Status: v1.12 — "Start with Windows" now actually launches (StartupApproved fix)

### v1.12 — respect the Task Manager / Settings startup toggle
- **Bug:** "Start with Windows" could be checked yet the app never launched at sign-in.
  Root cause is Windows' **StartupApproved** mechanism: when an autorun is disabled via
  **Task Manager → Startup** or **Settings → Apps → Startup**, Windows leaves the
  `HKCU\…\Run` value in place but records a "disabled" flag under
  `…\Explorer\StartupApproved\Run` and silently ignores the Run entry. The old
  `StartupManager.IsEnabled()` only looked at the Run key, so it reported **enabled**
  while Windows refused to start the app — and because `ShowSettings` only calls
  `SetEnabled` when the checkbox differs from `IsEnabled()`, re-checking the box was a
  **no-op**, leaving the user with no in-app way to fix it.
- **Reproduced** with the real code path (temporary `--startup-*` diag in `Program.cs`):
  wrote a disabled flag (`03 00 …`) → `IsEnabled()` still returned `True` (the bug).
  Confirmed `Environment.ProcessPath` resolves correctly for both the dev apphost and the
  **single-file** release exe, and that the Run write/format were otherwise fine.
- **Fix — `Services/StartupManager.cs`:**
  - `IsEnabled()` now returns the **effective** state: Run value present **and** not
    disabled in StartupApproved (flag absent, or its first byte even). So the menu /
    Settings checkbox tells the truth.
  - `SetEnabled(true)` writes the Run value **and clears** our StartupApproved entry
    (absent = enabled), so re-enabling from the app overrides a prior Task Manager disable
    and the entry is honoured at next sign-in.
  - `SetEnabled(false)` removes the Run value and the StartupApproved entry (clean slate).
  - All registry access stays per-user (HKCU), no admin rights, no new dependencies.
- **Verification:** built (0 warnings / 0 errors) and exercised every transition via the
  real registry: disabled-flag → `IsEnabled=False`; re-enable → flag cleared, Run present,
  `IsEnabled=True`; disable → both keys removed. Machine left in a clean state; the
  temporary diagnostic was removed before final build. Version bumped to **1.12.0**.

---

## Status: v1.11 — extra usage label shows dollars (cents ÷ 100)

### v1.11 — extra usage matches claude.ai account display
- **Problem:** `extra_usage.used_credits` and `monthly_limit` are in **cents** on the wire; the
  subtitle showed raw values (e.g. `1720/5000 USD` instead of `$17.20 / $50.00`).
- **Fix:** `DescribeExtraUsage` divides by 100, formats USD with `$`, accepts
  `monthly_credit_limit` as an alias for `monthly_limit`, and shows “used (no cap)” when only
  spend is present.

---

## Status: v1.10 — separate toggle for session-reset notifications

### v1.10 — independent reset-notification setting
- **Problem:** threshold warnings and “limit reset / tokens available again” alerts shared
  `ShowNotifications`, but Settings only described the threshold case — no way to disable
  reset alerts alone.
- **Fix:** `AppSettings.ShowResetNotifications` (default on); new Settings checkbox;
  `TrayApplicationContext` reset balloons respect it; load migrates missing key from
  `ShowNotifications` so existing users keep prior behavior.

---

## Status: v1.9 — tray tracks 5-hour session only; extra usage is informational

### v1.9 — separate metered windows from extra usage on the tray
- **Problem:** `extra_usage` was parsed as a rolling window when it had a `utilization` field, so it could inflate the tray "peak" and icon. Extra usage (metered credits) is separate from session limits.
- **Fix:** `extra_usage` is never added to `Windows` — only `ExtraUsageLabel` for the details subtitle. Tray icon, tooltip, context-menu readout, threshold balloons, and reset alerts use **`five_hour` only** via `UsageSnapshot.TrayWindow`.
- **Details window** still lists all rolling windows; README updated.

---

## Status: v1.8 — fix HTTP 0 (curl SSL revocation check)

### v1.8 — connect / Test connection no longer fails with HTTP 0
- **Problem:** "Test connection" (and polling) reported `claude.ai returned HTTP 0` on some
  Windows setups. `curl.exe` reached DNS/TCP but Schannel aborted TLS with
  `CRYPT_E_NO_REVOCATION_CHECK` when OCSP/CRL revocation could not be checked (common on
  restricted networks). Browsers still worked because they handle revocation differently.
- **Fix:** `ClaudeUsageClient` curl config now sets **`ssl-no-revoke`** (same flag many
  Windows curl scripts use). Clearer user message if status is still 0.
- **Verification:** `curl` to `claude.ai` without the flag → `http_code=000 exit=35`; with
  `ssl-no-revoke` → `http_code=403` (expected without cookie). `dotnet build -c Release`.

---

## Status: v1.7 — portable exe hardening (self-contained release only)

### v1.7 — stop shipping the wrong exe by mistake
- **Problem:** README promised a standalone ~67 MB exe, but `dotnet build -c Release` also produced a
  **~150 KB** framework-dependent `ClaudeTokenTracker.exe` in `bin\` that only runs when .NET 8 is
  installed — easy to copy to another PC by mistake; rebuilding there “worked” only because the SDK
  installed the runtime.
- **Fix:** `UseAppHost=false` for non-publish builds (use `dotnet run` locally); publish settings
  duplicated in `.csproj`; `publish.ps1` now passes explicit `--self-contained` / single-file flags
  and **fails if output is under 40 MB**; ReadyToRun disabled for broader CPU compatibility; README
  troubleshooting for file size; `.github/workflows/release.yml` builds the real portable asset on
  tag push.
- **Ship path unchanged:** `.\publish.ps1` → `release\ClaudeTokenTracker.exe` (~63–67 MB).

---

## Status: v1.7 — ring gauge replaces clipped pill bar

### v1.7 — clearer usage visualization in the details window
- **Problem:** the slim pill progress bar's rounded end caps read as two disconnected
  half-circles flanking the percentage — looked broken and cluttered.
- **Fix:** new **`UI/UsageRing.cs`** — a compact circular progress ring (track + coloured
  arc, percentage centred inside). Each **`UsageRow`** now shows the ring on the left with
  the window name and reset time on a single horizontal line to the right (58 px tall,
  down from 78). **`UsageBar`** kept for now but no longer used in rows.
- **Verification:** `dotnet build -c Release` → **0 warnings / 0 errors**.
- **`UsageForm` subtitle** — extra usage now always on its own line below plan/org; the subtitle
  label is width-constrained with `TextRenderer` word-wrap (no `AutoSize`) so longer values like
  `Extra usage: $12 / $50` never clip off the right when they update on refresh.

## Status: v1.6 — reset datetime in details + "available again" alerts

### v1.6 — surface the reset time and notify when a limit clears
- **Goal:** show *when* each window resets (not just a countdown), and ping the user the
  moment a maxed-out limit becomes usable again.
- **`Models/UsageModels.cs`** — added `UsageWindow.IsLimited` (`Utilization >= 1.0`) so
  "reached the limit" is one unambiguous check shared by the notification logic.
- **`UI/UsageRow.cs`** — the detail rows now show the **absolute reset datetime** alongside the
  countdown, e.g. `resets in 2h 30m  ·  6:30 PM` (phrased relative to today: bare time →
  `tomorrow 6:30 PM` → `Wed 6:30 PM` → `Jun 9, 6:30 PM`). New `FormatResetClock` /
  `FormatResetDetailed` helpers; `FormatReset` (the compact countdown) is unchanged so the tray
  menu stays terse. Row reflowed to a cleaner 3-line card (name + %, bar, reset line) and grew
  64→78 px; the absolute time also means the line stays accurate between refreshes.
- **`App/TrayApplicationContext.cs`** — when any window hits `IsLimited` with a future reset, we
  record a **reset watch** (`key → (resetsAt, displayName)`). A dedicated 30 s `_resetTimer`
  (running only while something is pending, so it's idle the rest of the time) plus each poll
  fire a **"Claude limit reset — tokens are available again"** balloon the moment the reset time
  passes, then clear the watch and re-arm the threshold warning. Respects the existing
  `ShowNotifications` toggle. Time-based (not poll-based) so the alert lands on time even with a
  long poll interval. Watches are in-memory (reset across app restarts), matching `_notifiedKeys`.
- **Verification:** `dotnet build -c Release` → **0 warnings / 0 errors**.

## Status: v1.5 — one-file release (self-contained single .exe)

### v1.5 — ship it as a single portable .exe
- **Goal:** a "real release" anyone can run without installing the .NET runtime.
- **`Properties/PublishProfiles/SingleFile-win-x64.pubxml`** — canonical publish config:
  `win-x64`, `SelfContained`, `PublishSingleFile`, `IncludeNativeLibrariesForSelfExtract` (native
  libs folded in → truly one file), `EnableCompressionInSingleFile` (keeps it ~67 MB),
  `PublishReadyToRun` (faster cold start for a startup app), and `DebugType=embedded` (no loose
  `.pdb`, so the output folder holds *only* the exe). Trimming deliberately **off** (WinForms +
  reflection). Works from both the CLI and VS's Publish button.
- **`publish.ps1`** (repo root) — one-command release: locates `dotnet` even when it's not on
  `PATH` (it wasn't on this machine — the SDK lives at `C:\Program Files\dotnet`), cleans `release\`,
  publishes via the profile, and prints the final exe path + size.
- **`NuGet.config`** — the machine had **no NuGet source configured** at all, so the self-contained
  restore failed (`NU1100` on the runtime packs). Added a repo-local source (`<clear/>` + nuget.org)
  used *only* to fetch the runtime packs the single file bundles. The app still has **zero NuGet
  package deps**, and the lean framework-dependent path still needs no download.
- **Version** bumped `1.0.0` → **`1.4.0`** (`Version`/`FileVersion`/`AssemblyVersion`) so the exe's
  metadata matches the actual shipped state.
- **Output:** `release\ClaudeTokenTracker.exe` — **single file, 66.7 MB**, ProductVersion 1.4.0,
  runs on Win10 (1803+)/11 x64 with no prerequisites. `dotnet publish` → 0 errors. (Live launch
  smoke-test skipped: a dev instance was already running, so the single-instance mutex would only
  pop the "already running" dialog.)

## Status: v1.4 — UI redesign (Anthropic aesthetic), builds clean

### v1.4 — professional visual pass on the windows
- **New `UI/Theme.cs`** — one place for the whole visual language, using Anthropic's published
  brand palette: ivory canvas `#FAF9F5` (never pure white), warm charcoal ink `#141413`, the clay
  accent `#D97757` (hover `#C6613F`), light-gray hairlines `#E8E6DC`. Headings use a serif
  (Georgia) and body uses Segoe UI to echo their editorial type. A single warm **semantic usage
  ramp** (olive → ochre → clay → brick) is now shared by the meters *and* the tray icon, so every
  surface agrees on colour. Also holds the font / rounded-rect / hairline helpers.
- **New `UI/FlatButton.cs`** — owner-drawn, anti-aliased rounded button with two variants: a filled
  clay **Primary** and a quiet outlined **Secondary**, each with hover/press states.
- **`UsageForm`** — ivory canvas, serif "Claude Usage" title + muted subtitle, hairline header/footer
  bands (rules, not heavy fills), slim textless meters with a bold colour-coded **% on the right**,
  hairline row dividers (replacing zebra), and a clay Refresh button + clay link.
  - Fixed a footer-layout bug found via an off-screen render: the right-aligned actions were anchored
    while the footer panel still had its default (pre-dock) width, shoving them ~220 px off the right
    edge. They're now positioned from the footer's `Layout` event using its real width, so they sit
    correctly and track window resizes.
  - Fixed the header subtitle clipping its trailing **extra-usage** segment: the subtitle was an
    `AutoSize` label that ran off the right edge, so `… · Extra usage: 12/50 USD` (the last item, and
    the *only* place credit-based overage is surfaced) was cut off. It now wraps to the window width
    via `LayoutHeader()` (constrains `MaximumSize`, grows the header band to fit) and re-flows on
    resize. Verified via off-screen renders at 420 px and 380 px widths.
- **`SettingsForm`** — matching serif header band, palette-styled inputs (flat single-line borders,
  white fields), clay **Save** / outlined **Cancel** / outlined **Test connection**, and a clay
  "How do I get this?" link.
- **`TrayIconRenderer`** — colour logic now delegates to `Theme.SemanticColor` (gray = `InkMuted`).
- **Merge note:** this pass landed alongside the v1.3 pin-to-taskbar work. The `SettingsForm`
  rewrite was reconciled so the **"Always show the icon on the taskbar"** checkbox (bound to
  `AppSettings.PinToTaskbar`) is preserved and still round-trips through Save.
- **Verification:** `dotnet build -c Release` → **0 warnings / 0 errors**. Both windows were rendered
  off-screen to PNG to confirm layout, spacing, and the colour ramp before sign-off. No new NuGet
  deps; the curl transport and all behaviour are untouched.

## Status: v1.3 — pin-to-taskbar (always-visible tray icon)

### v1.3 — keep the icon glanceable
- **Problem:** Windows 11 (22H2+) hides tray icons in the "⌃" overflow flyout by default, so the
  glanceable color/percentage icon wasn't actually visible at a glance.
- **Fix:** new `Services/TaskbarPinner.cs` promotes our own icon by setting
  `IsPromoted = 1` on our entry under `HKCU\Control Panel\NotifyIconSettings` (matched by
  `ExecutablePath`). Effect is immediate — no Explorer restart. The subkey only appears a beat
  after the icon is first shown and Windows can regenerate it, so we retry briefly on launch
  (8× / 800 ms) and re-apply every start.
- **Controls:** `AppSettings.PinToTaskbar` (default **on**); tray-menu toggle "Always show icon on
  taskbar" and a matching Settings checkbox. Pre-22H2 returns `Unsupported` and we show manual
  pin instructions (drag out of overflow / Taskbar settings) instead of failing silently.
- No new dependencies (registry via `Microsoft.Win32`, same as `StartupManager`).

## Status: v1.2 — VERIFIED WORKING against a live Max 5x account

### v1.2 — Cloudflare bypass via curl transport (the fix that made it actually work)
- **Verified end-to-end with a real sessionKey**: correctly reads plan ("Max 5x"), org,
  extra-usage credits, and per-window utilization with reset times.
- **Critical discovery #1 — utilization is a PERCENT (0–100), not a 0–1 fraction** as the
  third-party write-ups claimed. Live data: `five_hour.utilization = 20.0` means 20%. Fixed the
  scaling (was showing 2000% / maxed red icon). Plan now derived from org `rate_limit_tier`
  (e.g. `default_claude_max_5x` → "Max 5x"); dropped the redundant subscription_details call.
- **Critical discovery #2 — Cloudflare blocks .NET HttpClient.** Every variant (HTTP/1.1, HTTP/2,
  browser hints, with/without compression) gets `cf-mitigated: challenge` ("Just a moment" HTML).
  The OS `curl.exe` (same Schannel TLS!) passes — the difference is the TLS/HTTP fingerprint
  (JA3/JA4) which .NET can't easily change.
  - **Solution:** `ClaudeUsageClient` now shells out to **`curl.exe`** (built into Windows 10
    1803+/11) as its HTTP transport. Headers + cookie go through a short-lived curl `--config`
    file (cookie never on the command line); status parsed via a `--write-out` marker.
  - Trade-off: depends on curl's fingerprint continuing to pass Cloudflare. No NuGet deps added.

## Status: v1.1 — functional, builds clean; auth/cookie diagnostics hardened

### v1.1 — cookie & auth troubleshooting (after first user test)
- Probed the live endpoint: **auth is purely the `sessionKey` cookie**; Cloudflare is *not*
  challenging plain requests (clean JSON `403 account_session_invalid` when unauthenticated).
  `anthropic-client-platform` is **not** required, but is now sent for browser fidelity.
- `ClaudeUsageClient` now **surfaces claude.ai's own error message** (e.g.
  "Invalid authorization (account_session_invalid)") instead of a generic HTTP code — turns the
  vague "couldn't get usage" into an actionable message.
- Added browser-mimicking headers (`anthropic-client-platform/version/device-id`), bumped UA.
- More forgiving cookie parsing: strips wrapping quotes/whitespace from a bare value, and can
  extract `sessionKey`/`cf_clearance` from a pasted cURL/header blob.
- Rewrote the in-app cookie help to lead with the **Application → Cookies → sessionKey** method
  and explain that DevTools "Copy as fetch/cURL" *redacts* the cookie (the `credentials: "omit"`
  the user saw is normal; the cookie is still sent by the browser).

## Status: v1.0 — functional, builds clean, launches

### Done
- [x] Decided data source: claude.ai consumer (Pro/Max) usage endpoint + session cookie.
- [x] Installed .NET 8 SDK (8.0.421) via winget.
- [x] Project scaffold: `.csproj`, `Program.cs`, per-monitor DPI, single-instance mutex,
      global exception logging to `%APPDATA%\ClaudeTokenTracker\error.log`.
- [x] `ClaudeUsageClient` — lists orgs, reads usage (+ best-effort plan label / extra-usage),
      defensive JSON parsing, friendly HTTP/Cloudflare/auth error messages.
- [x] `SettingsStore` + `DataProtection` — settings JSON with **DPAPI-encrypted cookie**
      (P/Invoke, no NuGet).
- [x] `TrayIconRenderer` — runtime-drawn color-coded % icon (no GDI handle leak).
- [x] `TrayApplicationContext` — NotifyIcon, context menu w/ per-window readouts,
      polling timer, threshold balloon notifications, peak summary tooltip.
- [x] `SettingsForm` — cookie entry, **Test connection**, org picker, poll interval,
      warn threshold, start-with-Windows + notifications toggles, cookie help.
- [x] `UsageForm` / `UsageRow` / `UsageBar` — details window with colored bars + reset countdowns.
- [x] `StartupManager` — per-user Run-key toggle.
- [x] Build succeeds (0 warnings / 0 errors); smoke-tested startup (no crash, no error log).
- [x] **Minimal dependencies**: zero NuGet packages, zero NuGet sources required. Verified a clean
      framework-dependent publish (`dotnet publish -c Release -o publish`) → ~230 KB output
      (`.exe` + one DLL) using the installed .NET 8 Desktop Runtime.
- [x] README + .gitignore.

### Verified manually by the user
- [x] Paste a real cookie → Test connection succeeds and shows live numbers.
- [x] Icon color/percentage updates on the polling interval.
- [ ] Threshold notification fires.
- [x] "Start with Windows" persists across reboot.

### Possible future enhancements
- [ ] Auto-read the cookie from the local browser (Chrome/Edge DPAPI cookie store) so no manual paste.
- [ ] History/graph of utilization over time.
- [ ] Optional `console.anthropic.com` Admin API mode (API cost/usage) as a second source.
- [ ] Installer (MSI / winget manifest) and a proper app `.ico`.
- [ ] Resilience: detect schema changes and surface a clear "endpoint changed" hint.

---

## Notes / decisions
- The usage endpoint is **undocumented**; parsing is intentionally defensive (any object with a
  numeric `utilization` is treated as a window) so new windows appear automatically.
- Full Cookie header (incl. `cf_clearance`) is recommended over bare `sessionKey` for Cloudflare.
- **Dependency policy: keep it minimal.** No NuGet *package* deps (DPAPI via P/Invoke). The
  **release ship path** is now the self-contained single file (`publish.ps1` → one ~67 MB exe, no
  runtime install for end users); it pulls only the .NET runtime packs from nuget.org at build time.
  A lean framework-dependent publish (`dotnet publish -c Release -o publish`, ~230 KB) is still
  available for machines that already have the .NET 8 Desktop Runtime and downloads nothing.
