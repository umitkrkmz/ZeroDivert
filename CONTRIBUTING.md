# Contributing to ZeroDivert

Thank you for your interest in contributing to ZeroDivert!

---

## Table of Contents

- [How to Contribute](#how-to-contribute)
- [Development Setup](#development-setup)
- [Branch Naming](#branch-naming)
- [Commit Messages](#commit-messages)
- [Pull Request Process](#pull-request-process)
- [Code Style](#code-style)

---

## How to Contribute

### Reporting Bugs

1. Search [existing issues](https://github.com/umitkrkmz/ZeroDivert/issues) first
2. Open a new issue with:
   - Windows version
   - ISP name/country (helps with ISP-specific DPI behavior)
   - Steps to reproduce
   - Output from `-v` (verbose) mode

### Suggesting Features

Open an issue with the `enhancement` label. For large changes, discuss before writing code.

### Submitting Code

See [Pull Request Process](#pull-request-process) below.

---

## Development Setup

### Requirements

- Windows 10/11 64-bit
- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- Administrator privileges (required for WinDivert)
- WinDivert 2.2.2 — already bundled in `src/ZeroDivert.Driver/`

### Clone and Build

```bash
git clone https://github.com/umitkrkmz/ZeroDivert.git
cd ZeroDivert
dotnet build -c Release
```

Run as Administrator:
```
.\src\ZeroDivert.Console\bin\Release\net10.0-windows\ZeroDivert.Console.exe
```

### Project Structure

```
src/
  ZeroDivert.Core/     # Business logic — strategies, engine, filter, logging
  ZeroDivert.Driver/   # WinDivert P/Invoke wrapper + bundled binaries
  ZeroDivert.Console/  # CLI entry point
```

---

## Branch Naming

| Type | Pattern | Example |
|------|---------|---------|
| Feature | `feature/<short-name>` | `feature/tls-record-split` |
| Bug fix | `fix/<issue-or-desc>` | `fix/adaptive-calibration` |
| Docs | `docs/<topic>` | `docs/isp-guide` |

---

## Commit Messages

Use imperative mood:

```
Add: TLS record fragmentation strategy
Fix: SNI offset calculation for multi-byte characters
Update: adaptive engine timeout threshold
Remove: unused DiscordFilter overload
```

Prefix with: `Add`, `Fix`, `Update`, `Remove`, `Refactor`, `Docs`

---

## Pull Request Process

1. Fork the repository
2. Create a branch from `main`
3. Make your changes
4. Run `dotnet build` — must succeed without errors
5. Open a PR with a clear description and reference to any related issues

---

## Code Style

- Standard C# conventions (PascalCase for types/methods, camelCase for locals)
- Nullable reference types enabled
- Keep bypass strategies self-contained — implement `IDpiBypassStrategy`
- Thread safety: packet processing runs on multiple threads

---

## Questions?

- **General questions / ideas** → [GitHub Discussions](https://github.com/umitkrkmz/ZeroDivert/discussions)
- **Bug reports / feature requests** → [GitHub Issues](https://github.com/umitkrkmz/ZeroDivert/issues)
