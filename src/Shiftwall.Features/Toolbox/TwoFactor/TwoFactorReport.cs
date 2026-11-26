using System.Globalization;
using System.Net;
using System.Text;
using Shiftwall.Features.Toolbox.Export;

namespace Shiftwall.Features.Toolbox.TwoFactor;

/// <summary>Report of a plan (HTML and CSV) and audit export of the accounts. Emails yes, passwords never.</summary>
public static class TwoFactorReport
{
    public static ExportTable Inventory(AccountInventory inv, CultureInfo? c = null) => new(
        T("Accounts", c),
        [T("Col.Kind", c), "VDOM", T("Col.Name", c), T("Col.Type", c), T("Col.Server", c), "two-factor", "email-to", "FortiToken", "accprofile"],
        [.. inv.Accounts.Select(a => (IReadOnlyList<string>)[Kind(a.Kind, c), a.Vdom ?? "", a.Name, a.Type, a.RemoteServer ?? "", a.EffectiveTwoFactor, a.EmailTo ?? "", a.FortiToken ?? "", a.AccProfile ?? ""])]);

    public static ExportTable Changes(TwoFactorPlan plan, CultureInfo? c = null) => new(
        T("Changes", c),
        [T("Col.Kind", c), "VDOM", T("Col.Name", c), T("Col.Action", c), T("Col.EmailBefore", c), T("Col.EmailAfter", c), T("Col.TwoFactorBefore", c), T("Col.TwoFactorAfter", c)],
        [.. plan.Items.Select(i => (IReadOnlyList<string>)[Kind(i.Key.Kind, c), i.Key.Vdom ?? "", i.Key.Name, i.Create ? T("Created", c) : T("Modified", c),
            i.EmailBefore ?? "", i.EmailAfter ?? "", i.TwoFactorBefore ?? "disable", i.TwoFactorAfter ?? "disable"])]);

    public static ExportTable Findings(TwoFactorPlan plan, IReadOnlyList<PlanFinding> inputErrors, CultureInfo? c = null) => new(
        T("Findings", c),
        [T("Col.Severity", c), T("Col.Row", c), T("Col.Message", c)],
        [.. inputErrors.Concat(plan.Findings).Select(f => (IReadOnlyList<string>)[Severity(f.Severity, c), f.Row?.ToString(CultureInfo.InvariantCulture) ?? "", f.Format(c)])]);

    public static string Csv(TwoFactorPlan plan, IReadOnlyList<PlanFinding> inputErrors, CultureInfo? c = null) =>
        CsvWriter.Write(Changes(plan, c)) + "\n" + CsvWriter.Write(Findings(plan, inputErrors, c));

    public static string Html(TwoFactorPlan plan, IReadOnlyList<PlanFinding> inputErrors, AccountInventory inv, string? sourceName, int generatedPasswords, CultureInfo? c = null)
    {
        c ??= CultureInfo.CurrentUICulture;
        var sb = new StringBuilder();
        sb.Append("<!doctype html><html lang=\"").Append(c.TwoLetterISOLanguageName).Append("\"><head><meta charset=\"utf-8\"><title>")
          .Append(E(T("Title", c))).Append("</title><style>")
          .Append("body{font-family:Segoe UI,Arial,sans-serif;margin:24px;color:#1b1b1b}table{border-collapse:collapse;width:100%;margin:12px 0}")
          .Append("th,td{border:1px solid #c8c8c8;padding:4px 8px;text-align:left;font-size:13px}th{background:#f0f0f0}")
          .Append(".Error{color:#a4262c}.Warning{color:#8a5300}.NeedsConfirmation{color:#8a5300}.Info{color:#444}")
          .Append("</style></head><body>");
        sb.Append("<h1>").Append(E(T("Title", c))).Append("</h1><p>");
        if (sourceName is { Length: > 0 }) sb.Append(E(T("Source", c))).Append(": ").Append(E(sourceName)).Append(inv.Version is { } v ? $" (FortiOS {E(v)})" : "").Append("<br>");
        sb.Append(E(ToolText.Format("TwoFactor.Report.Summary", [plan.Items.Count.ToString(c), plan.Items.Count(i => i.IsAdmin).ToString(c), plan.Items.Count(i => i.Create).ToString(c), plan.Skipped.Count.ToString(c)], c)));
        if (generatedPasswords > 0) sb.Append("<br>").Append(E(ToolText.Format("TwoFactor.Report.Passwords", [generatedPasswords.ToString(c)], c)));
        sb.Append("</p>");

        Table(sb, Changes(plan, c));
        var findings = inputErrors.Concat(plan.Findings).ToList();
        if (findings.Count > 0)
        {
            sb.Append("<h2>").Append(E(T("Findings", c))).Append("</h2><ul>");
            foreach (var f in findings) sb.Append("<li class=\"").Append(f.Severity).Append("\">").Append(E(Severity(f.Severity, c))).Append(": ").Append(E(f.Format(c))).Append("</li>");
            sb.Append("</ul>");
        }

        sb.Append("<p>").Append(E(T("Disclaimer", c))).Append("</p></body></html>");
        return sb.ToString();
    }

    private static void Table(StringBuilder sb, ExportTable t)
    {
        sb.Append("<h2>").Append(E(t.Name)).Append("</h2><table><tr>");
        foreach (var h in t.Headers) sb.Append("<th>").Append(E(h)).Append("</th>");
        sb.Append("</tr>");
        foreach (var row in t.Rows)
        {
            sb.Append("<tr>");
            foreach (var cell in row) sb.Append("<td>").Append(E(cell)).Append("</td>");
            sb.Append("</tr>");
        }

        sb.Append("</table>");
    }

    private static string E(string s) => WebUtility.HtmlEncode(s);

    private static string T(string key, CultureInfo? c) => ToolText.Get("TwoFactor.Report." + key, c);

    public static string Kind(AccountKind k, CultureInfo? c = null) => T(k == AccountKind.Admin ? "Admin" : "User", c);

    public static string Severity(PlanSeverity s, CultureInfo? c = null) => T("Severity." + s, c);
}
