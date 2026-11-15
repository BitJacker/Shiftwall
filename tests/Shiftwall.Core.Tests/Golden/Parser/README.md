# Synthetic parser fixtures

Every file here is **synthetic**, written by hand for the tests. None comes from a real device:

- IP addresses use the RFC 5737 documentation ranges (192.0.2.0/24, 198.51.100.0/24, 203.0.113.0/24)
  and RFC 1918 lab ranges; names use `example.com`.
- `ENC` values are base64 of the text "Shiftwall synthetic test value - not a secret".
- Header platform codes and build numbers are illustrative only; they are not used as model data.

The extension `.fgtcfg` is used so that `.gitignore` (which blocks `*.conf`) never needs an exception.
Never add real configurations here: use the Anonymize tool and still rewrite them by hand.

`default-synthetic.fgtcfg` imitates the *shape* of a factory-default backup to test interface and
virtual-switch extraction. Its ports and switch members are invented and must never be used as model data.
