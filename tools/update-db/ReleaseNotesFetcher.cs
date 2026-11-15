using System.Text.RegularExpressions;
using Shiftwall.Core.Data;

namespace Shiftwall.UpdateDb;

/// <summary>
/// Reads FortiOS release notes on docs.fortinet.com: build number and supported models from
/// "Introduction and supported models", release date from the first ("Initial release") row of "Change Log".
/// </summary>
internal sealed partial class ReleaseNotesFetcher(DocsClient client, DateOnly today)
{
    private const string Base = "https://docs.fortinet.com";

    [GeneratedRegex(@"\b(?:FG|FWF|FGR|FFW|FOS)-[A-Z0-9]+(?:-[A-Z0-9]+)*\b")]
    private static partial Regex ModelToken();

    [GeneratedRegex(@"(\d{4}-\d{2}-\d{2})\n([^\n]*)")]
    private static partial Regex ChangeLogRow();

    /// <summary>Lists the patch versions of a branch (e.g. 7.6) published on the FortiGate product page.</summary>
    public async Task<IReadOnlyList<string>> ListVersionsAsync(string branch, CancellationToken ct)
    {
        var page = await client.GetAsync($"{Base}/product/fortigate/{branch}", ct);
        var rx = new Regex(@"\b" + Regex.Escape(branch) + @"\.(\d{1,3})\b");
        return
        [
            .. rx.Matches(page.Html)
                .Select(m => m.Value)
                .Where(v => !v.EndsWith(".99", StringComparison.Ordinal)) // ".99" is the docs site's "latest" alias.
                .Distinct()
                .OrderBy(FortiOsVersion.Parse),
        ];
    }

    public async Task<FortiOsRelease> FetchAsync(string version, CancellationToken ct)
    {
        var intro = await client.GetAsync($"{Base}/document/fortigate/{version}/fortios-release-notes", ct);
        var introUrl = intro.FinalUrl.ToString();
        var text = HtmlText.ToText(intro.Html);
        var title = $"FortiOS {version} Release Notes";

        var build = Regex.Match(text, @"release information for FortiOS\s+" + Regex.Escape(version) + @"\s+build\s+(\d+)");
        var buildSourced = build.Success
            ? new Sourced<string>(build.Groups[1].Value, VerificationStatus.Verified,
                [new SourceRef(introUrl, title + " - Introduction and supported models", today, "\"This guide provides release information for FortiOS ... build ...\"")])
            : Sourced<string>.Unverified("Build number not found in the release notes introduction.");

        var models = ExtractSupportedModels(text);
        var modelsSourced = models.Count > 0
            ? new Sourced<IReadOnlyList<string>>(models, VerificationStatus.Verified,
                [new SourceRef(introUrl, title + " - Introduction and supported models", today, "Section \"Supported models\" (special-branch models excluded).")])
            : Sourced<IReadOnlyList<string>>.Unverified("Supported models section not found.");

        var dateSourced = Sourced<DateOnly?>.Unverified("Change Log page not found in the release notes.");
        var clLink = Regex.Match(intro.Html, @"href=""(/document/fortigate/" + Regex.Escape(version) + @"/fortios-release-notes/\d+/change-log)""");
        if (clLink.Success)
        {
            var clUrl = Base + clLink.Groups[1].Value;
            var cl = HtmlText.ToText((await client.GetAsync(clUrl, ct)).Html);
            var start = cl.IndexOf("Change Description", StringComparison.Ordinal);
            var first = start >= 0 ? ChangeLogRow().Match(cl, start) : Match.Empty;
            if (first.Success && first.Groups[2].Value.Contains("initial release", StringComparison.OrdinalIgnoreCase))
            {
                dateSourced = new Sourced<DateOnly?>(DateOnly.ParseExact(first.Groups[1].Value, "yyyy-MM-dd"), VerificationStatus.Verified,
                    [new SourceRef(clUrl, title + " - Change Log", today, "Date of the \"Initial release.\" row.")]);
            }
            else
            {
                dateSourced = Sourced<DateOnly?>.Unverified("Change Log has no \"Initial release\" row.");
            }
        }

        return new FortiOsRelease(version, buildSourced, dateSourced, modelsSourced);
    }

    internal static IReadOnlyList<string> ExtractSupportedModels(string text)
    {
        var start = text.IndexOf("supports the following models", StringComparison.Ordinal);
        if (start < 0) return [];
        var end = text.IndexOf("Special branch", start, StringComparison.Ordinal);
        if (end < 0) end = text.IndexOf('©', start);
        if (end < 0) end = text.Length;
        return [.. ModelToken().Matches(text[start..end]).Select(m => m.Value).Distinct(StringComparer.Ordinal)];
    }
}
