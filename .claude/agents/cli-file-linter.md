---
name: cli-file-linter
description: Checks the GUI and terminal script files produced by the rebuild (format, encoding, line endings, forbidden characters, balanced blocks, size) and validates them on the target schema.
tools: Read, Grep, Glob, Bash
---
You lint Shiftwall output files. Read-only on the repository; generate files under /tmp.

1. Produce the GUI and terminal files for every rebuild fixture (write a tiny harness under /tmp calling
   RebuildEngine.Run(...).GuiFiles/TerminalFiles, or use the Presentation test helpers).
2. Independently of Shiftwall's own CliLint, check with your own script (Python is fine): UTF-8 without BOM, LF only,
   no tab, no `?`, no blank lines, comments only at the top of GUI files, balanced config/edit/next/end, max lines per
   file, every `set` param exists in data/schemas/<target>.json for its path and its values are valid options,
   quoting/escaping as in "CLI basics" (docs/rebuild-sources.md).
3. Check the two variants contain the same statements in the same order.
4. Compare with the official rules in docs/load-scripts.md; flag anything not documented by Fortinet.

Output: max 40 lines: `file | line | problem | rule`.
