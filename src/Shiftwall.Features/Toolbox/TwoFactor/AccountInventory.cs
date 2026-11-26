using Shiftwall.Core.Config;

namespace Shiftwall.Features.Toolbox.TwoFactor;

/// <summary>Local user (<c>config user local</c>, per VDOM) or administrator (<c>config system admin</c>, global).</summary>
public enum AccountKind
{
    LocalUser,
    Admin,
}

/// <summary>
/// One account as found in the configuration. Field names follow the FortiOS CLI Reference (6.2.16 – 8.0.0,
/// <c>config user local</c> and <c>config system admin</c>, checked 2026-11-26): <c>type</c>, <c>ldap-server</c>,
/// <c>radius-server</c>, <c>tacacs+-server</c>, <c>two-factor</c>, <c>email-to</c>, <c>fortitoken</c>, <c>accprofile</c>.
/// No password is ever read.
/// </summary>
/// <param name="Vdom">VDOM of a local user in a multi-VDOM backup; null otherwise (and for administrators).</param>
/// <param name="Type">Local users: the <c>type</c> option (default <c>password</c>). Administrators: <c>password</c>, or <c>remote</c> when <c>remote-auth</c> is enabled.</param>
/// <param name="RemoteServer">LDAP/RADIUS/TACACS+/SAML server of a local user; remote group of an administrator.</param>
/// <param name="TwoFactor">The <c>two-factor</c> option; null when not set (FortiOS default: disable).</param>
/// <param name="Line">Line of the <c>edit</c> statement (1-based).</param>
public sealed record AccountEntry(
    AccountKind Kind,
    string? Vdom,
    string Name,
    string Type,
    string? RemoteServer,
    string? TwoFactor,
    string? EmailTo,
    string? FortiToken,
    string? AccProfile,
    int Line)
{
    public bool IsAdmin => Kind == AccountKind.Admin;

    public bool IsSuperAdmin => IsAdmin && AccProfile == "super_admin";

    public bool HasFortiToken => !string.IsNullOrEmpty(FortiToken);

    public string EffectiveTwoFactor => TwoFactor ?? "disable";

    public bool HasEmailTwoFactor => EffectiveTwoFactor == "email";

    /// <summary>Stable key: kind, VDOM and name (user names are unique per VDOM, admin names globally).</summary>
    public AccountKey Key => new(Kind, Vdom, Name);
}

public readonly record struct AccountKey(AccountKind Kind, string? Vdom, string Name)
{
    public override string ToString() => Kind == AccountKind.Admin ? $"admin {Name}" : Vdom is null ? Name : $"{Name} ({Vdom})";
}

/// <summary><c>config system email-server</c> (global). Never contains the SMTP password.</summary>
public sealed record EmailServerSettings(bool BlockPresent, string? Type, string? Server, string? Port, string? Authenticate, string? Username, string? Security)
{
    /// <summary>
    /// A server is configured explicitly. A backup without the block, or with an empty <c>server</c>, is refused: the
    /// CLI Reference documents different defaults (blank when unset; <c>fortinet-notifications.com</c> after a factory
    /// reset on 7.6) and the backup does not tell which one applies.
    /// </summary>
    public bool IsUsable => BlockPresent && !string.IsNullOrWhiteSpace(Server);
}

/// <summary>An LDAP/RADIUS/TACACS+ server defined in the configuration (name only).</summary>
public sealed record RemoteServer(string Kind, string? Vdom, string Name);

/// <summary>A user group whose members include a remote server: its directory users are not local users.</summary>
public sealed record RemoteGroup(string? Vdom, string Name, IReadOnlyList<string> Servers);

/// <summary>Accounts and related settings read from one configuration.</summary>
public sealed record AccountInventory(
    IReadOnlyList<AccountEntry> Accounts,
    EmailServerSettings EmailServer,
    string? TwoFactorEmailExpiry,
    string? RemoteAuthTimeout,
    IReadOnlyList<RemoteServer> RemoteServers,
    IReadOnlyList<RemoteGroup> RemoteGroups,
    bool IsMultiVdom,
    IReadOnlyList<string> Vdoms,
    string? Version)
{
    public IEnumerable<AccountEntry> Users => Accounts.Where(a => a.Kind == AccountKind.LocalUser);

    public IEnumerable<AccountEntry> Admins => Accounts.Where(a => a.Kind == AccountKind.Admin);

    public AccountEntry? Find(AccountKind kind, string? vdom, string name) =>
        Accounts.FirstOrDefault(a => a.Kind == kind && a.Name == name && (kind == AccountKind.Admin || a.Vdom == vdom));

    /// <summary>Local users with this name in any VDOM.</summary>
    public IEnumerable<AccountEntry> UsersNamed(string name) => Users.Where(a => a.Name == name);

    public bool HasRemoteServer(string kind, string? vdom, string name) =>
        RemoteServers.Any(s => s.Kind == kind && s.Name == name && (s.Vdom == vdom || !IsMultiVdom));

    /// <summary>Reads the accounts of a parsed configuration.</summary>
    public static AccountInventory Read(ConfigDocument doc)
    {
        ArgumentNullException.ThrowIfNull(doc);
        var accounts = new List<AccountEntry>();
        var servers = new List<RemoteServer>();
        var groups = new List<RemoteGroup>();
        var vdoms = new List<string>();

        foreach (var (vdom, scope) in ConfigTables.VdomScopes(doc))
        {
            if (vdom is not null && !vdoms.Contains(vdom)) vdoms.Add(vdom);
            foreach (var e in ConfigTables.Entries(scope, "user local"))
            {
                var type = ConfigTables.Value(e, "type") ?? "password";
                var server = type switch
                {
                    "ldap" => ConfigTables.Value(e, "ldap-server"),
                    "radius" => ConfigTables.Value(e, "radius-server"),
                    "tacacs+" => ConfigTables.Value(e, "tacacs+-server"),
                    "saml" => ConfigTables.Value(e, "saml-server"),
                    _ => null,
                };
                accounts.Add(new AccountEntry(AccountKind.LocalUser, vdom, e.Key, type, server,
                    ConfigTables.Value(e, "two-factor"), ConfigTables.Value(e, "email-to"), ConfigTables.Value(e, "fortitoken"), null, e.Statement.Line));
            }

            foreach (var (path, kind) in new[] { ("user ldap", "ldap"), ("user radius", "radius"), ("user tacacs+", "tacacs+") })
            {
                foreach (var e in ConfigTables.Entries(scope, path)) servers.Add(new RemoteServer(kind, vdom, e.Key));
            }

            var serverNames = servers.Where(s => s.Vdom == vdom).Select(s => s.Name).ToHashSet(StringComparer.Ordinal);
            foreach (var g in ConfigTables.Entries(scope, "user group"))
            {
                var members = ConfigTables.Values(g, "member") ?? [];
                var remote = members.Where(serverNames.Contains).ToList();
                if (remote.Count > 0) groups.Add(new RemoteGroup(vdom, g.Key, remote));
            }
        }

        var global = doc.GlobalScope;
        foreach (var e in ConfigTables.Entries(global, "system admin"))
        {
            var remote = ConfigTables.Value(e, "remote-auth") == "enable";
            accounts.Add(new AccountEntry(AccountKind.Admin, null, e.Key, remote ? "remote" : "password",
                remote ? ConfigTables.Value(e, "remote-group") : null,
                ConfigTables.Value(e, "two-factor"), ConfigTables.Value(e, "email-to"), ConfigTables.Value(e, "fortitoken"),
                ConfigTables.Value(e, "accprofile"), e.Statement.Line));
        }

        var mail = ConfigTables.Settings(global, "system email-server");
        var emailServer = new EmailServerSettings(mail is not null,
            ConfigTables.Value(mail, "type"), ConfigTables.Value(mail, "server"), ConfigTables.Value(mail, "port"),
            ConfigTables.Value(mail, "authenticate"), ConfigTables.Value(mail, "username"), ConfigTables.Value(mail, "security"));
        var settings = ConfigTables.Settings(global, "system global");

        return new AccountInventory(accounts, emailServer,
            ConfigTables.Value(settings, "two-factor-email-expiry"), ConfigTables.Value(settings, "remoteauthtimeout"),
            servers, groups, doc.IsMultiVdom, vdoms, doc.Header.Version);
    }
}
