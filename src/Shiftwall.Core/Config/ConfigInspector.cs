using System.Text.RegularExpressions;

namespace Shiftwall.Core.Config;

public enum ConfigFileKind
{
    /// <summary>FortiOS CLI-format text backup: parsed.</summary>
    FortiOsText,

    /// <summary>Binary content, typically a password-encrypted backup: not parsed, never decrypted.</summary>
    BinaryOrEncrypted,

    /// <summary>YAML-format backup: not supported, the user is asked for a FortiOS-format backup.</summary>
    Yaml,

    Empty,
}

/// <summary>What Shiftwall learned about a configuration file. Contains no secret values.</summary>
public sealed record ConfigInspection(
    ConfigFileKind Kind,
    ConfigText Text,
    ConfigDocument? Document,
    IReadOnlyList<ConfigDiagnostic> Diagnostics,
    bool IsMultiVdom,
    IReadOnlyList<string> Vdoms,
    bool PrivateDataEncryption,
    int EncryptedValueCount,
    int MaskedPasswordCount,
    bool IsPartial)
{
    public ConfigHeader? Header => Document?.Header;

    public bool HasErrors => Diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error);
}

/// <summary>
/// Loads a configuration file and detects the conditions the user must know about before using it.
/// </summary>
/// <remarks>
/// Official references (FortiOS 7.6.8 Administration Guide / Release Notes, docs.fortinet.com):
/// backups can be encrypted with a password (AES-GCM; container format not documented), exported in YAML, or
/// exported with secrets replaced by <c>FortinetPasswordMask</c>, whose restore "is not recommended";
/// <c>private-data-encryption</c> encrypts secrets with a device key that must match on the target unit.
/// See <see cref="Guidance"/> for the URLs.
/// </remarks>
public static partial class ConfigInspector
{
    public const string PasswordMaskToken = "FortinetPasswordMask";

    [GeneratedRegex(@"^\s*(-\s+)?[A-Za-z0-9_.-]+:(\s|$)", RegexOptions.Multiline)]
    private static partial Regex YamlLine();

    public static ConfigInspection Inspect(ReadOnlySpan<byte> bytes) => Inspect(ConfigText.Decode(bytes));

    public static ConfigInspection Inspect(ConfigText text)
    {
        var diagnostics = new List<ConfigDiagnostic>();

        if (string.IsNullOrWhiteSpace(text.Text) && !text.LooksBinary)
        {
            diagnostics.Add(ConfigDiagnostic.Error(DiagnosticCodes.EmptyFile));
            return Result(ConfigFileKind.Empty, null);
        }

        if (text.LooksBinary)
        {
            diagnostics.Add(ConfigDiagnostic.Error(DiagnosticCodes.NotText));
            return Result(ConfigFileKind.BinaryOrEncrypted, null);
        }

        var doc = ConfigParser.Parse(text.Text);
        var hasConfigStatements = doc.Descendants().Any(n => n is ConfigBlock);
        if (!hasConfigStatements && !doc.Header.HasConfigVersion && YamlLine().Matches(text.Text).Count >= 2)
        {
            diagnostics.Add(ConfigDiagnostic.Error(DiagnosticCodes.YamlFormat));
            return Result(ConfigFileKind.Yaml, null);
        }

        if (text.Encoding == ConfigTextEncoding.Latin1Fallback)
        {
            diagnostics.Add(ConfigDiagnostic.Warning(DiagnosticCodes.NonUtf8));
        }

        diagnostics.AddRange(doc.Diagnostics);

        // Header and completeness.
        var partialReasons = new List<string>();
        if (!doc.Header.HasConfigVersion)
        {
            diagnostics.Add(ConfigDiagnostic.Warning(DiagnosticCodes.MissingHeader, 1));
            partialReasons.Add("no-header");
        }

        if (doc.GlobalScope.Block("system global") is null)
        {
            partialReasons.Add("no-system-global");
        }

        if (doc.Diagnostics.Any(d => d.Code == DiagnosticCodes.UnclosedBlock))
        {
            partialReasons.Add("unclosed-blocks");
        }

        if (partialReasons.Count > 0)
        {
            diagnostics.Add(ConfigDiagnostic.Warning(DiagnosticCodes.PartialConfig, null, [.. partialReasons]));
        }

        // Multi-VDOM.
        var multiVdom = doc.IsMultiVdom || doc.Header.VdomFlag == "1";
        var vdoms = doc.IsMultiVdom
            ? doc.VdomScopes().Select(v => v.Vdom!).Distinct(StringComparer.Ordinal).ToList()
            : [];
        if (multiVdom)
        {
            diagnostics.Add(ConfigDiagnostic.Info(DiagnosticCodes.MultiVdom, null, [.. vdoms]));
        }

        // private-data-encryption (config system global, global scope).
        var pdeField = doc.GlobalScope.Block("system global")?.Field("private-data-encryption");
        var pde = pdeField is { Verb: "set" } && pdeField.Values.FirstOrDefault() == "enable";
        if (pde)
        {
            diagnostics.Add(ConfigDiagnostic.Warning(DiagnosticCodes.PrivateDataEncryption, pdeField!.Statement.Line));
        }

        // Secrets: count only, never read or decode them.
        var enc = 0;
        var masked = 0;
        int? firstMaskedLine = null;
        foreach (var field in doc.Descendants().OfType<FieldNode>())
        {
            var tokens = field.Statement.Tokens;
            for (var i = 2; i < tokens.Count; i++)
            {
                var t = tokens[i];
                if (!t.IsQuoted && t.Value == "ENC" && i + 1 < tokens.Count)
                {
                    enc++;
                    i++;
                }
                else if (t.Value == PasswordMaskToken)
                {
                    masked++;
                    firstMaskedLine ??= field.Statement.Line;
                }
            }
        }

        if (enc > 0)
        {
            diagnostics.Add(ConfigDiagnostic.Info(DiagnosticCodes.EncryptedValues, null, enc.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        }

        if (masked > 0)
        {
            diagnostics.Add(ConfigDiagnostic.Warning(DiagnosticCodes.PasswordMasked, firstMaskedLine, masked.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        }

        return new ConfigInspection(ConfigFileKind.FortiOsText, text, doc, diagnostics, multiVdom, vdoms, pde, enc, masked, partialReasons.Count > 0);

        ConfigInspection Result(ConfigFileKind kind, ConfigDocument? d) =>
            new(kind, text, d, diagnostics, false, [], false, 0, 0, false);
    }
}

/// <summary>Official Fortinet pages the UI links to when a condition is detected.</summary>
public static class Guidance
{
    /// <summary>Backup/restore from GUI and CLI, encryption option, YAML, password masking.</summary>
    public const string BackupAndRestore = "https://docs.fortinet.com/document/fortigate/7.6.8/administration-guide/702257/configuration-backups-and-reset";

    /// <summary>Backups in multi-VDOM mode (global vs VDOM scope).</summary>
    public const string MultiVdomBackup = "https://docs.fortinet.com/document/fortigate/7.6.8/administration-guide/87472/backing-up-and-restoring-configurations-in-multi-vdom-mode";

    /// <summary>CLI reference of <c>private-data-encryption</c> (config system global).</summary>
    public const string PrivateDataEncryptionCli = "https://docs.fortinet.com/document/fortigate/7.6.8/cli-reference/339914554/config-system-global";

    /// <summary>FortiOS 7.6.1+ generates a random private-data-encryption key.</summary>
    public const string PrivateDataEncryptionRandomKey = "https://docs.fortinet.com/document/fortigate/7.6.8/fortios-release-notes/191180/fortimanager-support-for-updated-fortios-private-data-encryption-key";

    /// <summary>Backups cannot be restored after the random key changes.</summary>
    public const string PrivateDataEncryptionRestore = "https://docs.fortinet.com/document/fortigate/7.6.8/fortios-release-notes/335540/fortigate-cannot-restore-configuration-file-after-private-data-encryption-is-re-enabled";
}
