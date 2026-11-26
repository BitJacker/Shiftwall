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
- Rebuild mode for different FortiOS versions, first pair 5.2 → 8.0: CLI schemas built from the official CLI
  Reference (`tools/cli-schema`), translation rules with sources (`data/rules`) and a test that fails when a schema
  difference has no rule; output checked on the target schema, objects written in dependency order with the
  management interface last, references checked before export, final lint before saving; scripts for the GUI (Run
  Script) and for the terminal; passwords typed or generated per migration and saved only encrypted; SSL VPN tunnel
  mode converted to IPsec dial-up (FortiClient, IKEv2, EAP); interface mapping with logical targets (e.g. the `lan`
  switch of a 40F) and per-interface addressing (kept, static, DHCP). Guides: `docs/load-scripts.md`, `docs/LAB-TEST.md`.
- Email 2FA and rebuild passwords are generated from characters the FortiOS CLI accepts as typed or pasted (no `?`, `#`,
  quotes, backslash or spaces; CLI basics, Special characters).
- Batch conversion of several sites with the same mapping template, with a simulation mode.
- Model database with 112 models (desktop to data center, from the Hardware Acceleration guide) and 96 FortiOS releases (6.2.0 – 8.0.2). Every datum cites its official Fortinet
  source and verification date; unverified data shows a warning. An updated database can be imported and is validated
  first.
- Offline toolbox: configuration compare, security audit (23 rules with sources and a score), unused objects with a
  delete script, policy and object export to Excel/CSV, CSV import to CLI script, IPsec VPN summary and two-site
  comparison, anonymization with a reversible map encrypted with AES-256-GCM, CLI snippet generator, upgrade planning
  (links to Fortinet's official upgrade tool).
- *Email 2FA* page: bulk `email-to` and `two-factor email` for local users (per VDOM) and administrators, from the
  table or a CSV/Excel list (`samples/email-2fa.csv`). Validation (emails, unknown or ambiguous accounts, duplicates,
  FortiToken holders confirmed row by row, never every super_admin, missing email server blocks), dry run, CLI scripts
  in batches (default 500) with a rollback each, optional email expiry timers, random passwords of new users only in an
  encrypted file, HTML/CSV report and account export. Optional read-only list from the LAN (GET on cmdb user/local and
  system/admin). Sources in `docs/toolbox-sources.md`.
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
