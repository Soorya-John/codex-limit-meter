# Codex Limit Meter

A small Windows notification-area app. No main window, console, or taskbar button.

## Run

Launch `dist/CodexLimitMeter.exe`. Requires Windows 10/11 with .NET Framework 4.8 and an installed Codex executable signed in with your ChatGPT account. No Python, .NET SDK, or API key is needed.

The tray number is the **percentage remaining in the most depleted Codex quota window** (for example the lower of the five-hour and weekly values). Green: above 25%; orange: 11–25%; red: 10% or below. A gray `!` means the latest refresh failed; `--` means no quota windows were reported. Hover for a quick status; left-click or right-click for each bucket, percentages, and local reset times.

Refreshes every minute. The menu includes Refresh now, optional Start with Windows, a usage-page link, and Exit. Windows may initially place the icon in the overflow area: drag it into the visible notification area. Startup is off by default; enable it after placing the executable in a permanent folder. Disable it before moving/removing the executable.

## Data and privacy

Uses the documented [Codex app-server protocol](https://learn.chatgpt.com/docs/app-server): `initialize`, `initialized`, `account/rateLimits/read`. Starts a hidden, short-lived `codex app-server` for each refresh and reuses Codex's existing authentication. It does not start model turns, consume earned resets, read credential files itself, or persist usage responses. Codex may maintain its own normal local state. API-key-only authentication does not provide ChatGPT subscription quotas.

The app first looks for the Windows Codex desktop installation, then `codex.exe` on PATH. To override, set `CODEX_LIMIT_METER_CODEX` to an absolute Codex `.exe` path. Existing `CODEX_HOME` is respected; otherwise `%USERPROFILE%\.codex` is used. If unavailable, the menu marks old data as stale and retries on the next minute. Requests time out after 30 seconds.

## Build and verify

Run `powershell -ExecutionPolicy Bypass -File .\build.ps1`. Uses the Windows .NET Framework C# compiler; no package downloads. The script builds a Windows GUI subsystem executable and runs built-in parser tests (multiple buckets, reset timestamps, null windows, invalid responses, percentage bounds).

For a live connection check without starting the tray:

```powershell
Start-Process .\dist\CodexLimitMeter.exe -ArgumentList '--check', 'check-result.txt' -Wait
Get-Content .\check-result.txt
```

The optional check file contains quota summaries or a generic connection error, never credentials. A second normal launch exits immediately; use Exit in the tray menu to stop the running instance. The executable is unsigned, so Windows may show a reputation prompt.

Run `powershell -ExecutionPolicy Bypass -File .\tests\test.ps1` for an offline protocol integration test and tray-only/single-instance smoke tests. The fixture has no network access and no real account data.
