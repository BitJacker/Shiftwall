# Contributing to Shiftwall

Thank you for helping. By contributing you agree that your work is released under the [GPL-3.0](LICENSE) and that you
follow the [code of conduct](CODE_OF_CONDUCT.md).

## Quick setup

With [Git](https://git-scm.com/) and the [.NET SDK 10.0.404](https://dotnet.microsoft.com/download/dotnet/10.0):

```bash
git clone https://github.com/BitJacker/shiftwall.git
```

```bash
cd shiftwall
```

```bash
dotnet test -c Release
```

That builds everything and runs all the tests. On Windows, start the app with
`dotnet run --project src/Shiftwall.App`.

## Response times

Shiftwall is maintained in spare time. Expect a first answer to issues and pull requests **within a few days**,
sometimes longer. Security reports (see [SECURITY.md](SECURITY.md)) are read first.

## Golden rules

- **Never commit real configurations or backups**, not even "harmless" ones. Test files are synthetic and use the
  `.fgtcfg` extension under `tests/**/Golden/`. `.gitignore` excludes `*.conf`, `*.cfg` and backups.
- **No network code** outside `src/Shiftwall.Lan`. A test fails if you add one.
- **Never decrypt or alter `ENC` values.** They are copied byte for byte.
- **No invented data.** Every model or FortiOS datum needs an official Fortinet source (see below).
- **No Fortinet code, text or assets** (FortiConverter included), and no Fortinet logos or colors.
- Configuration content never goes into logs or exception messages.

## Build and test

The SDK version is pinned in `global.json`. The app is WPF and runs on Windows; libraries and tests also build on
Linux and macOS. Single-file exe (Windows):

```bash
dotnet publish src/Shiftwall.App -c Release -o publish
```

It produces `publish/Shiftwall.exe`.

Secret scan, the same one CI runs (Linux, downloads gitleaks and checks its SHA256):

```bash
tools/build/gitleaks.sh
```

Optional pre-commit hook: install [pre-commit](https://pre-commit.com/#install), then run `pre-commit install`.

## Adding or updating a FortiGate model

The database lives in `data/` (`models.json`, `fortios-releases.json`, `upgrade-paths.json`) and is maintained with
the `tools/update-db` developer tool. Every field has a `status` (`verified` or `unverified`) and a list of `sources`:

```json
"has_disk": {
  "value": false,
  "status": "verified",
  "sources": [
    {
      "url": "https://www.fortinet.com/content/dam/fortinet/assets/data-sheets/pdf/fortigate-fortiwifi-40f-series.pdf",
      "title": "FortiGate/FortiWiFi 40F Series Data Sheet",
      "verified_on": "2026-11-15",
      "note": "Onboard Storage: 0"
    }
  ]
}
```

A **source is mandatory** for a `verified` field: an official Fortinet URL (`docs.fortinet.com`, `www.fortinet.com`
data sheets, Fortinet Community technical tips on `community.fortinet.com`, or the document attachments on
`fortinetweb.s3.amazonaws.com` linked from docs.fortinet.com), its title and the date you checked
it. Third-party forums, blogs and memory are not sources; `validate` rejects other hosts. If you cannot find an official source, set `"status": "unverified"`, leave
`value` empty and explain why in `note`: the app then shows a warning.

Steps:

1. Edit `data/*.json`, or use the tool:
   - `dotnet run --project tools/update-db -- fetch-releases` reads the official release notes (build, date,
     supported models);
   - `dotnet run --project tools/update-db -- refresh-hw-ports` compares port lists with the cited pages;
   - `dotnet run --project tools/update-db -- import-default-backup --model <id> --file <path>` takes **only the
     interface names** from a real factory-default backup on your disk. The backup itself must never be committed.
2. `dotnet run --project tools/update-db -- normalize` (canonical formatting).
3. `dotnet run --project tools/update-db -- validate` (the same rules the app applies at load time).
4. `dotnet run --project tools/update-db -- gen-docs` (updates `docs/SOURCES.md`, `docs/supported-models.md` and the
   model tables in the READMEs). CI fails if you forget it.
5. Open a pull request: reviewers check every new URL.

You can also open a ["New FortiGate model" issue](https://github.com/BitJacker/shiftwall/issues/new/choose) with the
sources, and a maintainer will add it.

## Code style

- C# with nullable reference types; warnings are errors. Keep the existing style: file-scoped namespaces, `var` where
  the type is obvious, small classes.
- Engine code (`Shiftwall.Core`, `Shiftwall.Features`) has no UI and no network dependencies; view models live in
  `Shiftwall.Presentation`, WPF views in `Shiftwall.App`.
- Every user-visible string is in the resource files, in **English and Italian** (`*.resx` and `*.it.resx`). A test
  checks that both have the same keys.
- Every rule, default or limit taken from Fortinet or an RFC cites its source in `docs/` (see
  [docs/toolbox-sources.md](docs/toolbox-sources.md)).
- Add tests for every change. Behaviour on whole files is covered by golden tests (`tests/**/Golden/`).

## Commits and pull requests

- Small commits, in English, following [Conventional Commits](https://www.conventionalcommits.org/en/v1.0.0/)
  (`feat:`, `fix:`, `docs:`, `test:`, `ci:`, `build:`, `chore:`).
- Fill in the pull request template; CI (tests, secret scan, Windows build and smoke test) must pass.
- Add a line to the *Unreleased* section of [CHANGELOG.md](CHANGELOG.md) for user-visible changes.

## Releases (maintainers)

1. Move *Unreleased* to the new version in `CHANGELOG.md`, set `<Version>` in `Directory.Build.props` and write the
   release text in `docs/releases/v<version>.md`.
2. Tag the commit on `main`: `git tag v<version>` and push the tag. The release workflow builds, tests and publishes
   `Shiftwall.exe` and `SHA256SUMS.txt`. Versions 0.x are published as pre-releases.
