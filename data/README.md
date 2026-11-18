# Shiftwall model database

These JSON files are embedded in `Shiftwall.exe` and can be replaced at runtime by importing a file.
They are validated at load time and in CI (`DatabaseValidator`): invalid data is rejected, never used.

| File | Content |
|---|---|
| `models.json` | FortiGate models: FortiOS interface names, disk, default hardware/virtual switch |
| `fortios-releases.json` | FortiOS releases: build, release date, supported models |
| `upgrade-paths.json` | Upgrade path policy: link to the official Upgrade Path Tool, no scraped paths |

## The rule: no invented data

Every datum is a *sourced value*:

```json
"has_disk": {
  "value": true,
  "status": "verified",
  "sources": [
    { "url": "https://www.fortinet.com/…/fortigate-fortiwifi-60f-series.pdf",
      "title": "FortiGate/FortiWiFi 60F Series Data Sheet",
      "verified_on": "2026-11-15",
      "note": "Internal Storage: 1 x 128 GB SSD (FG-61F)" }
  ]
}
```

- `verified` needs at least one source on an official Fortinet host (`docs.fortinet.com`, `www.fortinet.com`,
  `community.fortinet.com`, or docs.fortinet.com attachments on `fortinetweb.s3.amazonaws.com`), with the date
  it was checked.
- `unverified` must carry **no value** and a `note` explaining what is missing. The app shows a warning.
- A real factory-default backup of the target model always takes precedence over this database.
  Such backups are used locally only and are never committed: only interface names may end up here.

## Where the data comes from

- **Interface names and speeds:** the `diagnose npu … port-list` output printed in the FortiOS
  *Hardware Acceleration* guide. That output only lists interfaces attached to the NP processor: when a model
  has extra ports (e.g. HA, MGMT) not in the list, the model's `interfaces.note` says so.
- **Disk:** the official data sheet (some only exist in a localized edition on fortinet.com).
- **Releases:** the release notes of each version (build, *Supported models*, *Initial release* date in the
  *Change Log*).
- **Upgrade paths:** Fortinet publishes them only in the interactive Upgrade Path Tool, so none are listed.

## Updating

```bash
dotnet run --project tools/update-db -- fetch-releases            # add new FortiOS releases
dotnet run --project tools/update-db -- refresh-hw-ports          # re-check documented port lists
dotnet run --project tools/update-db -- check-links               # every source URL still answers
dotnet run --project tools/update-db -- validate
dotnet run --project tools/update-db -- gen-docs                  # docs/SOURCES.md, docs/supported-models.md
```

Review every change with `git diff data/` before committing.
