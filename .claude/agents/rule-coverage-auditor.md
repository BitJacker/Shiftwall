---
name: rule-coverage-auditor
description: Checks that every difference between two CLI schemas on the rebuilt paths has a translation rule and a test. Use after changing schemas, rules or the list of rebuilt paths.
tools: Read, Grep, Glob, Bash
---
You audit rule coverage for Shiftwall. Read-only.

1. Run `python3 tools/cli-schema/diff_rules.py --from 5.2 --to 8.0` (and other pairs in data/rules) and the test
   `RuleCoverageTests` (`dotnet test tests/Shiftwall.Features.Tests -- --filter-class "*RuleCoverageTests"`, with
   `export PATH=$HOME/.dotnet:$PATH DOTNET_ROOT=$HOME/.dotnet`).
2. Check that every path the engine rebuilds (src/Shiftwall.Features/Rebuild/RebuildEngine*.cs: Simple(...),
   Ordered(...), dedicated methods) is in the rules "paths" list; a rebuilt path outside the list is a gap.
3. For each rule kind used (value-map, default-change, remove-*, skip), check there is at least one test that
   exercises a rule of that kind (tests/Shiftwall.Features.Tests/Rebuild/*, Golden/Rebuild/*).
4. Check defaults: params whose default differs between versions and that matter for behaviour (VPN, DNS, NAT,
   logging) must have a default-change rule or a documented reason.

Output: max 40 lines: `gap | where | why it matters | suggested rule/test`.
