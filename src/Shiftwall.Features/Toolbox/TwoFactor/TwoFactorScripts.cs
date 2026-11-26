using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Shiftwall.Core.Config;
using Shiftwall.Features.Toolbox.Anonymize;

namespace Shiftwall.Features.Toolbox.TwoFactor;

/// <param name="BatchSize">Accounts per script file. No documented size limit was found for configuration scripts
/// (FortiOS Administration Guide, "Configuration scripts", 6.4.0 – 8.0.0), so the default is 500 and can be changed.</param>
/// <param name="IncludeTimers">Adds <c>two-factor-email-expiry</c> and <c>remoteauthtimeout</c> to every script
/// (Fortinet Community, "Technical Tip: Email Two-Factor Authentication on FortiGate": both to 300 seconds).</param>
public sealed record ScriptOptions(int BatchSize = 500, bool IncludeTimers = false, int TimerSeconds = 300, string? SourceName = null)
{
    public const int MinBatch = 1;
    public const int MaxBatch = 10_000;
}

public sealed record ScriptFile(string FileName, string Text, int Accounts, bool IsAdminBatch, int Number, bool IsRollback);

/// <summary>A random password for a new local user of type password. Never written to scripts, logs or reports.</summary>
public sealed record GeneratedPassword(AccountKey Account, string Password);

public sealed record ScriptBundle(IReadOnlyList<ScriptFile> Scripts, IReadOnlyList<ScriptFile> Rollbacks, IReadOnlyList<GeneratedPassword> Passwords);

/// <summary>
/// Generates the CLI scripts in batches, with one rollback script per batch that restores the values read from the
/// backup. Syntax checked against the FortiOS CLI Reference 6.2.16, 6.4.15/16, 7.0.17, 7.2.12, 7.4.9, 7.6.6 and 8.0.0
/// (<c>config user local</c>, <c>config system admin</c>, <c>config system global</c>; checked 2026-11-26): the
/// options <c>two-factor email</c> and <c>email-to</c> (max 63 characters) exist in every one of them.
/// </summary>
public static class TwoFactorScripts
{
    public static ScriptBundle Generate(TwoFactorPlan plan, AccountInventory inventory, ScriptOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(inventory);
        options ??= new ScriptOptions();
        if (plan.IsBlocked) throw new InvalidOperationException("The plan has blocking findings: nothing can be generated.");
        if (options.BatchSize is < ScriptOptions.MinBatch or > ScriptOptions.MaxBatch) throw new ArgumentOutOfRangeException(nameof(options));
        if (options.IncludeTimers && options.TimerSeconds is < 30 or > 300) throw new ArgumentOutOfRangeException(nameof(options));

        var passwords = plan.Items.Where(i => i.NeedsPassword).Select(i => new GeneratedPassword(i.Key, NewPassword())).ToList();
        var scripts = new List<ScriptFile>();
        var rollbacks = new List<ScriptFile>();
        foreach (var (admins, prefix) in new[] { (false, "users"), (true, "admins") })
        {
            var items = plan.Items.Where(i => i.IsAdmin == admins).ToList();
            var batches = items.Chunk(options.BatchSize).ToList();
            for (var n = 0; n < batches.Count; n++)
            {
                var number = n + 1;
                var name = $"2fa-{prefix}-{number:000}.txt";
                var rollbackName = $"rollback-{prefix}-{number:000}.txt";
                var header = Header(batches[n], admins, number, batches.Count, inventory, options);
                scripts.Add(new ScriptFile(name, header(rollbackName, false) + Body(batches[n], inventory, options, rollback: false), batches[n].Length, admins, number, false));
                rollbacks.Add(new ScriptFile(rollbackName, header(name, true) + Body(batches[n], inventory, options, rollback: true), batches[n].Length, admins, number, true));
            }
        }

        return new ScriptBundle(scripts, rollbacks, passwords);
    }

    private static Func<string, bool, string> Header(PlanItem[] batch, bool admins, int number, int total, AccountInventory inv, ScriptOptions o) => (other, rollback) =>
    {
        var sb = new StringBuilder();
        var what = admins ? "administrators" : "local users";
        sb.Append(CultureInfo.InvariantCulture, $"# Shiftwall - email two-factor authentication - {(rollback ? "ROLLBACK of " : "")}batch {number} of {total} ({what})\n");
        if (o.SourceName is { Length: > 0 } src) sb.Append("# Source backup: ").Append(Comment(src)).Append(inv.Version is { } v ? $" (FortiOS {v})" : "").Append('\n');
        sb.Append(CultureInfo.InvariantCulture, $"# Accounts in this file: {batch.Length}\n");
        foreach (var g in batch.GroupBy(i => i.Key.Vdom))
        {
            if (inv.IsMultiVdom && !admins) sb.Append("#   VDOM ").Append(Comment(g.Key ?? "")).Append(CultureInfo.InvariantCulture, $": {g.Count()}\n");
            foreach (var i in g) sb.Append("#   - ").Append(Comment(i.Key.Name)).Append(i.Create ? (rollback ? " (delete: created by the batch)" : " (new local user)") : "").Append('\n');
        }

        sb.Append(rollback ? "# Restores the values read from the backup before the batch. Batch script: " : "# Rollback script: ").Append(other).Append('\n');
        if (!rollback && batch.Any(i => i.NeedsPassword)) sb.Append("# New password users are created disabled: their passwords are in the encrypted file, not here.\n");
        sb.Append("# Run it from Configuration > Scripts > Run Script (FortiOS Administration Guide, \"Configuration scripts\").\n");
        return sb.ToString();
    };

    private static string Comment(string s) => s.Replace('\r', ' ').Replace('\n', ' ');

    private static string Body(PlanItem[] batch, AccountInventory inv, ScriptOptions o, bool rollback)
    {
        var sb = new StringBuilder();
        var admins = batch[0].IsAdmin;
        if (o.IncludeTimers) Timers(sb, inv, o, rollback);

        if (admins)
        {
            var indent = inv.IsMultiVdom ? "    " : "";
            if (inv.IsMultiVdom) sb.Append("config global\n");
            Table(sb, "system admin", batch, indent, rollback);
            if (inv.IsMultiVdom) sb.Append("end\n");
            return sb.ToString();
        }

        if (!inv.IsMultiVdom)
        {
            Table(sb, "user local", batch, "", rollback);
            return sb.ToString();
        }

        sb.Append("config vdom\n");
        foreach (var g in batch.GroupBy(i => i.Key.Vdom))
        {
            sb.Append("    edit ").Append(ConfigToken.Render(g.Key ?? "root")).Append('\n');
            Table(sb, "user local", [.. g], "        ", rollback);
            sb.Append("    next\n");
        }

        sb.Append("end\n");
        return sb.ToString();
    }

    private static void Timers(StringBuilder sb, AccountInventory inv, ScriptOptions o, bool rollback)
    {
        var indent = inv.IsMultiVdom ? "    " : "";
        if (inv.IsMultiVdom) sb.Append("config global\n");
        sb.Append(indent).Append("config system global\n");
        var value = o.TimerSeconds.ToString(CultureInfo.InvariantCulture);
        Setting(sb, indent + "    ", "two-factor-email-expiry", rollback ? inv.TwoFactorEmailExpiry : value);
        Setting(sb, indent + "    ", "remoteauthtimeout", rollback ? inv.RemoteAuthTimeout : value);
        sb.Append(indent).Append("end\n");
        if (inv.IsMultiVdom) sb.Append("end\n");
    }

    private static void Setting(StringBuilder sb, string indent, string field, string? value)
    {
        if (value is null) sb.Append(indent).Append("unset ").Append(field).Append('\n');
        else sb.Append(indent).Append("set ").Append(field).Append(' ').Append(Value(value)).Append('\n');
    }

    /// <summary>Option keywords stay bare; free text (names, emails) is quoted.</summary>
    private static string Value(string v) => v.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.' or '+') ? v : ConfigToken.Render(v);

    private static void Table(StringBuilder sb, string path, IReadOnlyList<PlanItem> items, string indent, bool rollback)
    {
        sb.Append(indent).Append("config ").Append(path).Append('\n');
        var inner = indent + "    ";
        var field = inner + "    ";
        foreach (var i in rollback ? items.Reverse() : items)
        {
            if (rollback && i.Create)
            {
                sb.Append(inner).Append("delete ").Append(ConfigToken.Render(i.Key.Name)).Append('\n');
                continue;
            }

            sb.Append(inner).Append("edit ").Append(ConfigToken.Render(i.Key.Name)).Append('\n');
            if (!rollback && i.Create)
            {
                sb.Append(field).Append("set type ").Append(i.Type).Append('\n');
                var serverField = i.Type switch { "ldap" => "ldap-server", "radius" => "radius-server", "tacacs+" => "tacacs+-server", _ => null };
                if (serverField is not null) sb.Append(field).Append("set ").Append(serverField).Append(' ').Append(ConfigToken.Render(i.RemoteServer!)).Append('\n');
                if (i.NeedsPassword) sb.Append(field).Append("set status disable\n");
            }

            var (twoFactor, email) = rollback ? (i.TwoFactorBefore, i.EmailBefore) : (i.TwoFactorAfter, i.EmailAfter);
            var emailField = rollback || i.EmailChanged ? (Action<StringBuilder>)(b => Setting(b, field, "email-to", email)) : null;
            var twoFactorField = rollback || i.TwoFactorChanged ? (Action<StringBuilder>)(b => Setting(b, field, "two-factor", twoFactor)) : null;

            // email-to must exist before "two-factor email" is set, and must be removed only after it is disabled.
            if (twoFactor == "email")
            {
                emailField?.Invoke(sb);
                twoFactorField?.Invoke(sb);
            }
            else
            {
                twoFactorField?.Invoke(sb);
                emailField?.Invoke(sb);
            }

            sb.Append(inner).Append("next\n");
        }

        sb.Append(indent).Append("end\n");
    }

    /// <summary>A strong random password that is safe for the FortiOS CLI (see <see cref="Rebuild.CliPassword"/>).</summary>
    public static string NewPassword() => Rebuild.CliPassword.Generate();
}

/// <summary>
/// The passwords of new local users, in a separate file encrypted with a password chosen by the user (same format as
/// the anonymization map: AES-256-GCM, PBKDF2-HMAC-SHA256 600,000 iterations, see <see cref="MapProtector"/>). Each
/// entry carries the CLI lines that set the password and enable the user.
/// </summary>
public static class PasswordVault
{
    public const string Extension = ".swsecret";

    public sealed record Entry(string Account, string? Vdom, string Password, string Cli);

    public static byte[] Encrypt(IReadOnlyList<GeneratedPassword> passwords, AccountInventory inventory, string vaultPassword)
    {
        ArgumentNullException.ThrowIfNull(passwords);
        var entries = passwords.Select(p => new Entry(p.Account.Name, p.Account.Vdom, p.Password, Cli(p, inventory.IsMultiVdom))).ToList();
        var json = JsonSerializer.SerializeToUtf8Bytes(entries, VaultJson.Default.ListEntry);
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
            return JsonSerializer.Deserialize(json, VaultJson.Default.ListEntry) ?? [];
        }
        finally
        {
            CryptographicOperations.ZeroMemory(json);
        }
    }

    private static string Cli(GeneratedPassword p, bool multiVdom)
    {
        var body = $"config user local\n    edit {ConfigToken.Render(p.Account.Name)}\n        set passwd {ConfigToken.Render(p.Password)}\n        set status enable\n    next\nend\n";
        return multiVdom ? $"config vdom\nedit {ConfigToken.Render(p.Account.Vdom ?? "root")}\n{body}next\nend\n" : body;
    }
}

[System.Text.Json.Serialization.JsonSerializable(typeof(List<PasswordVault.Entry>))]
internal sealed partial class VaultJson : System.Text.Json.Serialization.JsonSerializerContext;
