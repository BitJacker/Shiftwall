# Third-party notices

Shiftwall is licensed under the [GNU General Public License v3.0](LICENSE). The components below keep their own
licenses. Apache-2.0 (DiffPlex) is compatible with GPLv3, according to the
[FSF license list](https://www.gnu.org/licenses/license-list.html#apache2) and the
[Apache Software Foundation](https://www.apache.org/licenses/GPL-compatibility.html).

Shiftwall uses the third-party components listed below. Licenses were checked on the date shown against the
package metadata on nuget.org or the project's own LICENSE file. A component is added here when it is
actually referenced by the build, with its exact version.

## Shipped in Shiftwall.exe

| Component | Version | License | Source | Checked |
|---|---|---|---|---|
| .NET runtime and libraries | 10.0.x (SDK 10.0.404) | MIT | <https://github.com/dotnet/runtime/blob/main/LICENSE.TXT> | 2026-11-15 |
| Windows Presentation Foundation (WPF) | 10.0.x | MIT | <https://github.com/dotnet/wpf/blob/main/LICENSE.TXT> | 2026-11-15 |
| DiffPlex | 1.9.0 | Apache-2.0 | <https://www.nuget.org/packages/DiffPlex/1.9.0> (no dependencies) | 2026-11-15 |

## Evaluated and not used

AvalonEdit and ClosedXML (both MIT) were evaluated in Phase 0. Shiftwall does not use them: the diff view is a WPF
list and Excel files are written by a small built-in writer (`Shiftwall.Features/Toolbox/Export/TableExport.cs`).

**Rejected:** QuestPDF (not an OSI license) and PDFsharp ≥ 6.5.0 (AGPL-3.0 or commercial since 2026-10-13).
PDF reports are produced by printing to the Windows "Microsoft Print to PDF" printer.

## Development and test only (not shipped)

| Component | Version | License | Source | Checked |
|---|---|---|---|---|
| xunit.v3 | 4.0.3 | Apache-2.0 | nuget.org | 2026-11-15 |
| gitleaks (secret scan in CI and pre-commit) | 8.30.1 | MIT | <https://github.com/gitleaks/gitleaks/blob/master/LICENSE> | 2026-11-15 |
| Playwright (brand asset rendering script only) | preinstalled | Apache-2.0 | <https://github.com/microsoft/playwright> | 2026-11-15 |

## Artwork

| Component | Use | License |
|---|---|---|
| Inter typeface (The Inter Project Authors) | Text in `assets/brand/banner-1280x640.png` | SIL Open Font License 1.1, <https://github.com/rsms/inter/blob/master/LICENSE.txt> |

The Shiftwall logo and icon are original work of the Shiftwall project.
