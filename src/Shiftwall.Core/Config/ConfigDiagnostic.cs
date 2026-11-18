namespace Shiftwall.Core.Config;

public enum DiagnosticSeverity
{
    Info,
    Warning,
    Error,
}

/// <summary>
/// A finding about a configuration. <see cref="Code"/> is stable and maps to a localized message in the UI;
/// <see cref="Arguments"/> never contain secrets (only line numbers, counts, section paths or VDOM names).
/// </summary>
public sealed record ConfigDiagnostic(string Code, DiagnosticSeverity Severity, int? Line, IReadOnlyList<string> Arguments)
{
    public static ConfigDiagnostic Info(string code, int? line = null, params string[] args) => new(code, DiagnosticSeverity.Info, line, args);

    public static ConfigDiagnostic Warning(string code, int? line = null, params string[] args) => new(code, DiagnosticSeverity.Warning, line, args);

    public static ConfigDiagnostic Error(string code, int? line = null, params string[] args) => new(code, DiagnosticSeverity.Error, line, args);
}

/// <summary>Stable diagnostic codes.</summary>
public static class DiagnosticCodes
{
    // Structure
    public const string UnterminatedQuote = "SW-P001";
    public const string UnclosedBlock = "SW-P002";
    public const string UnexpectedEnd = "SW-P003";
    public const string UnexpectedNext = "SW-P004";
    public const string EditOutsideConfig = "SW-P005";
    public const string UnknownStatement = "SW-P006";
    public const string FieldOutsideBlock = "SW-P007";

    // Content detection
    public const string NotText = "SW-D001";
    public const string YamlFormat = "SW-D002";
    public const string MissingHeader = "SW-D003";
    public const string PartialConfig = "SW-D004";
    public const string PrivateDataEncryption = "SW-D005";
    public const string MultiVdom = "SW-D006";
    public const string PasswordMasked = "SW-D007";
    public const string EncryptedValues = "SW-D008";
    public const string NonUtf8 = "SW-D009";
    public const string EmptyFile = "SW-D010";
}
