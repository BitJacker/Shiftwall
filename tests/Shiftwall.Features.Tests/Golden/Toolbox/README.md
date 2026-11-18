# Synthetic toolbox fixtures

Both files are synthetic, written by hand for the toolbox tests (conventions in
tests/Shiftwall.Core.Tests/Golden/Parser/README.md): RFC 5737 documentation addresses stand in for public IPs,
RFC 1918 for private ones, `example.com` for domains, and every `ENC` value is the base64 of
"Shiftwall synthetic test value - not a secret". Names of people are invented.

- `site-a.fgtcfg`: an "HQ" unit with deliberate audit findings (HTTP/Telnet admin access, default admin, duplicate,
  shadowed and disabled policies, weak IPsec proposals, TLS 1.1, strong-crypto disabled, no NTP), unused objects and
  an IPsec tunnel to site B.
- `site-b.fgtcfg`: the "branch" end of the same tunnel, with a phase 2 proposal that does not match site A.
