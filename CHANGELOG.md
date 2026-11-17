# Changelog

All notable changes to Shiftwall are documented in this file.

The format is based on [Keep a Changelog 1.1.0](https://keepachangelog.com/en/1.1.0/), and this project adheres to
[Semantic Versioning 2.0.0](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

## [0.1.0] - not yet released (pre-release)

First public version, published as a GitHub pre-release. The date is set when the `v0.1.0` tag is created, after
testing in a lab.

### Added

- Migration wizard in five steps (load, pre-checks, interface mapping, transform, report and save) between FortiGate
  models running the same FortiOS version. Target from a factory-default backup (recommended) or from the model
  database. Interface renaming in every reference and VDOM, removal of disk sections on diskless targets, replacement of
  the `config-version` section of the header only, validation of the result, before/after diff, HTML report,
  print/PDF and a manual checklist. Mapping templates can be saved and reused. The interface mapping is suggested by
  name and role (WAN to WAN, LAN ports in order, e.g. FortiGate 50E `lan1` → 60F `internal1`) and must be checked.
- Batch conversion of several sites with the same mapping template, with a simulation mode.
- Model database with 112 models (desktop to data center, from the Hardware Acceleration guide) and 96 FortiOS releases (6.2.0 – 8.0.2). Every datum cites its official Fortinet
  source and verification date; unverified data shows a warning. An updated database can be imported and is validated
  first.
- Offline toolbox: configuration compare, security audit (23 rules with sources and a score), unused objects with a
  delete script, policy and object export to Excel/CSV, CSV import to CLI script, IPsec VPN summary and two-site
  comparison, anonymization with a reversible map encrypted with AES-256-GCM, CLI snippet generator, upgrade planning
  (links to Fortinet's official upgrade tool).
- Optional *Backup from LAN* page (off by default): downloads a backup through the FortiOS REST API from a private IP
  address, with certificate fingerprint confirmation; the API token is never stored.
- English and Italian user interface, light and dark theme.
- Portable single-file `Shiftwall.exe` for Windows 10/11 x64, with `SHA256SUMS.txt`.

### Security

- No telemetry, analytics, online crash reporting or automatic updates; no network access outside the LAN page,
  enforced by automated tests.
- `ENC` values are copied unchanged and never decrypted; configuration content never reaches the logs.
- Secret scanning with gitleaks in CI and as an optional pre-commit hook.

[Unreleased]: https://github.com/BitJacker/shiftwall/compare/v0.1.0...HEAD
[0.1.0]: https://github.com/BitJacker/shiftwall/releases/tag/v0.1.0
