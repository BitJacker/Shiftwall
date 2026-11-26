# Sources of the rebuild mode

Every rule, command and value used by the rebuild mode comes from an official Fortinet document. Verified on
2026-11-26. No text of these documents is copied into Shiftwall: the schemas keep only paths, parameter names,
types, sizes, defaults and option names; the rules cite the page that supports them.

## CLI schemas (data/schemas)

| Version | Document | Built with |
|---|---|---|
| 5.2 | [FortiOS CLI Reference for FortiOS 5.2 (PDF, 2018-03-05)](https://fortinetweb.s3.amazonaws.com/docs.fortinet.com/v2/attachments/27300d29-1a11-11e9-9685-f8bc1258b856/fortigate-cli-52.pdf) | `tools/cli-schema/build_schema.py pdf` (syntax blocks; 332 paths) |
| 8.0 | [FortiOS 8.0.0 CLI Reference](https://docs.fortinet.com/document/fortigate/8.0.0/cli-reference) (the 8.0.1 and 8.0.2 links redirect to it; the PDF is labelled 8.0.2) | `tools/cli-schema/build_schema.py html` (710 config pages; 663 paths) |

The 5.2 schema is used to find differences, never to validate output. Output is validated on the 8.0 schema.

## Translation rules 5.2 → 8.0 (data/rules/5.2-8.0.json)

Every difference between the two schemas on the paths Shiftwall rebuilds has a rule; the test
`RuleCoverageTests.EveryDifferenceHasARule` fails otherwise. Rules written by hand:

| Rule | What | Source |
|---|---|---|
| V001 | `firewall service custom` `protocol TCP/UDP/SCTP` → `TCP/UDP/UDP-Lite/SCTP` | [7.6.0 New Features: Support UDP-Lite traffic](https://docs.fortinet.com/document/fortigate/7.6.0/new-features/927041/support-udp-lite-traffic), both CLI References |
| D001–D004 | phase1-interface: `ike-version 1`, `dhgrp 14 5`, `peertype any`, 5.2 default proposals written explicitly (8.0 defaults: 2, 20, peer) | CLI Reference 5.2 pp. 787–793 (Default column), 8.0 CLI Reference |
| D005–D006 | phase2-interface: `dhgrp 14 5` and 5.2 default proposals written explicitly | CLI Reference 5.2 pp. 805 and 809 (phase2-interface, pp. 804–812), 8.0 CLI Reference |
| D007 | `system dns` `protocol cleartext` written explicitly (5.2 has no DNS protocol setting; 8.0 default is also cleartext) | CLI Reference 5.2 p. 495, 8.0 CLI Reference |
| M001 | `dpd enable` (5.2) has no documented equivalent among `disable/on-idle/on-demand`: manual | both CLI References |
| M002 | `system global` `timezone`: index in 5.2, time zone name in 8.0: manual | both CLI References |
| E001 | `user group` `sslvpn-portal`: replaced by the SSL VPN → IPsec conversion | Release Notes below |
| E002–E003 | `dlp-sensor`, `spamfilter-profile` are absent in 8.0 (which has `dlp-profile`, `emailfilter-profile`); custom security profiles are not rebuilt (design choice) and are listed for manual work | 8.0 CLI Reference |
| D008 | Deny policies: `match-vip disable` written explicitly (5.2 default disable, 8.0 enable) | CLI Reference 5.2 p. 157, 8.0 CLI Reference |
| D009 | LDAPS/STARTTLS servers: `server-identity-check disable` written explicitly (5.2 has no such setting; 8.0 checks the certificate name by default; to confirm in the lab); enable it once the certificate matches | CLI Reference 5.2 pp. 723–725 (user ldap), 8.0 CLI Reference |
| D010 | DHCP/PPPoE interfaces: `defaultgw disable` written explicitly (5.2 default disable, 8.0 enable with distance 5) | CLI Reference 5.2 p. 552, 8.0 CLI Reference |
| D011 | When the source takes the gateway from DHCP/PPPoE: `distance 1` (5.2 default 1, 8.0 default 5) | CLI Reference 5.2 p. 553, 8.0 CLI Reference |
| D012 | phase1-interface with NAT traversal: `keepalive 5` written explicitly (5.2 default 5, 8.0 default 10) | CLI Reference 5.2 p. 789, 8.0 CLI Reference |
| F001 | Tunnel `remote-ip` gets the host mask `255.255.255.255` (5.2 bare address, 8.0 `ipv4-classnet-host`) | CLI Reference 5.2 p. 561, 8.0 CLI Reference |
| R007 | `icmp-redirect` (5.2) absent in 8.0; `icmp-send-redirect` looks equivalent but no official page maps them: manual | CLI Reference 5.2 pp. 546/555, 8.0 CLI Reference |
| S001–S003 | Not copied: object UUIDs (assigned automatically, 8.0 CLI Reference), `snmp-index`, `macaddr` (device-specific: a design choice) | 8.0 CLI Reference |
| S004–S005 | Not copied: admin dashboards (`config dashboard`, `dashboard-tab`), absent from 8.0 `system admin` | CLI Reference 5.2 and 8.0 |
| R… / X… | Parameters and option values documented in 5.2 and absent from 8.0: manual | both CLI References |

## Target model (FortiGate 40F on 8.0)

| Fact | Source |
|---|---|
| FG-40F is supported by FortiOS 8.0.2 | [8.0.2 Release Notes: Introduction and supported models](https://docs.fortinet.com/document/fortigate/8.0.2/fortios-release-notes/760203/introduction-and-supported-models) |
| No SSL VPN web mode (Agentless VPN) on 40F, 50G, 60F, 61F, 70G, 90G; SSL VPN tunnel mode not supported on any model | [8.0.2 Release Notes: Agentless VPN not supported on some models](https://docs.fortinet.com/document/fortigate/8.0.2/fortios-release-notes/877104/agentless-vpn-formerly-ssl-vpn-web-mode-not-supported-on-some-fortigate-series-models), [7.6.3 Release Notes: SSL VPN tunnel mode replaced with IPsec VPN](https://docs.fortinet.com/document/fortigate/7.6.3/fortios-release-notes/173430/ssl-vpn-tunnel-mode-replaced-with-ipsec-vpn), [7.6.0 Release Notes: SSL VPN removed from 2GB RAM models](https://docs.fortinet.com/document/fortigate/7.6.0/fortios-release-notes/877104/ssl-vpn-removed-from-2gb-ram-models-for-tunnel-and-web-mode) |
| No most proxy-related features on 2 GB RAM models (email proxy back in 7.6.5) | [8.0.2 Release Notes: 2 GB RAM models and proxy features](https://docs.fortinet.com/document/fortigate/8.0.2/fortios-release-notes/768039/2-gb-ram-fortigate-models-no-longer-support-most-fortios-proxy-related-features) |
| No Security Rating and Security Fabric topology on 2 GB RAM models | [8.0.2 Release Notes](https://docs.fortinet.com/document/fortigate/8.0.2/fortios-release-notes/169631/2-gb-ram-fortigate-models-no-longer-support-security-rating-and-security-fabric-topology) |
| Ports wan, lan1–lan3, a | [8.0.2 Hardware Acceleration: FortiGate 40F fast path architecture](https://docs.fortinet.com/document/fortigate/8.0.2/hardware-acceleration/965349/fortigate-40f-fast-path-architecture) |
| Factory switch "lan" and factory DHCP server | **Not verified**: not documented in the sources used. Load the factory-default backup of the real 40F as the target. |

## Source model (FortiGate 110C)

| Fact | Source |
|---|---|
| Ports 1–8 (8-port internal switch), WAN1, WAN2; "Port 1 interface" 192.168.1.99 and WAN1 192.168.100.99 by default | [FortiGate-110C QuickStart Guide (2009)](https://fortinetweb.s3.amazonaws.com/docs.fortinet.com/v2/attachments/b987e131-19f6-11e9-9685-f8bc1258b856/fortigate-110C-quickstart.pdf) |

The interface names actually used are always read from the source backup.

## Switches and ports (section 6b)

| Fact used by Shiftwall | Source |
|---|---|
| 40F: WAN, A and 1–3 are all connected to the SOC4; `config system virtual-switch` can create a hardware switch with any front panel interface connected to the SOC4; a port added to a hardware switch must have mode static and must not be used by any other configuration | [8.0.2 Hardware Acceleration: FortiGate 40F fast path architecture](https://docs.fortinet.com/document/fortigate/8.0.2/hardware-acceleration/965349/fortigate-40f-fast-path-architecture) |
| Ports are removed with `config system virtual-switch` / `edit <switch>` / `config port` / `delete <port>` and added with `config port` / `edit <port>` / `next`, with `set physical-switch` on the switch; a port added to a hardware switch must not be referenced and must have IP 0.0.0.0/0.0.0.0; hardware switch traffic is processed by the switch hardware | [8.0.2 Administration Guide: Hardware switch](https://docs.fortinet.com/document/fortigate/8.0.2/administration-guide/100999/hardware-switch) |
| Software switch: `config system switch-interface` / `set vdom` / `set member` / `set type switch`, then `config system interface` / `set type switch` / `set ip`; members must not be referenced and must have IP 0.0.0.0/0.0.0.0; packets are processed in software by the CPU | [8.0.0 Administration Guide: Software switch](https://docs.fortinet.com/document/fortigate/8.0.0/administration-guide/277799/software-switch) |
| A hardware switch cannot be emptied: to free all its ports it must be deleted, after removing its references (policy, DHCP server, the address made by "Create address object matching subnet"). Shiftwall never deletes it and blocks the export instead (RB-013) | [Fortinet Community Technical Tip: How to delete the default virtual hardware switch](https://community.fortinet.com/fortigate-3/technical-tip-how-to-delete-the-default-virtual-hardware-switch-98088) (staff article, not product documentation) |
| Name of the physical switch (`set physical-switch`) | Read from the factory-default backup of the target. The Administration Guide example uses `sw0`, but it is not documented per model: without the backup Shiftwall writes a software switch (RB-066) |
| Interface names: at most 15 characters | 8.0 CLI Reference, `config system interface`, `config system virtual-switch`, `config system switch-interface` (data/schemas/8.0.json) |
| VLAN on a parent interface: `set interface`, `set vlanid` (1–4094) | 8.0 CLI Reference, `config system interface` (data/schemas/8.0.json) |
| 5.2: `internal-switch-mode` default `switch` (the internal ports form one switch with one address) | FortiOS 5.2 CLI Reference PDF, p. 524 |

Models with a hardware-switch chip table in Shiftwall: only FG-40F (the only one with a source above). For any other model a
new switch is a software switch, with a warning (RB-061).

## SSL VPN → IPsec dial-up

| What | Source |
|---|---|
| Migration approach (groups, address range, split tunnel, policies, FortiClient) | [SSL VPN to IPsec VPN migration (7.6)](https://docs.fortinet.com/document/fortigate/7.6.0/ssl-vpn-to-ipsec-vpn-migration/126460/introduction), Part 2 and Local user authentication |
| CLI of the dial-up tunnel (IKEv2, EAP, mode-cfg, proposals `aes128-sha256 aes256-sha256 aes128gcm-prfsha256 aes256gcm-prfsha384 chacha20poly1305-prfsha256`, DH 20 21, split include, `assign-ip-from name`) | [8.0.2 Administration Guide: FortiClient as dialup client](https://docs.fortinet.com/document/fortigate/8.0.2/administration-guide/785501/forticlient-as-dialup-client) (the page writes `es128-sha256`, an evident typo for `aes128-sha256`, which is the value in the 8.0 CLI Reference) |
| `dns-mode manual` with `ipv4-dns-server1/2` when the SSL VPN had DNS servers (the guide example uses `dns-mode auto`) | 8.0 CLI Reference, `vpn ipsec phase1-interface` |
| FortiClient 7.4.4 and later do not support IKEv1 | SSL VPN to IPsec VPN migration, Part 2 |

## Script format and loading

| What | Source |
|---|---|
| Special characters (`< > ( ) # ' "`, space, `\`; `?` and Tab cannot be pasted) | [8.0.2 Administration Guide: CLI basics](https://docs.fortinet.com/document/fortigate/8.0.2/administration-guide/896276/cli-basics) |
| Script files: comment lines start with `#`; Configuration > Scripts > Run Script | [8.0.2 Administration Guide: Configuration scripts](https://docs.fortinet.com/document/fortigate/8.0.2/administration-guide/780930/configuration-scripts) |
| CLI Console (the "_>" icon), SSH with PuTTY | [8.0.2 Administration Guide: Connecting to the CLI](https://docs.fortinet.com/document/fortigate/8.0.2/administration-guide/901037/connecting-to-the-cli) |
| Return codes | [8.0.2 Administration Guide: CLI error codes](https://docs.fortinet.com/document/fortigate/8.0.2/administration-guide/257686/cli-error-codes) |
| `execute batch start/end/status/lastlog` exists (behaviour not described, not used) | [8.0 CLI Reference: execute batch](https://docs.fortinet.com/document/fortigate/8.0.0/cli-reference/219031151/execute-batch) |
| Line endings, encoding, maximum script size | **Not documented**: Shiftwall uses UTF-8 without BOM, LF, parts of at most 800 lines; to confirm in the lab |

## Passwords

| What | Source |
|---|---|
| Password policy enforced from 7.6.5: 12+ characters, upper case, lower case, number, special character | [8.0.2 Release Notes: Password policy enforcement](https://docs.fortinet.com/document/fortigate/8.0.2/fortios-release-notes/799879/password-policy-enforcement) |
| Changing the password of the administrator in use asks for the current password | [8.0.2 Administration Guide: Default administrator password](https://docs.fortinet.com/document/fortigate/8.0.2/administration-guide/99980/default-administrator-password) |

Generated passwords: 20 characters from `A–Z` (no I, O), `a–z` (no l), `2–9` and `! $ % & * + - = @ ^ _ ~`, with
at least one of each class, new for every migration. They never contain a character listed in *CLI basics*.
Which characters count as "special" for the password policy is not documented: the symbol set is to be confirmed
in the lab.
