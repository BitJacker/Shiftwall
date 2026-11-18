For every model in the database the **factory-default hardware/virtual switch membership** (for example which ports belong to `internal` or `lan`) is **unverified**: it is not documented in the public sources used. Today users must load a factory-default backup of the target to get it.

You can help if you have access to a FortiGate in its factory-default state (a lab unit, or a new unit before configuration).

**Task**
1. Back up the factory-default configuration of the unit.
2. On your PC run:
   `dotnet run --project tools/update-db -- import-default-backup --model <id> --file <path-to-backup>`
   It reads **only interface names and switch membership** from the file and shows the change; add `--apply` to write it to `data/models.json`.
3. **Never commit or attach the backup.** Only the interface names go into the database.
4. Run `-- normalize`, `-- validate`, `-- gen-docs` and open a pull request stating the model and FortiOS version of the unit.

One model per pull request is perfect.
