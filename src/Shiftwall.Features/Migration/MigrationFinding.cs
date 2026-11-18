using System.Globalization;
using System.Resources;

namespace Shiftwall.Features.Migration;

public enum FindingSeverity
{
    Info,
    Warning,

    /// <summary>Blocks the migration (or marks the produced output as invalid).</summary>
    Error,
}

/// <summary>A pre-check, change, validation result or checklist item. Arguments never contain secrets.</summary>
public sealed record MigrationFinding(string Code, FindingSeverity Severity, IReadOnlyList<string> Arguments, int? Line = null)
{
    public static MigrationFinding Info(string code, params string[] args) => new(code, FindingSeverity.Info, args);

    public static MigrationFinding Warning(string code, params string[] args) => new(code, FindingSeverity.Warning, args);

    public static MigrationFinding Error(string code, params string[] args) => new(code, FindingSeverity.Error, args);

    public string Format(CultureInfo? culture = null) => MigrationText.Format(Code, Arguments, Line, culture);
}

/// <summary>Stable codes; the text is in Resources/MigrationMessages(.it).resx.</summary>
public static class MigrationCodes
{
    // Pre-checks
    public const string FirmwareMismatch = "MIG-001";
    public const string FirmwareUnknown = "MIG-002";
    public const string HeaderNotAvailable = "MIG-003";
    public const string TargetInterfacesUnverified = "MIG-004";
    public const string TargetInterfacesNote = "MIG-005";
    public const string DiskUnknown = "MIG-006";
    public const string DiskSectionsWillBeRemoved = "MIG-007";
    public const string PrivateDataEncryption = "MIG-008";
    public const string MultiVdom = "MIG-009";
    public const string SourceHasErrors = "MIG-010";
    public const string VirtualSwitchReview = "MIG-011";
    public const string SourcePartial = "MIG-012";
    public const string MaskedPasswords = "MIG-013";
    public const string SourceNotFortiOsText = "MIG-014";
    public const string TargetIsSource = "MIG-015";

    // Mapping
    public const string UnmappedUsed = "MIG-020";
    public const string UnmappedUnused = "MIG-021";
    public const string DuplicateTarget = "MIG-022";
    public const string MappedToUnknownTarget = "MIG-023";
    public const string TargetOnlyPorts = "MIG-024";
    public const string SwitchModePortToSinglePort = "MIG-025";

    // Changes
    public const string HeaderReplaced = "MIG-030";
    public const string Renamed = "MIG-031";
    public const string DefinitionRemoved = "MIG-032";
    public const string SectionRemoved = "MIG-033";
    public const string PossibleReference = "MIG-034";
    public const string HeaderNotReplaced = "MIG-035";

    // Validation of the output
    public const string OutputStructure = "MIG-040";
    public const string LeftoverReference = "MIG-041";
    public const string PhysicalNotOnTarget = "MIG-042";
    public const string EncryptedValueChanged = "MIG-043";
    public const string SwitchMemberNotOnTarget = "MIG-044";
    public const string OutputValid = "MIG-045";

    // Checklist
    public const string CheckLab = "CHK-001";
    public const string CheckFirmware = "CHK-002";
    public const string CheckSecrets = "CHK-003";
    public const string CheckHeader = "CHK-004";
    public const string CheckLogging = "CHK-005";
    public const string CheckPossibleReferences = "CHK-006";
    public const string CheckTargetOnlyPorts = "CHK-007";
    public const string CheckSwitch = "CHK-008";
    public const string CheckHaMgmt = "CHK-009";
    public const string CheckLicensing = "CHK-010";
    public const string CheckManagementAccess = "CHK-011";
}

/// <summary>Localized texts for migration findings (English and Italian).</summary>
public static class MigrationText
{
    private static readonly ResourceManager Resources = new("Shiftwall.Features.Resources.MigrationMessages", typeof(MigrationText).Assembly);

    public static string Format(string code, IReadOnlyList<string> args, int? line = null, CultureInfo? culture = null)
    {
        culture ??= CultureInfo.CurrentUICulture;
        var template = Resources.GetString(code, culture) ?? code;
        var text = template.Replace("{line}", line?.ToString(culture) ?? "?", StringComparison.Ordinal);
        return args.Count == 0 ? text : string.Format(culture, text, [.. args.Cast<object>()]);
    }

    public static string Get(string key, CultureInfo? culture = null) =>
        Resources.GetString(key, culture ?? CultureInfo.CurrentUICulture) ?? key;
}
