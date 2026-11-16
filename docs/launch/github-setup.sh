#!/usr/bin/env bash
# One-off GitHub setup for BitJacker/shiftwall, to run once by the maintainer with the GitHub CLI
# (https://cli.github.com, `gh auth login` first). Read it before running: it only edits repository settings,
# labels and issues; it downloads and executes nothing.
set -euo pipefail
R="BitJacker/shiftwall"

# Description (same sentence as under the logo in README.md) and topics.
gh repo edit "$R" \
  --description "Migrate FortiGate configurations between models, offline, plus a sysadmin toolbox. One portable Windows exe, no telemetry." \
  --add-topic fortigate --add-topic fortios --add-topic firewall --add-topic network-automation \
  --add-topic migration --add-topic sysadmin --add-topic windows --add-topic offline \
  --add-topic wpf --add-topic dotnet

# Discussions (categories are created in the web UI, see docs/launch/README.md).
gh repo edit "$R" --enable-discussions

# Labels (--force updates a label that already exists).
gh label create "good first issue" --repo "$R" --color 7057ff --description "Good for newcomers" --force
gh label create "help wanted" --repo "$R" --color 008672 --description "Extra attention is needed" --force
gh label create "new-model" --repo "$R" --color 1d76db --description "Model database: add or correct a model, with official sources" --force
gh label create "docs" --repo "$R" --color 0e8a16 --description "Documentation" --force

# Good first issues: texts in docs/launch/issues/.
D="$(dirname "$0")/issues"
gh issue create --repo "$R" --title "Model database: disk status of FG-70F, FG-100E, FG-101E, FG-200E, FG-201E" \
  --label "good first issue" --label "new-model" --body-file "$D/01-disk-status.md"
gh issue create --repo "$R" --title "Model database: factory-default switch membership from real default backups" \
  --label "help wanted" --label "new-model" --body-file "$D/02-default-switches.md"
gh issue create --repo "$R" --title "Audit: add a rule from the FortiOS Best Practices not covered yet" \
  --label "good first issue" --label "help wanted" --body-file "$D/03-audit-rule.md"
gh issue create --repo "$R" --title "Translate the user interface into a third language" \
  --label "help wanted" --body-file "$D/04-translation.md"
gh issue create --repo "$R" --title "Migration guide: add a screenshot of each wizard step" \
  --label "good first issue" --label "docs" --body-file "$D/05-guide-screenshots.md"
