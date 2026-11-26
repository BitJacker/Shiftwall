# cli-schema

Builds the CLI schemas in `data/schemas/` from the official FortiOS CLI Reference and checks that every difference
between two schemas has a translation rule in `data/rules/`. Developer tool: it is not part of the application and
the application never makes network calls.

```bash
# FortiOS 6.2 and later: HTML CLI Reference on docs.fortinet.com
python3 build_schema.py html --version 8.0 --doc 8.0.0 --out ../../data/schemas/8.0.json --cache /tmp/cli-schema-cache
# FortiOS 5.2: PDF CLI Reference (needs pdftotext from poppler-utils)
python3 build_schema.py pdf --version 5.2 --pdf fortigate-cli-52.pdf --url <PDF URL> --title "FortiOS CLI Reference for FortiOS 5.2 (2018-03-05)" --out ../../data/schemas/5.2.json
# Differences without a rule (the test suite fails while there are any)
python3 diff_rules.py --from 5.2 --to 8.0
```

Only structured data is stored: paths, sub-tables, parameter names, types, sizes, defaults and option names, with
the URL of the reference and the date it was read. No description text is copied.

Known limits of the 5.2 PDF: the syntax blocks do not mark every multi-value parameter and some option lists are
abbreviated. The 5.2 schema is used to detect differences, never to validate output; output is validated on the
schema of the target version.
