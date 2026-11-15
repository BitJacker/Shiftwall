<p align="center">
  <img src="assets/brand/logo.svg" alt="Shiftwall logo" width="120">
</p>

<h1 align="center">Shiftwall</h1>

<p align="center">Offline FortiGate configuration migration and sysadmin toolbox for Windows, in a single portable exe.</p>

<p align="center"><b>English</b> · <a href="README.it.md">Italiano</a></p>

<p align="center">
  <a href="https://github.com/BitJacker/shiftwall/actions/workflows/ci.yml"><img src="https://github.com/BitJacker/shiftwall/actions/workflows/ci.yml/badge.svg" alt="Build"></a>
  <a href="https://github.com/BitJacker/shiftwall/releases/latest"><img src="https://img.shields.io/github/v/release/BitJacker/shiftwall?include_prereleases" alt="Latest release"></a>
  <a href="LICENSE"><img src="https://img.shields.io/badge/license-GPL--3.0-blue" alt="License: GPL-3.0"></a>
  <img src="https://img.shields.io/badge/platform-Windows%2010%2F11%20x64-0078D6" alt="Platform: Windows 10/11 x64">
</p>

<p align="center">
  <img src="assets/screenshots/migration-wizard.png" alt="Migration wizard, step 1: load the source backup and the factory-default backup of the target" width="800">
</p>

## Why Shiftwall

Replacing a FortiGate with a different model means renaming interfaces in hundreds of references, fixing the header
and checking that nothing points to ports the new unit does not have. Shiftwall does this in a guided wizard, shows
every change before writing anything, and never touches encrypted secrets. It runs entirely offline, so configurations
never leave your PC, and every model datum it uses comes from an official Fortinet source.

## Features

### Migration

- Five-step wizard: load, pre-checks, interface mapping (by name or drag and drop), transform, report and save.
- Target from a factory-default backup (recommended) or from the model database.
- Renames interfaces in every reference and VDOM, removes disk sections on diskless targets, replaces only the
  `config-version` section of the header, as in Fortinet's manual procedure.
- Validates the result, shows a before/after diff, exports an HTML report, prints to PDF and produces a manual checklist.
- Reusable mapping templates and batch conversion of many sites, with a simulation mode.
- `ENC` values (passwords, pre-shared keys, private keys) are copied byte for byte and checked afterwards.

### Toolbox

- **Compare configs**: differences by section.
- **Security audit**: 23 rules from Fortinet's Best Practices and RFCs, each with its source, and a score.
- **Unused objects**: finds them and writes a `delete` script.
- **Export policies**: policies, addresses, groups, services, VIPs and schedules to Excel or CSV.
- **Import from CSV**: addresses, services, their groups and VIPs to a CLI script, with validation.
- **VPN summary**: IPsec phase 1 and 2 of one site, or a comparison of two sites.
- **Anonymize config**: share a configuration safely; a password-protected map (AES-256-GCM) restores it.
- **CLI snippets**: VLAN, policy, VIP, static route and local user, as ready-to-review CLI.
- **Upgrade planning**: opens Fortinet's official upgrade tool, with no invented paths.
- **Backup from LAN** (optional, off by default): downloads a backup through the FortiOS REST API from a private address.

## Privacy

Shiftwall sends no data. It works completely offline.

| Shiftwall does | Shiftwall does not |
|---|---|
| Read the files you open and process them in memory | Send telemetry, analytics or crash reports |
| Write only the files you save, never over the original | Update itself or contact any server |
| Keep settings and an error log without configuration content | Decrypt `ENC` values or bypass licences and protections |
| Connect to a FortiGate only on the LAN page, when you press *Download* | Store the API token |

The build fails if code outside the LAN module uses a network client. Details: [SECURITY.md](SECURITY.md#privacy-policy).

## Download

1. Download `Shiftwall.exe` and `SHA256SUMS.txt` from the [latest release](https://github.com/BitJacker/shiftwall/releases/latest).
2. Check the hash in PowerShell: `Get-FileHash .\Shiftwall.exe -Algorithm SHA256` must match `SHA256SUMS.txt`.
3. The exe is not code-signed, so SmartScreen shows "Windows protected your PC": after checking the hash, choose
   *More info* → *Run anyway*.

Step-by-step instructions and the list of files Shiftwall writes: [docs/download-and-verify.md](docs/download-and-verify.md).
No installer and no administrator rights are needed (about 144 MB, Windows 10/11 x64).

## Quick start

1. Make sure both units run the **same FortiOS version**, and back up the old unit and the new one (factory default).
2. Start `Shiftwall.exe` and open **Migration**.
3. Drop the old backup on the left and the factory-default backup of the new unit on the right; read the pre-checks.
4. Press **Map by name** and complete the interface mapping.
5. Review the changes, save the new configuration, follow the checklist and test it in a lab.

Full guide: [docs/migration-guide.md](docs/migration-guide.md).

## Supported models and versions

Generated from the bundled database. Every datum has an official Fortinet source and a verification date in
[docs/SOURCES.md](docs/SOURCES.md). With a factory-default backup of the target, any model can be used as a target.

<!-- models:start (generated by update-db gen-docs, do not edit) -->
| Model | Interfaces | Disk | Default switch | FortiOS (release notes) |
|---|---|---|---|---|
| FG-100E | ✅ 22 | ⚠ | ⚠ | 6.2.0 – 7.2.14 |
| FG-100F | ✅ 28 | ✅ no | ⚠ | 6.2.4 – 8.0.2 |
| FG-101E | ✅ 22 | ⚠ | ⚠ | 6.2.0 – 7.2.14 |
| FG-101F | ✅ 28 | ✅ yes | ⚠ | 6.2.4 – 8.0.2 |
| FG-120G | ✅ 28 | ✅ no | ⚠ | 7.2.9 – 8.0.2 |
| FG-121G | ✅ 28 | ✅ yes | ⚠ | 7.2.9 – 8.0.2 |
| FG-200E | ✅ 20 | ⚠ | ⚠ | 6.2.0 – 8.0.2 |
| FG-200F | ✅ 28 | ✅ no | ⚠ | 6.4.7 – 8.0.2 |
| FG-200G | ✅ 28 | ✅ no | ⚠ | 7.4.8 – 8.0.2 |
| FG-201E | ✅ 20 | ⚠ | ⚠ | 6.2.0 – 8.0.2 |
| FG-201F | ✅ 28 | ✅ yes | ⚠ | 6.4.7 – 8.0.2 |
| FG-201G | ✅ 28 | ✅ yes | ⚠ | 7.6.4 – 8.0.2 |
| FG-30E | ⚠ | ⚠ | ⚠ | 6.2.0 – 6.2.17 |
| FG-30G | ✅ 4 | ✅ no | ⚠ | 7.4.10 – 8.0.2 |
| FG-31G | ✅ 4 | ✅ yes | ⚠ | 7.4.10 – 8.0.2 |
| FG-40F | ✅ 5 | ✅ no | ⚠ | 6.2.5 – 8.0.2 |
| FG-50E | ⚠ | ⚠ | ⚠ | 6.2.0 – 6.2.17 |
| FG-50G | ✅ 5 | ✅ no | ⚠ | 7.4.8 – 8.0.2 |
| FG-51G | ✅ 5 | ✅ yes | ⚠ | 7.4.8 – 8.0.2 |
| FG-60E | ⚠ | ⚠ | ⚠ | 6.2.0 – 7.4.13 |
| FG-60F | ✅ 10 | ✅ no | ⚠ | 6.2.4 – 8.0.2 |
| FG-61E | ⚠ | ⚠ | ⚠ | 6.2.0 – 7.4.13 |
| FG-61F | ✅ 10 | ✅ yes | ⚠ | 6.2.4 – 8.0.2 |
| FG-70F | ✅ 10 | ⚠ | ⚠ | 7.0.9 – 8.0.2 |
| FG-70G | ✅ 10 | ✅ no | ⚠ | 7.4.10 – 8.0.2 |
| FG-71F | ✅ 10 | ✅ yes | ⚠ | 7.0.9 – 8.0.2 |
| FG-71G | ✅ 10 | ✅ yes | ⚠ | 7.4.9 – 8.0.2 |
| FG-80E | ⚠ | ⚠ | ⚠ | 6.2.0 – 7.4.13 |
| FG-80F | ✅ 10 | ✅ no | ⚠ | 6.2.8 – 8.0.2 |
| FG-81E | ⚠ | ⚠ | ⚠ | 6.2.0 – 7.4.13 |
| FG-81F | ✅ 10 | ✅ yes | ⚠ | 6.2.8 – 8.0.2 |
| FG-90G | ✅ 10 | ✅ no | ⚠ | 7.2.9 – 8.0.2 |
| FG-91G | ✅ 10 | ✅ yes | ⚠ | 7.2.9 – 8.0.2 |
| FG-VM64 | ⚠ | ✅ yes | ⚠ | 6.2.0 – 8.0.2 |

✅ verified from an official source · ⚠ unverified (see [SOURCES.md](docs/SOURCES.md))
<!-- models:end -->

## Known limits

- Source and target must run the same FortiOS version: Shiftwall does not translate syntax between versions.
- Encrypted, YAML or password-masked backups cannot be used.
- Factory-default switch membership is not documented publicly for most models: load the factory-default backup.
- Upgrade paths are not computed; Fortinet's upgrade tool is linked instead.
- Tested on synthetic configurations and golden files; the LAN page was tested against a local test server, not yet
  against a real FortiGate. Reports from real migrations (anonymized) are welcome.
- The exe is not code-signed; Windows only (x64).
- Modified configurations are not supported by Fortinet TAC.

## FAQ

See [docs/faq.md](docs/faq.md).

## Building from source

Requires the [.NET SDK 10.0.404](https://dotnet.microsoft.com/download/dotnet/10.0) (pinned in `global.json`).

```bash
dotnet test -c Release
```

```bash
dotnet publish src/Shiftwall.App -c Release -o publish
```

The app runs on Windows; libraries and tests also build on Linux and macOS. Details in [CONTRIBUTING.md](CONTRIBUTING.md).

## Roadmap

- More models with verified interface lists, from official sources and contributed factory-default interface names.
- Feedback from real migrations to extend the pre-checks and the checklist.
- Code signing of the exe, to remove the SmartScreen warning.

## Contributing, license and disclaimer

Contributions are welcome: read [CONTRIBUTING.md](CONTRIBUTING.md) and the [code of conduct](CODE_OF_CONDUCT.md).
Report vulnerabilities privately as described in [SECURITY.md](SECURITY.md). Changes are listed in
[CHANGELOG.md](CHANGELOG.md).

Shiftwall is free software under the [GNU General Public License v3.0](LICENSE). Third-party components:
[THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).

> Shiftwall is not affiliated with Fortinet. FortiGate and FortiOS are trademarks of Fortinet, Inc. Modified
> configurations are not supported by Fortinet TAC: always test in a lab before production.
