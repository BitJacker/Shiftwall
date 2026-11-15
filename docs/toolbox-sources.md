# Toolbox: sources

Every rule, default value and protocol detail used by the toolbox comes from the documents below.
Fortinet documents were read on 2026-11-15. Where Fortinet does not publish a detail in a public document, this page
says so and explains how Shiftwall avoids guessing.

## Security audit

| Rule | Severity | Sources |
|---|---|---|
| AUD-001 HTTP/Telnet admin on an internet-facing interface | High | [FortiOS 8.0.0 Best Practices][bp] — Administrative settings; Blocking external access to administrative ports |
| AUD-002 HTTP/Telnet admin enabled | Medium | [Best Practices][bp] — Administrative settings; Encrypted protocols |
| AUD-003 Admin access from the internet without trusted hosts or local-in policy | Medium | [Best Practices][bp] — Blocking external access to administrative ports; How can users access the FortiGate |
| AUD-010 Any-to-any accept policy (not to the internet) | High | [Best Practices][bp] — Policies › Security policies ("Do not use the all or any objects in a policy, except when routing to the internet") |
| AUD-011 Policy on the "any" interface | Medium | [Best Practices][bp] — Security policies |
| AUD-012 Duplicate policy | Medium | [Best Practices][bp] — Security policies ("there should not be any unused policies") |
| AUD-013 Shadowed policy | Medium | [Best Practices][bp] — Security policies (evaluation order) |
| AUD-014 Disabled policy | Low | [Best Practices][bp] — Security policies |
| AUD-020 Traffic logging disabled | Medium | [Best Practices][bp] — Logging and reporting; [CLI Reference 7.6.8][cli] — `config firewall policy` (logtraffic default `utm`) |
| AUD-021 Internet traffic without security profiles | Medium | [Best Practices][bp] — Security profiles |
| AUD-030 Default `admin` account | Medium | [Best Practices][bp] — Non-standard admin ports and administrator usernames |
| AUD-031 Administrator without trusted hosts | Low | [Best Practices][bp] — How can users access the FortiGate |
| AUD-032 Administrator without MFA | Low | [Best Practices][bp] — How can users access the FortiGate |
| AUD-033 Idle timeout above 10 minutes | Low | [Best Practices][bp] — Administrative settings; [CLI Reference][cli] — `admintimeout` (default 5) |
| AUD-034 Default admin ports | Low | [Best Practices][bp] — Administrative settings; Non-standard admin ports |
| AUD-040 DES, MD5, null in IPsec proposals | High | [RFC 8221][rfc8221], [RFC 8247][rfc8247] (MUST NOT); [Best Practices][bp] — Strong ciphers |
| AUD-041 3DES, SHA-1 in IPsec proposals | Medium | [RFC 8221][rfc8221], [RFC 8247][rfc8247] |
| AUD-042 DH groups 1, 22 | High | [RFC 8247][rfc8247] §2.4 (MUST NOT) |
| AUD-043 DH groups 2, 5, 23, 24 | Medium | [RFC 8247][rfc8247] §2.4 (SHOULD NOT) |
| AUD-044 strong-crypto disabled | High | [Best Practices][bp] — Strong ciphers; [CLI Reference][cli] — `config system global` |
| AUD-045 TLS 1.0/1.1 allowed | Medium | [RFC 8996][rfc8996]; [CLI Reference][cli] — `admin-https-ssl-versions`, `ssl-min-proto-version`; [CLI Reference 7.4.10 — vpn ssl settings][ssl] |
| AUD-050 NTP not configured | Medium | [Best Practices][bp] — System time; [CLI Reference][cli] — `config system ntp` (`ntpsync` default `disable`, also in [7.4.2][ntp742]) |
| AUD-051 DNS not configured | Low | [CLI Reference][cli] — `config system dns` (`primary` default `0.0.0.0`) |

A standard backup lists only values that differ from the default. A missing value is treated as the documented
default only for FortiOS 7.4 and later (the versions whose CLI reference was checked); for older or unknown versions
the NTP/DNS checks are skipped and the report says so. Groups are compared by name (not expanded) when looking for
duplicate and shadowed policies.

Score: 100 minus 15 for each failed high rule, 8 for each medium and 3 for each low rule (each rule counts once).

## Unused objects

Script syntax `delete <table>`: [FortiOS Handbook — Sub-commands][sub]. Groups are deleted before their members.

## CSV import and CLI snippets

Field names, value options and limits (names up to 79 characters, interface names 15, policy names 35, local user
names 64, VLAN ID 1-4094, static route distance 1-255): [FortiOS 7.6.8 CLI Reference][cli]. Port range syntax
`dst[-dst][:src[-src]]`, at most 16 ranges: [FortiOS 6.0 CLI Reference — firewall service custom][svc].
`edit 0` assigns the next free ID: [Fortinet Community — Using the "edit 0" option][edit0].

## Anonymization map

AES-256-GCM, key derived with PBKDF2-HMAC-SHA256 at 600,000 iterations, as recommended by the
[OWASP Password Storage Cheat Sheet][owasp]. Aliases use reserved ranges: RFC 2544 (198.18.0.0/15) for public IPv4,
RFC 6598 (100.64.0.0/10) for private IPv4, RFC 3849 (2001:db8::/32) for IPv6.

## Backup from LAN (opt-in)

- Token in the `Authorization: Bearer` header, REST API administrator: FortiOS 7.6.5 Administration Guide,
  [Using APIs][apis] and [REST API administrator][apiadmin].
- Endpoint `/api/v2/monitor/system/config/backup?scope=global` (or `scope=vdom&vdom=<name>`): Fortinet Community,
  [Get backup config file using RestAPI][kb1] and [backup from a non-management VDOM][kb2].
- HTTP method: the Community articles disagree for FortiOS 7.6 ([GET in kb1][kb1], [POST from 7.6.0][kb405]).
  Shiftwall sends POST and falls back to GET only when the device answers 405.
- FortiOS 8.0.1 and later require a protection mode for exported secrets: [Encrypted configuration export 8.0.1][exp801].
  The API parameters `password` and `encrypt_mode=sensitive` come from [kb1].
- Fortinet's complete API reference (FNDN) requires a Fortinet account and was not consulted.

[bp]: https://fortinetweb.s3.amazonaws.com/docs.fortinet.com/v2/attachments/32eccc66-2e05-11f1-b31d-02356ffb40d9/FortiOS-8.0.0-Best_Practices.pdf
[cli]: https://fortinetweb.s3.amazonaws.com/docs.fortinet.com/v2/attachments/1c34e924-cbe3-11f1-8c5a-8e4c95ff11ac/FortiOS-7.6.8-CLI_Reference.pdf
[ssl]: https://docs.fortinet.com/document/fortigate/7.4.10/cli-reference/114404382
[ntp742]: https://docs.fortinet.com/document/fortigate/7.4.2/cli-reference/112620
[rfc8221]: https://www.rfc-editor.org/rfc/rfc8221
[rfc8247]: https://www.rfc-editor.org/rfc/rfc8247
[rfc8996]: https://www.rfc-editor.org/rfc/rfc8996
[sub]: https://docs.fortinet.com/document/fortigate/6.0.0/handbook/753045/sub-commands
[svc]: https://docs.fortinet.com/document/fortigate/6.0.0/cli-reference/398367
[edit0]: https://community.fortinet.com/t5/FortiGate/Technical-Tip-Using-the-quot-edit-0-quot-option-during-FortiGate/ta-p/190761
[owasp]: https://cheatsheetseries.owasp.org/cheatsheets/Password_Storage_Cheat_Sheet.html
[apis]: https://docs.fortinet.com/document/fortigate/7.6.5/administration-guide/940602
[apiadmin]: https://docs.fortinet.com/document/fortigate/7.6.5/administration-guide/399023
[kb1]: https://community.fortinet.com/t5/FortiGate/Technical-Tip-Get-backup-config-file-on-FortiGate-using-RestAPI/ta-p/202286
[kb2]: https://community.fortinet.com/fortigate-3/technical-tip-how-to-get-a-backup-config-file-on-fortigate-by-using-a-python-script-from-a-non-management-vdom-115466
[kb405]: https://community.fortinet.com/t5/FortiGate/Troubleshooting-Tip-Resolving-405-Method-Not-Allowed-Error-When/ta-p/417350
[exp801]: https://docs.fortinet.com/document/fortigate/8.0.0/new-features/241169/encrypted-configuration-export-8-0-1
