# Synthetic migration fixtures

All files are synthetic (see tests/Shiftwall.Core.Tests/Golden/Parser/README.md for the conventions).
`branch-source.fgtcfg` uses 60F-style port names and `target-default.fgtcfg` 70G-style names, but neither is a
copy of a real backup and neither is used as model data. `branch-expected.fgtcfg` is the reviewed output of the
migration with `branch-mapping.json`: any change to it must be reviewed line by line.

`multivdom-source.fgtcfg` is a synthetic multi-VDOM backup (interfaces in `config global`, policies and static routes
in two VDOMs, a VLAN on a mapped port, an HA heartbeat device). `multivdom-expected.fgtcfg` is its reviewed output with
`multivdom-mapping.json` and the synthetic target `multivdom-target.fgtcfg`: only the config-version section of the
first line changes (the `vdom=1` flag and the other header lines stay), and every interface reference is renamed.
