## What and why

<!-- What does this pull request change, and why? Link the issue if there is one. -->

## How it was tested

<!-- Tests added or updated, manual checks (Windows version, pages used). -->

## Checklist

- [ ] No real configuration, backup, token or public IP address is included (test files are synthetic `.fgtcfg`).
- [ ] No network code outside `src/Shiftwall.Lan`; `ENC` values are never decrypted or changed.
- [ ] New data, rules or defaults cite an official source (`data/*.json` or `docs/`), and `update-db validate` and
      `gen-docs` were run.
- [ ] User-visible strings are in both English and Italian resources.
- [ ] Tests pass (`dotnet test -c Release`).
- [ ] `CHANGELOG.md` updated (*Unreleased*) for user-visible changes.
- [ ] Commits follow Conventional Commits.
