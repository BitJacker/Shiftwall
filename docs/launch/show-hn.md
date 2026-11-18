# Show HN draft

Draft only: publish it when the app is stable and has been tested on real hardware.
Rules: <https://news.ycombinator.com/showhn.html> (the project must be something people can try; title starts with
"Show HN"; do not ask anyone to upvote or comment).

## Title

Show HN: Shiftwall – offline FortiGate config migration between models (Windows)

## Link

https://github.com/BitJacker/shiftwall

## Text

I'm a sysadmin. When a FortiGate is replaced with a different model, the backup can't simply be restored: interface
names change (internal1 vs port1, wan vs wan1), and they are referenced in hundreds of places. Fortinet's manual
procedure is to rename them by hand, fix the config-version line of the header, restore and check. Fortinet's own
converter is a separate product, and I wanted something I could run offline on any Windows PC.

Shiftwall is a portable Windows exe (WPF, .NET 10, GPL-3.0) with a five-step wizard: load the old backup and a
factory-default backup of the new unit, see the pre-checks, map the ports, review a diff and a validation report,
then save to a new file. Encrypted values (ENC passwords, keys) are copied byte for byte and never decrypted. It
makes no network connections; a test fails the build if code outside an opt-in LAN module uses a network client.
Model data (interface lists, disk, supported FortiOS versions) comes only from Fortinet documents, each with its URL
and check date; anything without a source is flagged as unverified instead of guessed.

There is also an offline toolbox: config compare, a security audit whose rules cite Fortinet's Best Practices or RFCs,
unused objects, Excel/CSV export, CSV to CLI, IPsec summary, and an anonymizer to share configs safely.

Limits, honestly:
- Both units must run the same FortiOS version; it does not translate syntax between versions.
- So far it has been tested on synthetic configurations and in [N lab migrations: fill in before posting], not
  across many real-world migrations.
- The exe is not code-signed, so SmartScreen warns on first run (SHA256 sums are published with each release).
- Not affiliated with Fortinet, and modified configs are not supported by Fortinet TAC: test in a lab first.

You can try it without a FortiGate using the synthetic samples in the repo. I'd especially like to hear about port
layouts or config sections it handles wrongly; please share anonymized excerpts only.
