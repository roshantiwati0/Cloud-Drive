# 📜 PROJECT POLICIES & COMPLIANCE (Cloud-SSD)

> **Overview:** Guidelines for open-source contributions, security vulnerability reporting, and third-party software licensing notices.  
> **Source Documents Consolidated:** CONTRIBUTING.md, SECURITY.md, THIRD_PARTY_NOTICES.md.

---

# 📑 TABLE OF CONTENTS
- [1. Contributing Guide](#1-contributing)
- [2. Security Policy](#2-security)
- [3. Third Party Software Notices](#3-third-party-notices)

---

# 1. Contributing Guide
# Contributing

Thanks for helping. A few ground rules keep Cloud-SSD honest and safe:

- **No DRM, anti-cheat, launcher or ownership bypasses.** Pull requests that add them are closed.
- **No fabricated numbers.** Metrics, benchmarks and "game works" claims must come from a real, reproducible run.
  Say what was tested and what was not.
- **Never delete cloud data**, and never delete user save files to free cache space.
- Keep secrets out of the repository (see [SECURITY.md](archive/SECURITY.md)).

## Development

Requirements: Windows 10/11 x64, .NET 10 SDK, WinFsp (for integration tests), PowerShell.

```bash
powershell -ExecutionPolicy Bypass -File installer/fetch-rclone.ps1
```

```bash
dotnet build CloudSSD.slnx -c Release
```

```bash
dotnet test tests/CloudSSD.Tests
```

Integration tests (`tests/CloudSSD.IntegrationTests`) mount real drives and take several minutes. Please run them
when you change mounting, caching or streaming code, and include the results in your pull request.

To build the installer (`dist/CloudPlayX-<version>-setup.exe`), install Inno Setup 6
(`winget install JRSoftware.InnoSetup`) and run `installer/build-release.ps1`.


---

# 2. Security Policy
# Security

Cloud-SSD handles Google sign-ins, so please report security problems privately: use GitHub's
**Security → Report a vulnerability** on this repository instead of opening a public issue.

How credentials are handled:

- Google sign-in happens in your browser (OAuth via rclone). Cloud-SSD never sees your Google password.
- The saved rclone login is encrypted with a key protected by Windows DPAPI (current user).
- rclone's remote-control API listens on 127.0.0.1 only, with random credentials per session.
- Logs and diagnostic reports pass through a redactor that removes tokens, client secrets and passwords.

Never put OAuth client secrets, tokens or `rclone.conf` in issues, pull requests or commits. The optional publisher
key `installer/google-client.json` is ignored by git on purpose.


---

# 3. Third Party Software Notices
# Third-party notices

Cloud-SSD bundles or depends on the following components.

## rclone (bundled: `tools/rclone/rclone.exe`, v1.75.2)

https://rclone.org — MIT License

```
Copyright (C) 2012 by Nick Craig-Wood http://www.craig-wood.com/nick/

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in
all copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
THE SOFTWARE.
```

The binary was downloaded from `https://downloads.rclone.org/v1.75.2/` and its archive was verified against the
official `SHA256SUMS` (included next to the binary).

## WinFsp (official installer included in `CloudPlayX-…-setup.exe`, v2.1.25156)

https://winfsp.dev, Copyright (C) Bill Zissimopoulos / Navimatics LLC. GPLv3 with a FLOSS exception. Source
code: https://github.com/winfsp/winfsp. The setup program carries the unmodified, Authenticode-signed
`winfsp-2.1.25156.msi` from https://github.com/winfsp/winfsp/releases and runs it only when WinFsp is missing.
Cloud-SSD does not link WinFsp; rclone uses the WinFsp installation present on the system. WinFsp is a separate
program with its own uninstall entry ("WinFsp 2025").

## NuGet packages

| Package | License |
|---|---|
| CommunityToolkit.Mvvm | MIT |
| Microsoft.Data.Sqlite (SQLitePCLRaw, SQLite) | MIT / Public domain |
| Microsoft.Extensions.Logging, DependencyInjection | MIT |
| System.Security.Cryptography.ProtectedData | MIT |
| System.Diagnostics.EventLog | MIT |
| xunit, Microsoft.NET.Test.Sdk, coverlet (tests only) | Apache-2.0 / MIT |

## .NET runtime (bundled in self-contained releases)

MIT License, © .NET Foundation and Contributors.

