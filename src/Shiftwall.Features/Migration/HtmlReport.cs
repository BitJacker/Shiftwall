using System.Globalization;
using System.Text;

namespace Shiftwall.Features.Migration;

/// <summary>Context shown in the report header. Names are file names only (no full paths).</summary>
public sealed record ReportContext(
    string SourceName,
    string TargetName,
    string AppVersion,
    DateTimeOffset GeneratedAt,
    CultureInfo Culture,
    bool IncludeDiff = true);

/// <summary>
/// Self-contained HTML report (inline CSS, no scripts, no external resources), readable in light and dark mode
/// and printable to PDF.
/// </summary>
public static class HtmlReport
{
    public static string Build(MigrationRequest request, MigrationResult result, ReportContext ctx)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(ctx);
        var c = ctx.Culture;
        string T(string key) => MigrationText.Get(key, c);
        static string E(string? s) => Escape(s);

        var sb = new StringBuilder();
        sb.Append("<!doctype html><html lang=\"").Append(c.TwoLetterISOLanguageName).Append("\"><head><meta charset=\"utf-8\">");
        sb.Append("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">");
        sb.Append("<title>").Append(E(T("Report.Title"))).Append(" – ").Append(E(ctx.SourceName)).Append("</title>");
        sb.Append("<style>").Append(Css).Append("</style></head><body><main>");

        sb.Append("<h1>").Append(E(T("Report.Title"))).Append("</h1>");
        sb.Append("<p class=\"muted\">").Append(E(string.Format(c, T("Report.Generated"), ctx.AppVersion, ctx.GeneratedAt.ToString("g", c)))).Append("</p>");

        sb.Append("<table class=\"kv\"><tr><th></th><th>").Append(E(T("Report.Source"))).Append("</th><th>").Append(E(T("Report.Target"))).Append("</th></tr>");
        sb.Append("<tr><td>File</td><td>").Append(E(ctx.SourceName)).Append("</td><td>").Append(E(ctx.TargetName)).Append("</td></tr>");
        sb.Append("<tr><td>").Append(E(T("Report.Model"))).Append("</td><td>").Append(E(request.Source.PlatformCode ?? "?"))
          .Append("</td><td>").Append(E(request.Target.ModelId ?? request.Target.PlatformCode ?? "?")).Append("</td></tr>");
        sb.Append("<tr><td>").Append(E(T("Report.Firmware"))).Append("</td><td>").Append(E(request.Source.Version ?? "?"))
          .Append("</td><td>").Append(E(request.Target.Version ?? "?")).Append("</td></tr></table>");

        Section(sb, T("Report.PreChecks"), result.Analysis.PreChecks, c);

        sb.Append("<h2>").Append(E(T("Report.Mapping"))).Append("</h2><table class=\"map\"><tr><th>").Append(E(T("Report.Source")))
          .Append("</th><th></th><th>").Append(E(T("Report.Target"))).Append("</th><th>").Append(E(T("Report.Uses"))).Append("</th></tr>");
        foreach (var iface in request.Source.PhysicalInterfaces)
        {
            var to = request.Mapping[iface.Name];
            var uses = result.Analysis.UsageCount.GetValueOrDefault(iface.Name);
            sb.Append("<tr><td><code>").Append(E(iface.Name)).Append("</code></td><td>→</td><td>")
              .Append(to is null ? $"<em>{E(T("Report.NotMapped"))}</em>" : $"<code>{E(to)}</code>")
              .Append("</td><td class=\"muted num\">").Append(uses.ToString(c)).Append("</td></tr>");
        }

        sb.Append("</table>");
        Section(sb, null, result.Analysis.MappingFindings, c);

        if (result.IsBlocked)
        {
            sb.Append("<p class=\"error box\">").Append(E(T("Report.Blocked"))).Append("</p>");
        }
        else
        {
            Section(sb, T("Report.Changes"), result.Changes, c);
            Section(sb, T("Report.Validation"), result.Validation, c);
        }

        sb.Append("<h2>").Append(E(T("Report.Checklist"))).Append("</h2><ul class=\"check\">");
        foreach (var item in result.Checklist)
        {
            sb.Append("<li>☐ ").Append(E(item.Format(c))).Append("</li>");
        }

        sb.Append("</ul>");

        if (ctx.IncludeDiff && result.OutputText is not null)
        {
            sb.Append("<h2>").Append(E(T("Report.Diff"))).Append("</h2>");
            sb.Append("<p class=\"warning box\">").Append(E(T("Report.ContainsConfig"))).Append("</p><pre class=\"diff\">");
            foreach (var line in ConfigDiff.Unified(result.SourceText, result.OutputText))
            {
                var (cls, mark) = line.Kind switch
                {
                    DiffLineKind.Added => ("add", "+"),
                    DiffLineKind.Removed => ("del", "-"),
                    DiffLineKind.Gap => ("gap", " "),
                    _ => ("ctx", " "),
                };
                sb.Append("<span class=\"").Append(cls).Append("\">")
                  .Append(E((line.OldLine?.ToString(CultureInfo.InvariantCulture) ?? "").PadLeft(6)))
                  .Append(' ').Append(E((line.NewLine?.ToString(CultureInfo.InvariantCulture) ?? "").PadLeft(6)))
                  .Append(' ').Append(mark).Append(' ').Append(E(line.Text)).Append("</span>");
            }

            sb.Append("</pre>");
        }

        sb.Append("<footer><p>").Append(E(T("Report.Disclaimer"))).Append("</p></footer></main></body></html>");
        return sb.ToString();
    }

    private static void Section(StringBuilder sb, string? title, IEnumerable<MigrationFinding> findings, CultureInfo c)
    {
        var list = findings.ToList();
        if (title is not null) sb.Append("<h2>").Append(Escape(title)).Append("</h2>");
        if (list.Count == 0) return;
        sb.Append("<ul class=\"findings\">");
        foreach (var f in list)
        {
            var cls = f.Severity switch { FindingSeverity.Error => "error", FindingSeverity.Warning => "warning", _ => "info" };
            var icon = f.Severity switch { FindingSeverity.Error => "✖", FindingSeverity.Warning => "⚠", _ => "ℹ" };
            sb.Append("<li class=\"").Append(cls).Append("\"><span class=\"icon\">").Append(icon).Append("</span> <span class=\"code\">")
              .Append(f.Code).Append("</span> ").Append(Escape(f.Format(c))).Append("</li>");
        }

        sb.Append("</ul>");
    }

    /// <summary>Escapes the five HTML-significant characters only (the document is UTF-8).</summary>
    internal static string Escape(string? s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        var sb = new StringBuilder(s.Length + 16);
        foreach (var ch in s)
        {
            sb.Append(ch switch
            {
                '&' => "&amp;",
                '<' => "&lt;",
                '>' => "&gt;",
                '"' => "&quot;",
                '\'' => "&#39;",
                _ => ch.ToString(),
            });
        }

        return sb.ToString();
    }

    private const string Css = """
        :root{--bg:#fff;--fg:#14212b;--muted:#5b6b75;--line:#d7e0e4;--accent:#0f766e;--err:#b42318;--warn:#a15c07;--info:#1f6474;
        --add-bg:#e6f6ef;--add-fg:#0b5d3b;--del-bg:#fdecea;--del-fg:#8a1c12;--code-bg:#f3f6f7}
        @media (prefers-color-scheme:dark){:root{--bg:#0d1117;--fg:#e6edf3;--muted:#8b98a5;--line:#2a3640;--accent:#5eead4;--err:#ff7b72;
        --warn:#e3b341;--info:#79c0ff;--add-bg:#0f2d22;--add-fg:#7ee2b8;--del-bg:#3a1614;--del-fg:#ffa198;--code-bg:#161b22}}
        *{box-sizing:border-box}body{margin:0;background:var(--bg);color:var(--fg);font:15px/1.5 "Segoe UI",system-ui,sans-serif}
        main{max-width:1100px;margin:0 auto;padding:24px 16px}h1{color:var(--accent);margin:0 0 4px}h2{border-bottom:1px solid var(--line);padding-bottom:4px;margin-top:32px}
        .muted{color:var(--muted)}code,pre{font-family:"Cascadia Mono",Consolas,monospace;font-size:13px}
        table{border-collapse:collapse}td,th{padding:4px 10px;border-bottom:1px solid var(--line);text-align:left;vertical-align:top}
        ul.findings,ul.check{list-style:none;padding:0}ul.findings li,ul.check li{padding:6px 8px;border-left:3px solid var(--line);margin:4px 0}
        li.error{border-color:var(--err)}li.warning{border-color:var(--warn)}li.info{border-color:var(--info)}
        li.error .icon{color:var(--err)}li.warning .icon{color:var(--warn)}li.info .icon{color:var(--info)}.code{color:var(--muted);font-size:12px}
        .box{padding:10px 12px;border-radius:6px;border:1px solid var(--line)}p.error{color:var(--err)}p.warning{color:var(--warn)}
        pre.diff{background:var(--code-bg);border:1px solid var(--line);border-radius:6px;padding:8px 0;overflow-x:auto;white-space:pre}
        pre.diff span{display:block;padding:0 10px}.add{background:var(--add-bg);color:var(--add-fg)}.del{background:var(--del-bg);color:var(--del-fg)}
        .gap{color:var(--muted)}td.num{text-align:right}footer{margin-top:40px;color:var(--muted);font-size:13px;border-top:1px solid var(--line);padding-top:12px}
        @media print{pre.diff{white-space:pre-wrap}h2{break-after:avoid}}
        """;
}
