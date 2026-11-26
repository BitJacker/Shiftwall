using System.Globalization;
using Shiftwall.Features.Toolbox.Import;

namespace Shiftwall.Features.Toolbox.TwoFactor;

/// <summary>Reads change requests from CSV or Excel (first sheet). Column names in Italian or English.</summary>
public static class TwoFactorInput
{
    public static readonly string[] TemplateColumns = ["username", "email", "tipo", "server_remoto", "azione", "ambito", "vdom"];

    private static readonly Dictionary<string, string> ColumnAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["username"] = "username", ["utente"] = "username", ["nome_utente"] = "username", ["user"] = "username", ["name"] = "username",
        ["email"] = "email", ["e-mail"] = "email", ["email-to"] = "email", ["mail"] = "email",
        ["tipo"] = "type", ["type"] = "type",
        ["server_remoto"] = "server", ["remote_server"] = "server", ["server"] = "server",
        ["azione"] = "action", ["action"] = "action",
        ["ambito"] = "scope", ["scope"] = "scope",
        ["vdom"] = "vdom",
    };

    private static readonly Dictionary<string, ChangeAction> Actions = new(StringComparer.OrdinalIgnoreCase)
    {
        ["set"] = ChangeAction.Set, ["imposta"] = ChangeAction.Set,
        ["modifica"] = ChangeAction.Modify, ["modify"] = ChangeAction.Modify,
        ["rimuovi"] = ChangeAction.Remove, ["remove"] = ChangeAction.Remove,
        ["abilita2fa"] = ChangeAction.EnableTwoFactor, ["enable2fa"] = ChangeAction.EnableTwoFactor,
        ["disabilita2fa"] = ChangeAction.DisableTwoFactor, ["disable2fa"] = ChangeAction.DisableTwoFactor,
    };

    public static string Template() =>
        string.Join(',', TemplateColumns) + "\n" +
        "mario.rossi,mario.rossi@example.com,,,set,utente,\n" +
        "anna.bianchi,,ldap,AD-SERVER,set,utente,\n" +
        "admin-test,it-security@example.com,,,abilita2fa,admin,\n";

    public static (List<ChangeRequest> Requests, List<PlanFinding> Errors) ParseCsv(string text) => Parse(CsvReader.Parse(text, out _));

    public static (List<ChangeRequest> Requests, List<PlanFinding> Errors) ParseXlsx(byte[] file) => Parse(XlsxReader.Read(file));

    public static (List<ChangeRequest> Requests, List<PlanFinding> Errors) Parse(List<List<string>> rows)
    {
        var requests = new List<ChangeRequest>();
        var errors = new List<PlanFinding>();
        if (rows.Count == 0)
        {
            errors.Add(new("EmptyFile", PlanSeverity.Error, null, null, []));
            return (requests, errors);
        }

        var columns = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var i = 0; i < rows[0].Count; i++)
        {
            if (ColumnAliases.TryGetValue(rows[0][i].Trim().TrimStart('﻿'), out var name) && !columns.ContainsKey(name)) columns[name] = i;
        }

        if (!columns.ContainsKey("username"))
        {
            errors.Add(new("MissingColumn", PlanSeverity.Error, 1, null, ["username"]));
            return (requests, errors);
        }

        for (var r = 1; r < rows.Count; r++)
        {
            var row = rows[r];
            string? Get(string c) => columns.TryGetValue(c, out var i) && i < row.Count && !string.IsNullOrWhiteSpace(row[i]) ? row[i].Trim() : null;
            if (row.All(string.IsNullOrWhiteSpace)) continue;
            var line = r + 1;

            var actionText = Get("action");
            var action = ChangeAction.Set;
            if (actionText is not null && !Actions.TryGetValue(actionText.Replace(" ", "", StringComparison.Ordinal), out action))
            {
                errors.Add(new("InvalidAction", PlanSeverity.Error, line, null, [actionText]));
                continue;
            }

            AccountKind? kind = Get("scope")?.ToLowerInvariant() switch
            {
                null => null,
                "utente" or "user" or "utenti" or "users" or "local" => AccountKind.LocalUser,
                "admin" or "amministratore" or "administrator" => AccountKind.Admin,
                _ => (AccountKind?)(AccountKind)(-1),
            };
            if (kind == (AccountKind)(-1))
            {
                errors.Add(new("InvalidScope", PlanSeverity.Error, line, null, [Get("scope")!]));
                continue;
            }

            requests.Add(new ChangeRequest(line, Get("username") ?? "", Get("email"), action, Get("type")?.ToLowerInvariant(), Get("server"), kind, Get("vdom")));
        }

        return (requests, errors);
    }

    public static string Format(ChangeAction a) => a switch
    {
        ChangeAction.Set => "set",
        ChangeAction.Modify => "modifica",
        ChangeAction.Remove => "rimuovi",
        ChangeAction.EnableTwoFactor => "abilita2fa",
        ChangeAction.DisableTwoFactor => "disabilita2fa",
        _ => a.ToString().ToLower(CultureInfo.InvariantCulture),
    };
}
