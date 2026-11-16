# Samples

Synthetic FortiGate configurations for trying Shiftwall. They were written by hand for the project: no file comes
from a real device. Public addresses use the RFC 5737 documentation ranges, private ones RFC 1918, domains are
`example.com`, and every `ENC` value is the base64 of a test string ("Shiftwall synthetic test value - not a secret").
A test (`tests/Shiftwall.Features.Tests/SamplesTests.cs`) fails if a sample breaks these rules.

| File | Use it for |
|---|---|
| `branch-60f.conf` | Migration source: 60F-style port names, FortiOS 7.4.13, a VLAN, an IPsec tunnel, a DMZ port to drop. |
| `factory-default-70g.conf` | Migration target: a factory-default-style backup with 70G-style port names, FortiOS 7.4.13. |
| `branch-60f-to-70g.swmap.json` | Mapping template for the two files above (*Load template…* in step 3). |
| `hq-60f.conf` | Toolbox: deliberate audit findings, unused objects, policies to export, an IPsec tunnel. |

Steps: see "Try it in one minute" in the [README](../README.md#try-it-in-one-minute).

**Never add real configurations here**, not even anonymized ones.

---

Configurazioni FortiGate sintetiche per provare Shiftwall, scritte a mano: nessun file viene da un apparato reale.
Passi: sezione "Provalo in 1 minuto" del [README in italiano](../README.it.md#provalo-in-1-minuto).
**Non aggiungere mai configurazioni reali**, nemmeno anonimizzate.
