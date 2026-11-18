using System.Net;
using System.Text.RegularExpressions;

namespace Shiftwall.UpdateDb;

/// <summary>Minimal HTML-to-text conversion for docs.fortinet.com pages (no external dependency).</summary>
internal static partial class HtmlText
{
    [GeneratedRegex(@"<(script|style|nav|header|footer)[\s\S]*?</\1>", RegexOptions.IgnoreCase)]
    private static partial Regex Chrome();

    [GeneratedRegex(@"<br\s*/?>|</p>|</li>|</tr>|</h\d>|</td>|</pre>", RegexOptions.IgnoreCase)]
    private static partial Regex Breaks();

    [GeneratedRegex(@"<[^>]+>")]
    private static partial Regex Tags();

    [GeneratedRegex(@"[ \t]+")]
    private static partial Regex Spaces();

    [GeneratedRegex(@"\n\s*\n+")]
    private static partial Regex BlankLines();

    [GeneratedRegex(@"<title>([^|<]*)", RegexOptions.IgnoreCase)]
    private static partial Regex TitleRegex();

    public static string ToText(string html)
    {
        var t = Chrome().Replace(html, "");
        t = Breaks().Replace(t, "\n");
        t = Tags().Replace(t, "");
        t = WebUtility.HtmlDecode(t);
        t = Spaces().Replace(t, " ");
        return BlankLines().Replace(t, "\n");
    }

    public static string Title(string html)
    {
        var m = TitleRegex().Match(html);
        return m.Success ? WebUtility.HtmlDecode(m.Groups[1].Value).Trim() : "";
    }
}
