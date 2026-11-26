# Lab test of a rebuilt configuration

Before using a rebuilt configuration in production, run it on a lab FortiGate of the same model, or on a FortiGate
VM, with the firmware of the target version. Every error you find this way becomes a test in Shiftwall.

## What you need

- The same model as the target (or a FortiGate VM with the same FortiOS version, for everything that does not
  depend on the hardware: objects, policies, VPN settings).
- The exact firmware of the target (System > Firmware). Shiftwall validates the script on the CLI Reference of the
  major version (e.g. 8.0): note the full build.
- The files saved by Shiftwall: the GUI parts or the terminal blocks, the HTML report, and the encrypted password
  file if passwords were generated.

## Procedure

1. **Factory configuration.** Reset the lab FortiGate to its factory configuration and log in.
2. **Backup of the factory configuration.** Save it (Configuration > Backup): it is your way back, and it is the
   best input for Shiftwall (load it as the target in step 1 of the wizard, so the interface list and the factory
   DHCP servers come from the real device).
3. **Run the script** as described in [load-scripts.md](load-scripts.md): GUI parts in order, or terminal blocks one
   at a time. The management part goes last; reconnect to the new address before the very last part.
4. **Check after each part:** no error in the Script Execution History or in the console.
5. **Verify the result:**
   - interfaces, addresses, zones, VLANs (Network > Interfaces);
   - routes (Network > Static Routes) and internet access from the LAN;
   - policies: count them against the report ("policy: N → N") and try one per direction;
   - IPsec tunnels: with the new keys on the other side; dial-up with FortiClient (IKEv2, EAP);
   - administrators and users: log in with the new passwords, then change them;
   - everything in the "To do by hand" list of the report.
6. **Backup** of the resulting configuration.

## When a command fails

Write down:

| What | Example |
|---|---|
| Shiftwall version | 0.1.0 (Info page) |
| Source model and FortiOS version | FG-110C 5.2.x build |
| Target model and FortiOS version | FG-40F 8.0.x build |
| File and line | `gui-004-addresses.txt`, line 37 |
| The command | `set associated-interface "lan"` |
| The full error text | `Command fail. Return code -651` and the line before it |

Then open an issue on the Shiftwall repository with these data. **Do not attach real configurations**: if the
error depends on your data, anonymize the backup with the Anonymize page of Shiftwall first and attach only the
lines needed to reproduce it. The fix comes with a test that reproduces the error, so it cannot come back.

## Known limits to check in the lab

- Line endings and maximum script size accepted by Run Script are not documented: Shiftwall uses LF and parts of
  at most 800 lines.
- The time zone of FortiOS 5.2 is a number; FortiOS 8.0 expects a time zone name: set it by hand.
- Security profiles (antivirus, web filter, application control, IPS...) are not rebuilt: policies are created
  without the custom profiles, which are listed in the report.
- SSL VPN is converted to IPsec dial-up following Fortinet's "FortiClient as dialup client" example: it is marked
  "to review" and must be tested with FortiClient before production.

## Trying the scripts on a FortiGate-VM (legal, documented, limited)

Fortinet documents a **permanent evaluation licence** for FortiGate-VM: it is free, does not expire and needs only a
FortiCloud account ([8.0.2 Administration Guide: Permanent trial mode for FortiGate-VM](https://docs.fortinet.com/document/fortigate/8.0.2/administration-guide/441460/permanent-trial-mode-for-fortigate-vm)).
Its documented limits decide what such a test can prove:

| Limit of the evaluation licence (same page) | Consequence for a Shiftwall script |
|---|---|
| One free evaluation copy per FortiCloud account | One lab VM per account |
| 1 CPU and 2 GB of memory | Enough to load a configuration |
| **At most three interfaces, three firewall policies and three routes** | A full script does not fit: from the fourth policy or route FortiOS refuses the command. Load the parts one at a time and expect those refusals |
| No FortiCare and no FortiGuard support | Profiles load, signatures are not updated |

What a VM can prove: the syntax of every command that is not about the hardware (addresses, services, schedules, users,
IPsec, DNS, the first three policies and routes) and the Return codes of the parts. What it cannot prove: everything that
depends on the 40F hardware. A FortiGate-VM has ports named `port1`, `port2`… and no hardware switch, so the `switch` part
(`config system virtual-switch`) and the interface names `wan`, `lan`, `lan1`–`lan3` do not exist there. That part, the
port moves and the management change must be tested on the real 40F.

Procedure (KVM example; VMware, Hyper-V and Proxmox have equivalent guides on docs.fortinet.com):

1. Download the new-deployment package `FGT_VM64_KVM-8.0.x-buildXXXX-FORTINET.out.kvm.zip` (not the `.out` upgrade image)
   from the Fortinet support site, VM images section, with the same firmware build as the real 40F
   ([8.0.0 KVM Administration Guide: Downloading the FortiGate-VM deployment package](https://docs.fortinet.com/document/fortigate-private-cloud/8.0.0/kvm-administration-guide/961760/downloading-the-fortigate-vm-deployment-package)).
2. Create the VM with 1 vCPU, 2 GB RAM and three network interfaces, as the KVM guide describes; add the log disk.
3. At the console, apply the evaluation licence: `execute vm-license-options account-id <your FortiCloud email>`,
   `execute vm-license-options account-password <password>`, `execute vm-license` (the VM restarts). Check with
   `get system status`: `License Status: Valid`.
4. Take a backup of the VM factory configuration and load it in Shiftwall as the target, so the interface names are the
   VM ones (`port1`…); map the source interfaces to them. The switch variants do not apply on a VM.
5. Run the parts in order (System > Configuration > Scripts, or paste the terminal blocks) and note every Return code
   as described above. Refusals caused by the three-policy/three-route limit are expected and are not Shiftwall errors.
6. Delete the VM after the test: it held a copy of the (anonymised) configuration.

Never use a real customer backup on a lab VM that is reachable from other networks; use the anonymised backup.
