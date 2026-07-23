# Changelog

All notable changes to ZeroDivert are documented here.

Format follows [Keep a Changelog](https://keepachangelog.com/en/1.0.0/).
Versions follow [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

---

## [1.1.0] - 2026-07-24

### Fixed

- **`AdaptiveEngine` never rotated bypass techniques** — `ReportFailure` was never called from anywhere in the codebase, so `_consecutiveFailures` never advanced and `TryNextTechnique()` was dead code. In practice ZeroDivert always stayed on `TcpFragmentation` regardless of whether it actually worked, contradicting the documented "tries all 5 techniques" behavior. Added RST-based failure detection: an inbound TCP RST arriving shortly after a Discord ClientHello was sent on the same local port is now treated as a bypass failure and reported to the adaptive engine, letting it rotate through techniques as designed.
- **Status line stats could show inconsistent values** (e.g. `Modified` briefly exceeding `Pkts`) — `StatusLine.UpdateStats` updated its four counters with separate `Interlocked.Exchange` calls, so the background refresh timer could read a mix of old and new values mid-update. The four fields are now updated under the same lock the refresh timer uses.
- **Ctrl+C shutdown could throw on the signal-handler thread** — `_handle.Shutdown()` in the `CancelKeyPress` handler could race with the main loop's own handle disposal and throw `ObjectDisposedException`, occasionally making shutdown appear to hang. Now handled gracefully.
- **Silent (non-RST) DPI blocks were never detected** — some ISPs drop packets after a ClientHello instead of sending a RST, which the RST-only failure check above couldn't see, leaving the adaptive engine stuck reporting "success" on a technique that wasn't actually getting through. A pending ClientHello with no inbound response at all within 6 seconds is now also reported as a failure.

### Documentation

- Documented that some ISPs (confirmed for at least one Turkish ISP) block Discord at the **DNS layer** in addition to SNI/TLS, so ZeroDivert's SNI-hiding alone isn't sufficient — DNS also needs to point to an encrypted resolver (e.g. Cloudflare `1.1.1.1`/`1.0.0.1`) for Discord to connect. See the README FAQ.

### Added

- **Spectre.Console-based interactive UI** — replaces the hand-rolled ANSI console output:
  - `FigletText` startup banner instead of a raw ASCII-art string.
  - `SpectreStatusPanel` — a live-refreshing dashboard (technique, packet/Discord/modified counts, throughput, current status, and a rolling window of recent events) shown while monitoring, replacing the old single-line status redraw (`ZeroDivert.Core.UI.StatusLine`, now removed as dead code — its `LogEntry`/`LogLevel` types remain since `FileLogger` still uses them).
  - Interactive `MultiSelectionPrompt` at startup when run with no arguments in an interactive terminal, so `-v` / `--no-log` / `--no-status` can be toggled without memorizing flags. Passing explicit CLI args (e.g. from a script) skips the prompt as before.
- **Home menu** — when run interactively with no arguments, ZeroDivert now opens a persistent menu (`Başlat` / `Ayarlar` / `Log ve Profil Bilgisi` / `Çıkış`) instead of going straight into a one-shot options prompt:
  - `Ayarlar` → `Çalışma seçenekleri` re-opens the run-options prompt (now pre-selects whatever was already chosen), plus a new `DNS Ayarları` screen.
  - `DNS Ayarları` (`DnsSettingsManager.cs`) lists active network adapters (name, current DNS source, current DNS servers) and lets you point one at Cloudflare (`1.1.1.1`/`1.0.0.1`), Google (`8.8.8.8`/`8.8.4.4`), a custom pair of addresses, or back to DHCP — applied via `netsh interface ip set/add dns`. Added directly in response to the DNS-layer blocking found above, so DNS can be fixed from inside the app instead of a manual PowerShell step.
  - `Log ve Profil Bilgisi` shows the profile path/status, log directory/most recent log file, and (read-only) the saved profile's technique, verified/success-rate, test-history count, and timestamps.
  - `Ayarlar` → `Başlangıçta Otomatik Başlat` installs/removes a Task Scheduler task (`AutoStartManager.cs`, via `schtasks`) that launches ZeroDivert in tray mode at logon with `/rl highest` (elevated without a UAC prompt each time). Deliberately *not* a Windows Service — services run in Session 0 and can't show a tray icon, which was the actual ask.
  - `Çıkış` exits without starting monitoring. Once `Başlat` is chosen, Ctrl+C behavior is unchanged from before (stops monitoring and exits the app — it does not return to the menu).
  - Selection menus got a shared `CreateMenu` helper (consistent highlight color, wrap-around, page size) for a more readable/consistent look.
- **Tray icon is now always present** (`TrayApp.cs`, `--tray` still selects the headless auto-start launch), not just in a separate background-only mode. `NotifyIcon` with Başlat/Durdur, Konsolu Göster/Gizle, Çıkış. Closing the console window (the "X" button) now **hides it to the tray instead of exiting** (via a native `SetConsoleCtrlHandler` intercepting `CTRL_CLOSE_EVENT`) — Ctrl+C or the tray's "Çıkış" are the only things that actually stop monitoring and end the process. `AppStateStore.cs` persists whether monitoring was running when the app last exited (`%LOCALAPPDATA%\ZeroDivert\state.json`), so a headless (`--tray`) launch resumes the same state instead of re-probing anything. If a monitoring session fails right after starting (e.g. WinDivert couldn't open), the tray now shows a balloon-tip error and resets the label back to "Başlat" instead of silently claiming "Durdur" for a session that never actually ran. Tray icon is extracted from the app's own exe icon instead of a generic shield. Requires `UseWindowsForms` in the Console project; `Panel`/`Color` references were disambiguated (`Spectre.Console.Panel`/`Spectre.Console.Color`) where that introduced a WinForms/Spectre naming clash.

## [1.0.0] - 2025-02-17

### Added

**Core**
- `ZeroDivertEngine` — Main engine coordinating packet capture and bypass strategies
- `DpiBypassEngine` — Strategy coordinator with 5 bypass techniques
- `AdaptiveEngine` — Automatically tests and selects the best bypass strategy for the user's ISP
- `PacketInspector` — Kernel-level packet parsing and SNI extraction
- `DiscordFilter` — Discord-specific traffic detection (domains, IPs, ports)
- `FileLogger` — JSON packet logging with periodic stats
- `StatusLine` — Single-line real-time status display

**Bypass Strategies**
- `TcpFragmentationStrategy` — Splits ClientHello into small TCP segments
- `SmartFragmentationStrategy` — Calculates exact SNI offset, splits at optimal point
- `DesyncStrategy` — Reorders TCP packets to break DPI state machine
- `FakeTtlStrategy` — Sends decoy packets with low TTL that expire before the server
- `UdpFakeStrategy` — Applies fake packet techniques for UDP-based Discord voice traffic

**Configuration**
- `BypassProfile` + `ProfileManager` — Saves working strategy to `profile.json`, loads on next run
- Per-user profile path: `%LOCALAPPDATA%\ZeroDivert\profile.json`

**CLI**
- Standard run mode (automatic calibration on first run, profile on subsequent runs)
- `-v` / `--verbose` — Verbose output
- `--no-log` — Disable file logging

**Driver**
- `WinDivertHandle` — WinDivert handle management, packet send/receive
- `WinDivertNative` — Full P/Invoke declarations for WinDivert 2.2 API
- `WinDivertStructs` — Native structure definitions

**Project**
- GPL v2 license
- `.gitignore` for .NET / WinDivert / user data
- `CONTRIBUTING.md`, `CHANGELOG.md`, `SECURITY.md`
- Comprehensive bilingual README (Turkish + English)
- WinDivert 2.2.2 bundled in `src/ZeroDivert.Driver/`
