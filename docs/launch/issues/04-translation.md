The interface is available in English and Italian. A third language would help more sysadmins.

**What it involves**
- Add `*.<culture>.resx` files next to the existing ones:
  - `src/Shiftwall.Presentation/Localization/Strings.resx`
  - `src/Shiftwall.Features/Resources/MigrationMessages.resx`
  - `src/Shiftwall.Features/Resources/ToolboxMessages.resx`
- Add the culture to `Loc.SupportedCultures`.
- The sidebar has an English/Italian toggle button today (`MainViewModel.ToggleLanguageCommand`): with three languages it needs a small list instead.
- The localization tests check that every key exists in every language: extend them to the new culture.

Please comment with the language you want to work on, so two people don't translate the same one. Native speakers only, please: no machine translation without a review.
