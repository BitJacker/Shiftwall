Five models in `data/models.json` have `has_disk` marked **unverified**, because no official data sheet covering the exact model was found:

| Model | Note in the database |
|---|---|
| FG-70F | The official 70F/71F data sheet found covers only the 71F. |
| FG-100E, FG-101E, FG-200E, FG-201E | No official data sheet was found online. |

Without this datum Shiftwall keeps the disk sections (`config system storage`, `config log disk setting`) and asks the user to check them by hand (MIG-006).

**Task**
1. Find an official Fortinet document (data sheet on `www.fortinet.com`, or a page on `docs.fortinet.com`) that states the onboard storage of each model. Forums and resellers are not valid sources.
2. Update `has_disk` for each model you can verify: `value`, `"status": "verified"`, and a source with URL, title, `verified_on` date and a short note (for example "Onboard Storage: 0"). See [CONTRIBUTING.md](https://github.com/BitJacker/shiftwall/blob/main/CONTRIBUTING.md#adding-or-updating-a-fortigate-model).
3. Run `dotnet run --project tools/update-db -- normalize`, `-- validate` and `-- gen-docs`, then open a pull request.

If no official source exists for a model, comment here with what you checked: leaving it unverified is the correct outcome.
