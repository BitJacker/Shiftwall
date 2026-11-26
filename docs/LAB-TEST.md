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
