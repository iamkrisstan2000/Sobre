# Sobre — Ad-free mail for Windows

[![License: GPL v3](https://img.shields.io/badge/License-GPLv3-blue.svg)](LICENSE)
[![Windows](https://img.shields.io/badge/Windows-11-0078D4?logo=windows)](https://github.com/iamkrisstan2000/Sobre)
[![Version](https://img.shields.io/badge/version-1.1.0-0F6CBD)](https://github.com/iamkrisstan2000/Sobre/releases)

> **A fast, native Windows mail app.** No ads. No tracking. No Electron.

**Created by Mr Knight (KnightWin) — Made possible with AI — November 2026**

[![ko-fi](https://ko-fi.com/img/githubbutton_sm.svg)](https://ko-fi.com/N7D7292WUD)

Your support keeps Sobre free and ad-free. Every coffee helps ship the next update — [ko-fi.com/N7D7292WUD](https://ko-fi.com/N7D7292WUD)

---

### Download — v1.1.0

Get the latest release from [Releases](https://github.com/iamkrisstan2000/Sobre/releases):

| File | What it is | Size |
|------|------------|------|
| `Sobre-1.1.0-win-x64.exe` | **Installer** — installs to Start Menu, creates shortcut | ~75 MB |
| `Sobre-1.1.0-portable-win-x64.exe` | **Portable** — single file, needs [.NET 8 Desktop Runtime](https://dotnet.microsoft.com/en-us/download/dotnet/8.0) | ~0.2 MB + runtime |

> Windows SmartScreen will warn because the binary is unsigned — choose **More info → Run anyway**. For distribution, sign it with your own certificate.

**Requirements:** Windows 11 (works on Windows 10 1809+). x64. Windows 11 22H2+ shows true Mica/Acrylic; older builds fall back to a solid Fluent layer.

**What's new in 1.1.0:**
- Reader pane now reflows wide tables/code with horizontal scroll (`FlowDocument` PageWidth fix)
- Add Account shows a clickable **Google App Passwords** link for Gmail and help link for other presets
- Startup crash fixed (IMAP login no longer recurses)
- Outlook preset removed — use **Custom** for any IMAP/SMTP server (Gmail, Yahoo, iCloud, Fastmail presets remain)

---

### Quick start

1. Download and run `Sobre`.
2. Click **Settings → Accounts → Add account** (or the `+` on the left rail).
3. Pick **Gmail / Yahoo / iCloud / Fastmail** or **Custom IMAP + SMTP**.
4. Enter your email + app password and hit **Test connection**.

No account yet? Sobre opens empty and gently prompts you to add one — no fake demo mailbox.

---

### Features

- **Multi-account** — each account keeps its own colour, signature, and servers. Switch instantly from the left rail.
- **Fluent design** — light / dark / follow Windows, live accent sync, Mica backdrop on Windows 11.
- **Private by design** — no ads, no analytics, no telemetry. Passwords sealed with **Windows DPAPI + AES-256-GCM**.
- **App lock** — optional PBKDF2-SHA256 password on startup (600,000 iterations).
- **Fast list** — virtualized message list, lazy body loading, 60 msgs per folder cache to keep RAM low.
- **Search & filters** — search sender/subject/preview, toggle unread / flagged.
- **Optimized for Windows 11** — SegmentHeap, Workstation GC, no ReadyToRun bloat.

---

### Setting up mail

**Gmail** needs a 16-char App Password:
1. Enable 2-Step Verification at [myaccount.google.com/security](https://myaccount.google.com/security)
2. Create an App Password at [myaccount.google.com/apppasswords](https://myaccount.google.com/apppasswords) named `Sobre`
3. In Sobre pick **Gmail** → paste the app password (the Help link in the app opens this directly)

**Yahoo / AOL:** Generate an app password at [login.yahoo.com/account/security](https://login.yahoo.com/account/security) → pick **Yahoo Mail**.

**iCloud:** Create an app-specific password at [appleid.apple.com](https://appleid.apple.com) → **Sign-In and Security**.

**Fastmail:** Generate in **Settings → Privacy & Security → App passwords** → [app.fastmail.com/settings/security/apps](https://app.fastmail.com/settings/security/apps)

**Other / Custom:** Enter the IMAP + SMTP details from your provider. Works with any standard server (including Outlook.com via `outlook.office365.com:993` + `smtp-mail.outlook.com:587` if your account still allows app passwords).

---

### Where your data lives

All under `%LOCALAPPDATA%\Sobre`:

```
accounts.json   account config (no passwords)
settings.json   theme, layout, sync prefs
device.key      32-byte AES key, DPAPI-wrapped to your Windows user
sobre.log       only if something crashes
```

**Network:** Sobre talks only to the IMAP/SMTP servers you configure. No update checker, no crash reporter.

---

### Support Sobre

Sobre is free and will stay ad-free. If it saves you time:

[![ko-fi](https://ko-fi.com/img/githubbutton_sm.svg)](https://ko-fi.com/N7D7292WUD)

[Buy me a coffee](https://ko-fi.com/N7D7292WUD) — it funds icons, testing on real Windows hardware, and the next feature.

---

### Building from source

```powershell
git clone https://github.com/iamkrisstan2000/Sobre.git
cd Sobre/src/Sobre
dotnet publish -c Release -o ./publish
```

Result: `Sobre.exe` — single self-contained file, .NET 8 included. Needs .NET 8 SDK.

---

### Credits

- **Icon** — Fluent white envelope with knight seal (drawn for Sobre)
- **Icons** — [Microsoft Fluent System Icons](https://github.com/microsoft/fluentui-system-icons) MIT
- **Mail** — [MailKit](https://github.com/jstedfast/MailKit) MIT
- **Author** — KnightWin ([github.com/iamkrisstan2000](https://github.com/iamkrisstan2000))

Sobre is not affiliated with Microsoft, Google or Apple.

---

### License

**GNU GPL v3** — see [LICENSE](LICENSE). You can use, share, and modify Sobre, but any distributed modified version must stay open under GPL-3.0.

© 2026 KnightWin
