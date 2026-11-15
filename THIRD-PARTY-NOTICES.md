# Third-party notices

Shiftwall uses the third-party components listed below. Licenses were checked on the date shown against the
package metadata on nuget.org or the project's own LICENSE file. A component is added here when it is
actually referenced by the build, with its exact version.

## Shipped in Shiftwall.exe

| Component | Version | License | Source | Checked |
|---|---|---|---|---|
| .NET runtime and libraries | 10.0.x (SDK 10.0.404) | MIT | <https://github.com/dotnet/runtime/blob/main/LICENSE.TXT> | 2026-11-15 |
| Windows Presentation Foundation (WPF) | 10.0.x | MIT | <https://github.com/dotnet/wpf/blob/main/LICENSE.TXT> | 2026-11-15 |
| DiffPlex | 1.9.0 | Apache-2.0 | <https://www.nuget.org/packages/DiffPlex/1.9.0> (no dependencies) | 2026-11-15 |

## Planned (not referenced yet)

Verified in Phase 0, added to the table above when the code that uses them lands:

| Component | Version | License | Notes |
|---|---|---|---|
| AvalonEdit | 6.3.1.120 | MIT | Text/diff viewer |
| ClosedXML | 0.105.1 | MIT | Excel export. Transitive `SixLabors.Fonts` must stay at **1.0.0 (Apache-2.0)**: versions 2.x use the Six Labors Split License, not OSI-approved. Pinned centrally. |
| DocumentFormat.OpenXml | 3.x | MIT | ClosedXML dependency |
| ClosedXML.Parser, ExcelNumberFormat, RBush.Signed | 2.0.0, 1.1.0, 4.0.0 | MIT | ClosedXML dependencies |

**Rejected:** QuestPDF (not an OSI license) and PDFsharp ≥ 6.5.0 (AGPL-3.0 or commercial since 2026-10-13).
PDF reports are produced by printing to the Windows "Microsoft Print to PDF" printer.

## Development and test only (not shipped)

| Component | Version | License | Source | Checked |
|---|---|---|---|---|
| xunit.v3 | 4.0.3 | Apache-2.0 | nuget.org | 2026-11-15 |
| Playwright (brand asset rendering script only) | preinstalled | Apache-2.0 | <https://github.com/microsoft/playwright> | 2026-11-15 |

## Artwork

| Component | Use | License |
|---|---|---|
| Inter typeface (The Inter Project Authors) | Text in `assets/brand/banner-1280x640.png` | SIL Open Font License 1.1, <https://github.com/rsms/inter/blob/master/LICENSE.txt> |

The Shiftwall logo and icon are original work of the Shiftwall project.
