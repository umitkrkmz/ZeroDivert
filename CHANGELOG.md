# Changelog

All notable changes to ZeroDivert are documented here.

Format follows [Keep a Changelog](https://keepachangelog.com/en/1.0.0/).
Versions follow [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

---

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
