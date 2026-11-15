# Synthetic migration fixtures

All files are synthetic (see tests/Shiftwall.Core.Tests/Golden/Parser/README.md for the conventions).
`branch-source.fgtcfg` uses 60F-style port names and `target-default.fgtcfg` 70G-style names, but neither is a
copy of a real backup and neither is used as model data. `branch-expected.fgtcfg` is the reviewed output of the
migration with `branch-mapping.json`: any change to it must be reviewed line by line.
