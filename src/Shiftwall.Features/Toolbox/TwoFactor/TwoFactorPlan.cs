using System.Globalization;
using System.Text.RegularExpressions;

namespace Shiftwall.Features.Toolbox.TwoFactor;

/// <summary>What to do on an account (CSV column <c>azione</c> / <c>action</c>).</summary>
public enum ChangeAction
{
    /// <summary>Set the email and enable email two-factor authentication.</summary>
    Set,

    /// <summary>Change the email only (two-factor unchanged).</summary>
    Modify,

    /// <summary>Remove the email (and disable email two-factor, which cannot work without it).</summary>
    Remove,

    /// <summary>Enable email two-factor with the email given or already configured.</summary>
    EnableTwoFactor,

    /// <summary>Disable two-factor authentication.</summary>
    DisableTwoFactor,
}

/// <summary>One requested change (a CSV/Excel row or an edit in the table).</summary>
/// <param name="Row">CSV row number (1 = header), or 0 for edits made in the table.</param>
/// <param name="Type">Account type for accounts to create: <c>password</c>, <c>ldap</c>, <c>radius</c>, <c>tacacs+</c>.</param>
/// <param name="Kind">Restricts the match to local users or administrators (CSV column <c>ambito</c>, optional).</param>
/// <param name="Vdom">VDOM of the local user (CSV column <c>vdom</c>, optional; required to create users in multi-VDOM backups).</param>
public sealed record ChangeRequest(
    int Row,
    string Username,
    string? Email,
    ChangeAction Action,
    string? Type = null,
    string? RemoteServer = null,
    AccountKind? Kind = null,
    string? Vdom = null);

/// <summary>
/// Completes partial emails: <c>mario.rossi</c> + domain <c>azienda.it</c> → <c>mario.rossi@azienda.it</c>; with a
/// template such as <c>{name}.{surname}@azienda.it</c>, an empty email is built from the user name split on
/// <c>.</c>, <c>_</c>, <c>-</c> or spaces (first part = name, last part = surname).
/// </summary>
public sealed partial record EmailRule(string? DefaultDomain = null, string? Template = null)
{
    [GeneratedRegex(@"^[A-Za-z0-9](?:[A-Za-z0-9-]{0,61}[A-Za-z0-9])?(?:\.[A-Za-z0-9](?:[A-Za-z0-9-]{0,61}[A-Za-z0-9])?)+$")]
    private static partial Regex DomainRegex();

    public static bool IsValidDomain(string domain) => DomainRegex().IsMatch(domain.TrimStart('@'));

    /// <summary>The completed email, or the input unchanged; <paramref name="completed"/> tells whether the rule was used.</summary>
    public string? Apply(string username, string? email, out bool completed)
    {
        completed = false;
        var domain = DefaultDomain?.Trim().TrimStart('@');
        if (!string.IsNullOrWhiteSpace(email))
        {
            email = email.Trim();
            if (email.Contains('@', StringComparison.Ordinal) || string.IsNullOrEmpty(domain)) return email;
            completed = true;
            return $"{email}@{domain}";
        }

        if (string.IsNullOrWhiteSpace(Template)) return null;
        var parts = username.Split(['.', '_', '-', ' '], StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return null;
        var name = parts[0].ToLowerInvariant();
        var surname = parts.Length > 1 ? parts[^1].ToLowerInvariant() : "";
        if (surname.Length == 0 && Template.Contains("{surname}", StringComparison.Ordinal)) return null;
        var result = Template.Trim()
            .Replace("{username}", username.ToLowerInvariant(), StringComparison.Ordinal)
            .Replace("{name}", name, StringComparison.Ordinal)
            .Replace("{surname}", surname, StringComparison.Ordinal);
        if (!result.Contains('@', StringComparison.Ordinal) && !string.IsNullOrEmpty(domain)) result += "@" + domain;
        completed = true;
        return result;
    }
}

public enum PlanSeverity
{
    Info,
    Warning,

    /// <summary>Blocks generation until the user confirms (row by row, or the administrator double confirmation).</summary>
    NeedsConfirmation,
    Error,
}

/// <summary>A finding of the validation. Texts: Resources/ToolboxMessages(.it).resx, keys <c>TwoFactor.{Code}</c>.</summary>
public sealed record PlanFinding(string Code, PlanSeverity Severity, int? Row, AccountKey? Account, IReadOnlyList<string> Arguments)
{
    public string Format(CultureInfo? c = null)
    {
        var text = ToolText.Format("TwoFactor." + Code, Arguments, c);
        return Row is > 0 ? ToolText.Format("TwoFactor.RowPrefix", [Row.Value.ToString(CultureInfo.InvariantCulture), text], c) : text;
    }
}

/// <summary>The change that will be made on one account: values before and after (never a password).</summary>
public sealed record PlanItem(
    AccountKey Key,
    int Row,
    AccountEntry? Before,
    bool Create,
    string Type,
    string? RemoteServer,
    string? TwoFactorAfter,
    string? EmailAfter)
{
    public bool IsAdmin => Key.Kind == AccountKind.Admin;

    public string? TwoFactorBefore => Before?.TwoFactor;

    public string? EmailBefore => Before?.EmailTo;

    public bool EmailChanged => Create || EmailBefore != EmailAfter;

    public bool TwoFactorChanged => Create || (TwoFactorBefore ?? "disable") != (TwoFactorAfter ?? "disable");

    /// <summary>A new local user of type password: its random password goes to the encrypted file only.</summary>
    public bool NeedsPassword => Create && Type == "password";
}

/// <summary>Choices the user makes before generating.</summary>
/// <param name="CreateMissingUsers">Create local users that are not in the configuration (e.g. directory users as local users of type ldap). Off by default.</param>
/// <param name="ConfirmedFortiToken">Accounts with a FortiToken whose switch to email two-factor was confirmed row by row.</param>
/// <param name="AdminFirstConfirmation">First confirmation for changes that touch administrators.</param>
/// <param name="AdminSecondConfirmation">Second confirmation for changes that touch administrators.</param>
public sealed record PlanOptions(
    EmailRule? EmailRule = null,
    bool CreateMissingUsers = false,
    IReadOnlySet<AccountKey>? ConfirmedFortiToken = null,
    bool AdminFirstConfirmation = false,
    bool AdminSecondConfirmation = false);

public sealed record TwoFactorPlan(IReadOnlyList<PlanItem> Items, IReadOnlyList<PlanFinding> Findings, IReadOnlyList<ChangeRequest> Skipped)
{
    public bool IsBlocked => Findings.Any(f => f.Severity is PlanSeverity.Error or PlanSeverity.NeedsConfirmation);

    public bool TouchesAdmins => Items.Any(i => i.IsAdmin);
}

/// <summary>
/// Validates requests against the configuration and computes the changes. Nothing is generated when the plan is blocked.
/// </summary>
public static partial class TwoFactorPlanner
{
    /// <summary>Maximum length of <c>email-to</c> (FortiOS CLI Reference 6.2.16 – 8.0.0: "Maximum length: 63").</summary>
    public const int MaxEmailLength = 63;

    /// <summary>Consumer mail domains: an administrator's 2FA code sent there leaves the organisation.</summary>
    private static readonly string[] ExternalDomains =
    [
        "gmail.com", "googlemail.com", "outlook.com", "hotmail.com", "hotmail.it", "live.com", "live.it", "msn.com",
        "yahoo.com", "yahoo.it", "icloud.com", "me.com", "aol.com", "gmx.com", "gmx.net", "gmx.de", "proton.me",
        "protonmail.com", "libero.it", "virgilio.it", "tiscali.it", "alice.it", "tim.it", "fastwebnet.it", "email.it",
        "inwind.it", "yandex.com", "mail.ru", "zoho.com",
    ];

    [GeneratedRegex(@"^[A-Za-z0-9!#$%&'*+/=?^_`{|}~-]+(?:\.[A-Za-z0-9!#$%&'*+/=?^_`{|}~-]+)*@[A-Za-z0-9](?:[A-Za-z0-9-]{0,61}[A-Za-z0-9])?(?:\.[A-Za-z0-9](?:[A-Za-z0-9-]{0,61}[A-Za-z0-9])?)+$")]
    private static partial Regex EmailRegex();

    public static bool IsValidEmail(string email) => EmailRegex().IsMatch(email);

    public static bool IsExternalDomain(string email)
    {
        var at = email.LastIndexOf('@');
        return at >= 0 && ExternalDomains.Contains(email[(at + 1)..].ToLowerInvariant());
    }

    private static readonly string[] CreatableTypes = ["password", "ldap", "radius", "tacacs+"];

    public static TwoFactorPlan Plan(AccountInventory inventory, IReadOnlyList<ChangeRequest> requests, PlanOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(inventory);
        ArgumentNullException.ThrowIfNull(requests);
        options ??= new PlanOptions();
        var findings = new List<PlanFinding>();
        var skipped = new List<ChangeRequest>();
        var resolved = new List<(ChangeRequest Request, AccountKey Key, AccountEntry? Before, string? Email)>();

        if (!inventory.EmailServer.IsUsable) findings.Add(new("EmailServerMissing", PlanSeverity.Error, null, null, []));

        foreach (var g in inventory.RemoteGroups)
        {
            findings.Add(new("RemoteGroup", PlanSeverity.Info, null, null, [g.Name, string.Join(", ", g.Servers)]));
        }

        // 1. Resolve every row to one account (existing, or to create).
        foreach (var r in requests)
        {
            if (string.IsNullOrWhiteSpace(r.Username))
            {
                findings.Add(new("EmptyUsername", PlanSeverity.Error, r.Row, null, []));
                skipped.Add(r);
                continue;
            }

            var email = options.EmailRule is { } rule ? rule.Apply(r.Username, r.Email, out var completed) : Trim(r.Email, out completed);
            if (completed && email is not null) findings.Add(new("EmailCompleted", PlanSeverity.Info, r.Row, null, [r.Username, email]));

            var candidates = Candidates(inventory, r).ToList();
            if (candidates.Count > 1)
            {
                findings.Add(new("Ambiguous", PlanSeverity.Error, r.Row, null, [r.Username, string.Join(", ", candidates.Select(c => c.Key.ToString()))]));
                skipped.Add(r);
                continue;
            }

            if (candidates.Count == 1)
            {
                resolved.Add((r, candidates[0].Key, candidates[0], email));
                continue;
            }

            if (!CanCreate(inventory, r, options, findings))
            {
                skipped.Add(r);
                continue;
            }

            var vdom = inventory.IsMultiVdom ? r.Vdom : null;
            resolved.Add((r, new AccountKey(AccountKind.LocalUser, vdom, r.Username), null, email));
        }

        // 2. Duplicate and conflicting rows for the same account.
        var items = new List<PlanItem>();
        foreach (var group in resolved.GroupBy(x => x.Key))
        {
            var list = group.ToList();
            var distinct = list.Select(x => (x.Request.Action, Email: x.Email?.ToLowerInvariant(), x.Request.Type, x.Request.RemoteServer)).Distinct().Count();
            if (distinct > 1)
            {
                var rows = string.Join(", ", list.Select(x => x.Request.Row.ToString(CultureInfo.InvariantCulture)));
                foreach (var x in list)
                {
                    findings.Add(new("Conflict", PlanSeverity.Error, x.Request.Row, group.Key, [group.Key.ToString(), rows]));
                    skipped.Add(x.Request);
                }

                continue;
            }

            foreach (var dup in list.Skip(1))
            {
                findings.Add(new("Duplicate", PlanSeverity.Warning, dup.Request.Row, group.Key, [group.Key.ToString(), list[0].Request.Row.ToString(CultureInfo.InvariantCulture)]));
                skipped.Add(dup.Request);
            }

            var (request, key, before, email) = list[0];
            if (Item(request, key, before, email, options, findings) is { } item) items.Add(item);
            else skipped.Add(request);
        }

        // 3. Checks on the result as a whole.
        CheckSharedEmails(inventory, items, findings);
        CheckAdmins(inventory, items, options, findings);
        return new TwoFactorPlan(items, findings, skipped);
    }

    private static string? Trim(string? email, out bool completed)
    {
        completed = false;
        return string.IsNullOrWhiteSpace(email) ? null : email.Trim();
    }

    private static IEnumerable<AccountEntry> Candidates(AccountInventory inv, ChangeRequest r)
    {
        IEnumerable<AccountEntry> all = inv.Accounts.Where(a => a.Name == r.Username);
        if (r.Kind is { } k) all = all.Where(a => a.Kind == k);
        if (!string.IsNullOrWhiteSpace(r.Vdom)) all = all.Where(a => a.Kind == AccountKind.Admin || a.Vdom == r.Vdom);
        return all;
    }

    private static bool CanCreate(AccountInventory inv, ChangeRequest r, PlanOptions options, List<PlanFinding> findings)
    {
        if (r.Kind == AccountKind.Admin)
        {
            findings.Add(new("AdminNotFound", PlanSeverity.Error, r.Row, null, [r.Username]));
            return false;
        }

        var type = r.Type?.Trim().ToLowerInvariant();
        if (r.Action != ChangeAction.Set)
        {
            findings.Add(new(type == "ldap" ? "DirectoryOnly" : "UnknownAccount", PlanSeverity.Error, r.Row, null, [r.Username]));
            return false;
        }

        if (!options.CreateMissingUsers)
        {
            findings.Add(new(type == "ldap" ? "DirectoryOnly" : "UnknownAccount", PlanSeverity.Error, r.Row, null, [r.Username]));
            return false;
        }

        if (type is null || !CreatableTypes.Contains(type))
        {
            findings.Add(new("TypeRequired", PlanSeverity.Error, r.Row, null, [r.Username, r.Type ?? ""]));
            return false;
        }

        if (inv.IsMultiVdom && (string.IsNullOrWhiteSpace(r.Vdom) || !inv.Vdoms.Contains(r.Vdom)))
        {
            findings.Add(new("VdomRequired", PlanSeverity.Error, r.Row, null, [r.Username, r.Vdom ?? "", string.Join(", ", inv.Vdoms)]));
            return false;
        }

        if (type != "password")
        {
            if (string.IsNullOrWhiteSpace(r.RemoteServer))
            {
                findings.Add(new("RemoteServerRequired", PlanSeverity.Error, r.Row, null, [r.Username, type]));
                return false;
            }

            if (!inv.HasRemoteServer(type, inv.IsMultiVdom ? r.Vdom : null, r.RemoteServer.Trim()))
            {
                findings.Add(new("UnknownRemoteServer", PlanSeverity.Error, r.Row, null, [r.RemoteServer.Trim(), type]));
                return false;
            }
        }

        findings.Add(new(type == "password" ? "WillCreatePassword" : "WillCreate", PlanSeverity.Info, r.Row, null, [r.Username, type]));
        return true;
    }

    private static PlanItem? Item(ChangeRequest r, AccountKey key, AccountEntry? before, string? email, PlanOptions options, List<PlanFinding> findings)
    {
        var create = before is null;
        var type = create ? r.Type!.Trim().ToLowerInvariant() : before!.Type;
        var server = create ? r.RemoteServer?.Trim() : before!.RemoteServer;
        var twoFactor = before?.TwoFactor;
        var emailAfter = before?.EmailTo;

        switch (r.Action)
        {
            case ChangeAction.Set:
                if (email is null) return Fail("EmailRequired");
                emailAfter = email;
                twoFactor = "email";
                break;
            case ChangeAction.Modify:
                if (email is null) return Fail("EmailRequired");
                emailAfter = email;
                break;
            case ChangeAction.Remove:
                emailAfter = null;
                if (before?.HasEmailTwoFactor == true) twoFactor = "disable";
                break;
            case ChangeAction.EnableTwoFactor:
                emailAfter = email ?? before?.EmailTo;
                if (emailAfter is null) return Fail("EmailRequired");
                twoFactor = "email";
                break;
            case ChangeAction.DisableTwoFactor:
                twoFactor = "disable";
                if (before?.EffectiveTwoFactor is "fortitoken" or "fortitoken-cloud")
                {
                    findings.Add(new("DisableToken", PlanSeverity.Warning, r.Row, key, [key.ToString(), before.EffectiveTwoFactor]));
                }

                break;
        }

        if (emailAfter is not null && emailAfter != before?.EmailTo)
        {
            if (emailAfter.Length > MaxEmailLength) return Fail("EmailTooLong", MaxEmailLength.ToString(CultureInfo.InvariantCulture));
            if (!IsValidEmail(emailAfter)) return Fail("InvalidEmail", emailAfter);
        }

        var item = new PlanItem(key, r.Row, before, create, type, server, twoFactor, emailAfter);
        if (!item.EmailChanged && !item.TwoFactorChanged)
        {
            findings.Add(new("NoChange", PlanSeverity.Info, r.Row, key, [key.ToString()]));
            return null;
        }

        if (before is { HasFortiToken: true } && twoFactor == "email" && before.EffectiveTwoFactor != "email")
        {
            var confirmed = options.ConfirmedFortiToken?.Contains(key) == true;
            findings.Add(new(confirmed ? "TokenConfirmed" : "TokenConfirm", confirmed ? PlanSeverity.Info : PlanSeverity.NeedsConfirmation, r.Row, key, [key.ToString(), before.FortiToken!]));
        }

        return item;

        PlanItem? Fail(string code, params string[] extra)
        {
            findings.Add(new(code, PlanSeverity.Error, r.Row, key, [key.ToString(), .. extra]));
            return null;
        }
    }

    private static void CheckSharedEmails(AccountInventory inv, List<PlanItem> items, List<PlanFinding> findings)
    {
        var after = inv.Accounts.ToDictionary(a => a.Key, a => a.EmailTo);
        foreach (var i in items) after[i.Key] = i.EmailAfter;
        var changed = items.Where(i => i.EmailChanged && i.EmailAfter is not null).Select(i => i.EmailAfter!.ToLowerInvariant()).ToHashSet();
        foreach (var g in after.Where(kv => kv.Value is not null).GroupBy(kv => kv.Value!.ToLowerInvariant()).Where(g => g.Count() > 1 && changed.Contains(g.Key)))
        {
            findings.Add(new("SharedEmail", PlanSeverity.Warning, null, null, [g.Key, string.Join(", ", g.Select(kv => kv.Key.ToString()))]));
        }
    }

    private static void CheckAdmins(AccountInventory inv, List<PlanItem> items, PlanOptions options, List<PlanFinding> findings)
    {
        var admins = items.Where(i => i.IsAdmin).ToList();
        if (admins.Count == 0) return;

        foreach (var a in admins.Where(a => a.EmailAfter is not null && a.EmailChanged && IsExternalDomain(a.EmailAfter)))
        {
            findings.Add(new("AdminExternalEmail", PlanSeverity.Warning, a.Row, a.Key, [a.Key.Name, a.EmailAfter!]));
        }

        var enabling = admins.Where(a => a.TwoFactorAfter == "email" && a.TwoFactorChanged).ToList();
        var superAdmins = inv.Admins.Where(a => a.IsSuperAdmin).ToList();
        if (superAdmins.Count > 0)
        {
            var afterTwoFactor = superAdmins.ToDictionary(a => a.Key, a => a.EffectiveTwoFactor);
            foreach (var a in admins.Where(a => afterTwoFactor.ContainsKey(a.Key))) afterTwoFactor[a.Key] = a.TwoFactorAfter ?? "disable";
            var touchedSuper = enabling.Any(a => afterTwoFactor.ContainsKey(a.Key));
            if (touchedSuper && afterTwoFactor.Values.All(v => v == "email"))
            {
                findings.Add(new("AllSuperAdmins", PlanSeverity.Error, null, null, [string.Join(", ", superAdmins.Select(s => s.Name))]));
            }
        }

        if (enabling.Count > 0)
        {
            findings.Add(new("BreakGlass", PlanSeverity.Warning, null, null, []));
            if (enabling.Count > 1) findings.Add(new("TestOneAdmin", PlanSeverity.Warning, null, null, [enabling.Count.ToString(CultureInfo.InvariantCulture)]));
        }

        var confirmed = options.AdminFirstConfirmation && options.AdminSecondConfirmation;
        findings.Add(new(confirmed ? "AdminConfirmed" : "AdminConfirm", confirmed ? PlanSeverity.Info : PlanSeverity.NeedsConfirmation, null, null,
            [admins.Count.ToString(CultureInfo.InvariantCulture), string.Join(", ", admins.Select(a => a.Key.Name))]));
    }
}
