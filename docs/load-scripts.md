# Loading a rebuilt configuration on the new FortiGate

The rebuild mode of Shiftwall writes the source configuration again as a clean CLI script in the syntax of the
target FortiOS version. It is meant for a FortiGate **with its factory configuration**, on the firmware of the
target version. Always try it in a lab first (see [LAB-TEST.md](LAB-TEST.md)).

> Shiftwall is not affiliated with Fortinet. FortiGate and FortiOS are trademarks of Fortinet, Inc. Modified
> configurations are not supported by Fortinet TAC: always test in a lab before production.

Screenshots of the FortiGate GUI are not included: they will be added after the lab test, taken on our own lab
device. The steps below follow the official FortiOS 8.0.2 documentation, linked in each section.

## What Shiftwall saves

| Save button | Folder | Content |
|---|---|---|
| Save for GUI | `<source>-to-<target>-gui` | `gui-001-<section>.txt`, `gui-002-…`: numbered parts. Each starts with two `#` comment lines (allowed in scripts). |
| Save for terminal | `<source>-to-<target>-terminal` | `cli-001-<section>.txt`, `cli-002-…`: blocks of about 40 lines, no comments, to paste one at a time. |
| Export HTML report | `…-report.html` | Counts source → target, manual work with the reason, rules applied with sources, checklist. No passwords. |
| Save encrypted | `…-passwords.swsecret` | The new passwords, encrypted with the file password you choose. |

Both script versions contain the same commands in the same order. Every file is UTF-8 without BOM with LF line
endings, uses only `config`/`edit`/`set`/`unset`/`delete`/`next`/`end`, and passed the final check of Shiftwall:
balanced blocks, no `?` or tab (they cannot be pasted, see *CLI basics*), every `set` valid in the CLI Reference of
the target version, and every name used by the script defined in the script or present out of the box.

The order of the parts follows the dependencies: switch ports, interfaces (VLANs after their parent), zones,
general settings and DNS, addresses, services, schedules, virtual IPs and IP pools, users and groups, IPsec,
routes, policies, administrators, DHCP servers, and **last** the interface you are connected to and its DHCP
server.

Not documented by Fortinet, to confirm in the lab: whether CRLF line endings are also accepted, and the maximum
size of a script file. Shiftwall keeps parts small (at most 800 lines) and uses LF.

## Option 1: GUI, Run Script

Source: FortiOS 8.0.2 Administration Guide, [Configuration scripts](https://docs.fortinet.com/document/fortigate/8.0.2/administration-guide/780930/configuration-scripts).

1. Log in to the new FortiGate.
2. Click your user name (top right) and select **Configuration > Scripts**.
3. Click **Run Script**, select `gui-001-….txt` and click **OK**. The script runs immediately and the
   **Script Execution History** table shows whether it ran successfully.
4. Repeat with `gui-002`, `gui-003`… **in order**. Stop at the first part that fails and read the error (below).
5. The part called `management` changes the interface you are connected to: after it the connection drops.
   Reconnect to the new address and run the last part (`management-dhcp`).

## Option 2: terminal (CLI Console or SSH)

Source: FortiOS 8.0.2 Administration Guide, [Connecting to the CLI](https://docs.fortinet.com/document/fortigate/8.0.2/administration-guide/901037/connecting-to-the-cli).

- **CLI Console in the GUI**: click the `_>` icon at the top right of the GUI (as the Administration Guide calls it).
- **SSH (e.g. PuTTY)**: SSH must be allowed on the interface you connect to; host = its IP address, port 22.

Open `cli-001-….txt` in a text editor, copy the whole block, paste it in the console and press Enter. Wait for the
prompt, check that no error was printed, then go on with `cli-002`. When you reach the `management` block, the
session drops after the address change: reconnect to the new address and paste the last block.

Shiftwall does not use `execute batch start` / `execute batch end`: the command exists in the
[FortiOS 8.0 CLI Reference](https://docs.fortinet.com/document/fortigate/8.0.0/cli-reference/219031151/execute-batch),
but the reference does not describe how it behaves, and the scripts never contain the characters that would need it.

## Reading errors

Source: FortiOS 8.0.2 Administration Guide, [CLI error codes](https://docs.fortinet.com/document/fortigate/8.0.2/administration-guide/257686/cli-error-codes).

When a command fails the CLI prints a summary followed by `Command fail. Return code -X`. Common codes:

| Return code | Meaning (Fortinet) | What it usually means for a Shiftwall script |
|---|---|---|
| 1 | Generic CLI syntax error | A command of the script is not valid on this firmware: check the firmware version. |
| -1 | Invalid length of value | A name or value is too long for this version. |
| -4 | Maximum number of entries has been reached | The new model has a lower limit for this table. |
| -5 | A duplicate entry already exists | The object already exists (e.g. the script was run twice with a different name). |
| -8 | Invalid IP Address | An address does not fit (e.g. a gateway outside the subnet). |
| -37 | Permission denied | The administrator running the script does not have enough rights. |
| -56 | Empty values are not allowed | A required value is missing. |
| -61 | Input not as expected | A keyword is not valid here (parameter removed or renamed in this version). |
| -160 | Generic configuration error | Look at the summary line printed before the code. |
| -553 | Name conflicts with an interface, VDOM, switch-interface, zone, or interface name used for hardware switch interfaces | The name of a new interface or zone is already used on the new model. |
| -651 | Input value is invalid | The value is not accepted (option, range, or a name that does not exist). |

Write down the file, the line, the full error text and the firmware build: see [LAB-TEST.md](LAB-TEST.md). Every
real error becomes a new test of Shiftwall.

## Running a part twice

The scripts use `edit` and `set`, which create or overwrite: running a part twice gives the same result. The
exceptions are the `delete` lines (ports taken out of a switch, factory DHCP servers replaced): the second time the
entry is already gone and the CLI prints an error for that line only.
