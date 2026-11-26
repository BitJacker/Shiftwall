using System.Globalization;
using System.Text;
using Shiftwall.Features.Migration;

namespace Shiftwall.Features.Rebuild;

/// <summary>HTML report of a rebuild: counts, applied rules with sources, manual work, checklist. Never contains passwords.</summary>
public static class RebuildReport
{
    /// <summary>What to do on the real FortiGate, in order (texts in MigrationMessages).</summary>
    public static IReadOnlyList<string> Checklist { get; } = ["RBC-001", "RBC-002", "RBC-003", "RBC-004", "RBC-005", "RBC-006", "RBC-007", "RBC-008"];

    public static string Build(RebuildResult result, ReportContext ctx)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(ctx);
        var c = ctx.Culture;
        string T(string key) => MigrationText.Get(key, c);
        static string E(string? s) => HtmlReport.Escape(s);
        var sb = new StringBuilder();
        sb.Append("<!doctype html><html lang=\"").Append(c.TwoLetterISOLanguageName).Append("\"><head><meta charset=\"utf-8\"><title>")
          .Append(E(T("RBR.Title"))).Append("</title><style>").Append(HtmlReport.Css).Append("</style></head><body><main>");
        sb.Append("<h1>").Append(E(T("RBR.Title"))).Append("</h1>");
        sb.Append(CultureInfo.InvariantCulture, $"<p>{E(ctx.SourceName)} → {E(ctx.TargetName)} · Shiftwall {E(ctx.AppVersion)} · {E(ctx.GeneratedAt.ToString("g", c))}</p>");
        sb.Append("<p><strong>").Append(E(T("RBR.Disclaimer"))).Append("</strong></p>");

        sb.Append("<h2>").Append(E(T("RBR.Findings"))).Append("</h2><ul>");
        foreach (var f in result.Findings) sb.Append("<li class=\"").Append(f.Severity.ToString().ToLowerInvariant()).Append("\">").Append(E(f.Format(c))).Append("</li>");
        sb.Append("</ul>");

        sb.Append("<h2>").Append(E(T("RBR.Counts"))).Append("</h2><table><tr><th>").Append(E(T("RBR.Object"))).Append("</th><th>")
          .Append(E(T("RBR.Source"))).Append("</th><th>").Append(E(T("RBR.Rebuilt"))).Append("</th><th>").Append(E(T("RBR.Converted")))
          .Append("</th><th>").Append(E(T("RBR.Manual"))).Append("</th></tr>");
        foreach (var n in result.Counts)
        {
            sb.Append(CultureInfo.InvariantCulture, $"<tr><td><code>{E(n.Path)}</code></td><td>{n.Source}</td><td>{n.Rebuilt}</td><td>{n.Converted}</td><td>{n.Manual}</td></tr>");
        }

        sb.Append("</table>");

        sb.Append("<h2>").Append(E(T("RBR.ManualWork"))).Append("</h2>");
        if (result.Manual.Count == 0) sb.Append("<p>—</p>");
        foreach (var m in result.Manual)
        {
            sb.Append("<details><summary><code>").Append(E(m.Path)).Append("</code> <strong>").Append(E(m.Object)).Append("</strong>: ").Append(E(m.Format(c))).Append("</summary>");
            if (m.Original.Length > 0) sb.Append("<pre>").Append(E(m.Original)).Append("</pre>");
            foreach (var s in m.Sources) sb.Append("<p><a href=\"").Append(E(s.Url)).Append("\">").Append(E(s.Title)).Append("</a></p>");
            sb.Append("</details>");
        }

        sb.Append("<h2>").Append(E(T("RBR.Profiles"))).Append("</h2>");
        if (result.Profiles.Count == 0) sb.Append("<p>—</p>");
        else
        {
            sb.Append("<table><tr><th>").Append(E(T("RBR.Policy"))).Append("</th><th>").Append(E(T("RBR.ProfilesOriginal"))).Append("</th><th>")
              .Append(E(T("RBR.ProfilesApplied"))).Append("</th><th>").Append(E(T("RBR.Status"))).Append("</th></tr>");
            foreach (var p in result.Profiles)
            {
                sb.Append("<tr><td>").Append(E(p.Policy)).Append("</td><td><code>").Append(E(string.Join("; ", p.Original))).Append("</code></td><td><code>")
                  .Append(E(string.Join("; ", p.Applied))).Append("</code></td><td>").Append(E(T(p.Disabled ? "RBR.Disabled" : "RBR.Enabled"))).Append("</td></tr>");
            }

            sb.Append("</table>");
        }

        sb.Append("<h2>").Append(E(T("RBR.Rules"))).Append("</h2><table><tr><th>ID</th><th>").Append(E(T("RBR.Object"))).Append("</th><th>").Append(E(T("RBR.Param"))).Append("</th></tr>");
        foreach (var a in result.Applied) sb.Append(CultureInfo.InvariantCulture, $"<tr><td>{E(a.RuleId)}</td><td><code>{E(a.Path)}</code> {E(a.Object)}</td><td>{E(a.Param)}</td></tr>");
        sb.Append("</table>");

        sb.Append("<h2>").Append(E(T("RBR.Checklist"))).Append("</h2><ol>");
        foreach (var k in Checklist) sb.Append("<li>").Append(E(T(k))).Append("</li>");
        sb.Append("</ol>");

        sb.Append("<h2>").Append(E(T("RBR.Script"))).Append("</h2><pre>").Append(E(result.FullScript(maskSecrets: true))).Append("</pre>");
        sb.Append("</main></body></html>");
        return sb.ToString();
    }
}
