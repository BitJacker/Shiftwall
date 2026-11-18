# Launch checklist (maintainer)

Steps that need the repository owner. Everything else is in the repository.

## 1. Repository settings (GitHub CLI)

Read [github-setup.sh](github-setup.sh), then run it once:

```bash
bash docs/launch/github-setup.sh
```

It sets the description and topics, enables Discussions, creates the labels `good first issue`, `help wanted`,
`new-model`, `docs` and opens the five issues in [issues/](issues/).

## 2. Discussions categories

When Discussions is enabled, GitHub creates the default categories Announcements, General, Ideas, Polls, Q&A and
Show and tell ([GitHub Docs](https://docs.github.com/en/discussions/managing-discussions-for-your-community/managing-categories-for-discussions)).
Q&A, Ideas and Show and tell are the ones Shiftwall needs; delete or keep the others in *Discussions → Categories*
(pencil icon).

## 3. Security

In *Settings → Advanced Security*
([GitHub Docs](https://docs.github.com/en/code-security/secret-scanning/enabling-secret-scanning-features/enabling-push-protection-for-your-repository)):

- **Private vulnerability reporting**: Enable (used by SECURITY.md).
- **Secret Protection**: Enable, then **Push protection**: Enable.
- **Dependabot alerts**: Enable (version updates are configured in `.github/dependabot.yml`).

## 4. Social preview

*Settings → General → Social preview → Edit → Upload an image*: `assets/brand/social-preview.png` (1280 × 640 px,
305 KB; GitHub requires PNG/JPG/GIF under 1 MB,
[GitHub Docs](https://docs.github.com/en/repositories/managing-your-repositorys-settings-and-features/customizing-your-repository/customizing-your-repositorys-social-media-preview)).

## 5. Demo GIF

Record it following [docs/RECORDING.md](../RECORDING.md).

## 6. First release (only after testing the exe in a lab)

1. Merge the work to `main` and wait for CI to pass.
2. In `CHANGELOG.md` replace "not yet released (pre-release)" with the date.
3. Tag and push:

```bash
git tag v0.1.0
```

```bash
git push origin v0.1.0
```

The release workflow publishes `Shiftwall.exe` and `SHA256SUMS.txt` as a **pre-release** (all 0.x versions are).
GitHub's `releases/latest` links skip pre-releases, so the README's direct download link starts working with the first
stable release.

## 7. Show HN

Draft in [show-hn.md](show-hn.md). Post it only when the app is stable and tested on real devices.
