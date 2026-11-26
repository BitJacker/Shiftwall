using System.Security.Cryptography;
using System.Text.Json;
using Shiftwall.Features.Toolbox.Anonymize;

namespace Shiftwall.Features.Rebuild;

/// <summary>
/// The password summary of a rebuild, saved only as a file encrypted with a password chosen by the user (same
/// encryption as the anonymization map: AES-256-GCM, PBKDF2-HMAC-SHA256). Never in reports, logs or scripts.
/// </summary>
public static class RebuildVault
{
    public const string Extension = ".swsecret";

    public sealed record Entry(string Category, string Path, string Object, string Param, string Password, bool Generated);

    public static byte[] Encrypt(IReadOnlyList<SecretAssignment> secrets, string vaultPassword)
    {
        ArgumentNullException.ThrowIfNull(secrets);
        var entries = secrets.Select(s => new Entry(RebuildEngine.CategoryName(s.Category), s.Path, s.Object, s.Param, s.Password, s.Generated)).ToList();
        var json = JsonSerializer.SerializeToUtf8Bytes(entries, RebuildVaultJson.Default.ListEntry);
        try
        {
            return MapProtector.Encrypt(json, vaultPassword);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(json);
        }
    }

    public static IReadOnlyList<Entry> Decrypt(byte[] file, string vaultPassword)
    {
        var json = MapProtector.Decrypt(file, vaultPassword);
        try
        {
            return JsonSerializer.Deserialize(json, RebuildVaultJson.Default.ListEntry) ?? [];
        }
        finally
        {
            CryptographicOperations.ZeroMemory(json);
        }
    }

    /// <summary>The summary shown once in the app: "these accounts and keys have this password".</summary>
    public static string Summary(IReadOnlyList<SecretAssignment> secrets, Func<SecretCategory, string> label)
    {
        ArgumentNullException.ThrowIfNull(secrets);
        ArgumentNullException.ThrowIfNull(label);
        var sb = new System.Text.StringBuilder();
        foreach (var g in secrets.GroupBy(s => (s.Category, s.Password)))
        {
            sb.Append(label(g.Key.Category)).Append(": ").Append(g.Key.Password).Append('\n');
            foreach (var s in g) sb.Append("    ").Append(s.Path).Append(" \"").Append(s.Object).Append("\" (").Append(s.Param).Append(")\n");
        }

        return sb.ToString();
    }
}

[System.Text.Json.Serialization.JsonSerializable(typeof(List<RebuildVault.Entry>))]
internal sealed partial class RebuildVaultJson : System.Text.Json.Serialization.JsonSerializerContext;
