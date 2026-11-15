# Security policy

## Reporting a vulnerability

Please report vulnerabilities **privately**, not in public issues:

1. Open the [Security tab](https://github.com/BitJacker/shiftwall/security) of the repository.
2. Choose **Report a vulnerability** and fill in the form (GitHub
   [private vulnerability reporting](https://docs.github.com/code-security/security-advisories/guidance-on-reporting-and-writing/privately-reporting-a-security-vulnerability)).

Describe the problem, the affected version and the steps to reproduce it. **Never attach a real FortiGate
configuration**: reproduce the problem with a synthetic file, or with a configuration anonymized by Shiftwall's
*Anonymize config* tool (do not send the reversible map, nor its password).

You will get an answer as soon as possible. Fixes are released as a new version with a note in
[CHANGELOG.md](CHANGELOG.md); reporters are credited if they wish.

## Supported versions

Only the latest release receives security fixes.

## Privacy policy

Shiftwall sends no data. It works completely offline.

What Shiftwall does:

- It reads the configuration files you open and processes them **in memory**.
- It writes only the files you save, where you choose, and never overwrites the file you opened.
- It stores your settings in `%APPDATA%\Shiftwall\settings.json` and an error log with exception types and code
  locations only, never configuration content, in `%LOCALAPPDATA%\Shiftwall\logs`. Details:
  [docs/download-and-verify.md](docs/download-and-verify.md#3-what-shiftwall-writes-on-your-pc).

What Shiftwall does **not** do:

- No telemetry, analytics, online crash reporting or automatic updates.
- No network connections, with one exception: the optional *Backup from LAN* page (off by default) connects, when you
  press *Download*, only to the private IP address you enter, after you confirm its certificate fingerprint. The API
  token is never stored.
- It never tries to decrypt `ENC` values (passwords, pre-shared keys, private keys): they are copied unchanged.

These rules are enforced by automated tests: the build fails if code outside the LAN module uses a network client.
