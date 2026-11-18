#!/usr/bin/env bash
# Scans the whole git history for secrets with gitleaks (MIT, https://github.com/gitleaks/gitleaks).
# The binary is downloaded from the official release and checked against the SHA256 published in
# gitleaks_<version>_checksums.txt. Configuration: .gitleaks.toml at the repository root.
# Usage: tools/build/gitleaks.sh [repository path]   (Linux x64)
set -euo pipefail

VERSION="8.30.1"
SHA256="551f6fc83ea457d62a0d98237cbad105af8d557003051f41f3e7ca7b3f2470eb"
ARCHIVE="gitleaks_${VERSION}_linux_x64.tar.gz"
URL="https://github.com/gitleaks/gitleaks/releases/download/v${VERSION}/${ARCHIVE}"

repo="$(cd "${1:-.}" && pwd)"
work="$(mktemp -d)"
trap 'rm -rf "$work"' EXIT

curl -fsSL --retry 3 -o "$work/$ARCHIVE" "$URL"
echo "$SHA256  $work/$ARCHIVE" | sha256sum --check --strict
tar -xzf "$work/$ARCHIVE" -C "$work" gitleaks

"$work/gitleaks" git --redact --verbose --config "$repo/.gitleaks.toml" "$repo"
