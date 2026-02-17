# Security Policy

## Supported Versions

| Version | Supported |
|---------|-----------|
| 1.0.x   | Yes       |
| < 1.0   | No        |

---

## Reporting a Vulnerability

**Please do not report security vulnerabilities through public GitHub issues.**

Report privately via [GitHub Security Advisories](https://github.com/umitkrkmz/ZeroDivert/security/advisories/new).

### What to include

- Description of the vulnerability
- Steps to reproduce
- Potential impact

### Response Timeline

- **Acknowledgement**: within 3 business days
- **Initial assessment**: within 7 days

---

## Security Considerations

ZeroDivert requires **Administrator privileges** to operate (WinDivert kernel driver).

- Only download ZeroDivert from the [official repository](https://github.com/umitkrkmz/ZeroDivert/releases)
- WinDivert may trigger antivirus warnings — this is a known false positive. Source code is available at [basil00/WinDivert](https://github.com/basil00/WinDivert)

**Out of scope:**
- WinDivert itself — report to the [WinDivert project](https://github.com/basil00/WinDivert)
- Vulnerabilities in the .NET runtime
