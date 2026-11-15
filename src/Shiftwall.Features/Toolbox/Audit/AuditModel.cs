using System.Globalization;

namespace Shiftwall.Features.Toolbox.Audit;

public enum AuditSeverity
{
    Low,
    Medium,
    High,
}

/// <summary>A published source a rule is based on.</summary>
public sealed record AuditSource(string Title, string Url);

/// <summary>
/// A best-practice rule. Its title, explanation and fix are localized (keys "{Id}.Title", "{Id}.Why", "{Id}.Fix"
/// in ToolboxMessages); <see cref="Sources"/> lists the documents it comes from.
/// </summary>
public sealed record AuditRule(string Id, AuditSeverity Severity, IReadOnlyList<AuditSource> Sources)
{
    public string Title(CultureInfo? c = null) => ToolText.Get(Id + ".Title", c);

    public string Why(CultureInfo? c = null) => ToolText.Get(Id + ".Why", c);

    public string Fix(CultureInfo? c = null) => ToolText.Get(Id + ".Fix", c);
}

/// <summary>One occurrence of a rule violation. <see cref="Location"/> names the VDOM, section and entry.</summary>
public sealed record AuditFinding(string RuleId, AuditSeverity Severity, string Location, IReadOnlyList<string> Arguments)
{
    /// <summary>Detail text ("{RuleId}.Detail" formatted with the arguments).</summary>
    public string Detail(CultureInfo? c = null) => ToolText.Format(RuleId + ".Detail", Arguments, c);
}

/// <summary>Result of an audit.</summary>
/// <param name="Score">0-100: 100 minus the weight of every failed rule (High 15, Medium 8, Low 3).</param>
public sealed record AuditReport(IReadOnlyList<AuditRule> Rules, IReadOnlyList<AuditFinding> Findings, int Score, IReadOnlyList<string> Notes)
{
    public IEnumerable<AuditRule> FailedRules => Rules.Where(r => Findings.Any(f => f.RuleId == r.Id));

    public IEnumerable<AuditRule> PassedRules => Rules.Where(r => Findings.All(f => f.RuleId != r.Id));

    public static int Weight(AuditSeverity s) => s switch
    {
        AuditSeverity.High => 15,
        AuditSeverity.Medium => 8,
        _ => 3,
    };
}
