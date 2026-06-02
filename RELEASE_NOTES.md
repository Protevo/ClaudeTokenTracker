# Release notes

## Claude Token Tracker 1.7.0

**June 2026** — clearer usage details and a more reliable header.

### Highlights

- **Circular progress rings** in the details window replace the old pill bar. Each usage window shows a compact ring with the percentage inside, plus the window name and reset time beside it — easier to read at a glance and no more awkward split end caps on the old bar.
- **Extra usage always visible** — plan and organization stay on the first line of the header; credit-based overage (`Extra usage: …`) moves to its own line below. Long values (for example `12/50 USD`) wrap inside the window instead of clipping off the right when they update on refresh.

### Details

- New `UsageRing` control: coloured arc on a neutral track, same semantic colour ramp as the tray icon (olive → ochre → clay → brick).
- Usage rows are slightly shorter (58 px) with ring + text laid out on one horizontal line.
- Header subtitle uses fixed width and word-wrap (`TextRenderer`) so dynamic extra-usage text reflows correctly on every refresh and resize.

### Requirements

Unchanged from prior releases:

- Windows 10 (1803+) or Windows 11 (x64)
- Claude.ai **Pro** or **Max** subscription and a valid session cookie
- For the portable build: no .NET install — use `release\ClaudeTokenTracker.exe` from `.\publish.ps1`

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
