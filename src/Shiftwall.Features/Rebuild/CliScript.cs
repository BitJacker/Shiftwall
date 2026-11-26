using System.Text;
using Shiftwall.Core.Config;

namespace Shiftwall.Features.Rebuild;

/// <summary>One <c>set</c> (or <c>unset</c>) line. Values are unescaped; rendering adds quotes when needed.</summary>
public sealed record CliSet(string Param, IReadOnlyList<string> Values, bool QuoteValues, bool Unset = false, bool Secret = false);

/// <summary>An <c>edit</c> entry (or the body of a non-table block when <see cref="Key"/> is null).</summary>
public sealed class CliEntry(string? key, bool quoteKey = true)
{
    public string? Key { get; } = key;

    public bool QuoteKey { get; } = quoteKey;

    public List<CliSet> Sets { get; } = [];

    public List<CliBlock> Subs { get; } = [];

    /// <summary>Keys deleted inside this entry's sub-tables are expressed as blocks with <see cref="CliBlock.Deletes"/>.</summary>
    public CliEntry Set(string param, IReadOnlyList<string> values, bool quote, bool secret = false)
    {
        Sets.Add(new CliSet(param, values, quote, Secret: secret));
        return this;
    }
}

/// <summary>A <c>config &lt;path&gt;</c> ... <c>end</c> block.</summary>
public sealed class CliBlock(string path, bool isTable)
{
    public string Path { get; } = path;

    public bool IsTable { get; } = isTable;

    /// <summary>Entries deleted before the edits (rendered as <c>delete &lt;key&gt;</c>).</summary>
    public List<string> Deletes { get; } = [];

    public List<CliEntry> Entries { get; } = [];
}

/// <summary>A group of blocks in the dependency order of the script (interfaces, zones, addresses...).</summary>
public sealed record ScriptSection(int Order, string Key, List<CliBlock> Blocks)
{
    public bool IsEmpty => Blocks.All(b => b.Entries.Count == 0 && b.Deletes.Count == 0);
}

/// <summary>Renders blocks in the FortiOS <c>show</c> layout: four spaces per level, LF line endings.</summary>
public static class CliRenderer
{
    public const string Indent = "    ";

    public static string Render(CliBlock block, bool maskSecrets = false)
    {
        var sb = new StringBuilder();
        Render(sb, block, 0, maskSecrets);
        return sb.ToString();
    }

    public static string Render(IEnumerable<CliBlock> blocks, bool maskSecrets = false)
    {
        var sb = new StringBuilder();
        foreach (var b in blocks) Render(sb, b, 0, maskSecrets);
        return sb.ToString();
    }

    private static void Render(StringBuilder sb, CliBlock block, int level, bool mask)
    {
        if (block.Entries.Count == 0 && block.Deletes.Count == 0) return;
        Line(sb, level, "config " + block.Path);
        foreach (var d in block.Deletes) Line(sb, level + 1, "delete " + Token(d, !d.All(char.IsAsciiDigit)));
        foreach (var e in block.Entries)
        {
            var inner = level + 1;
            if (e.Key is not null)
            {
                // Numeric keys (policy, route, DHCP ids) are written without quotes, as FortiOS shows them.
                Line(sb, level + 1, "edit " + Token(e.Key, e.QuoteKey && !e.Key.All(char.IsAsciiDigit)));
                inner = level + 2;
            }

            foreach (var s in e.Sets)
            {
                if (s.Unset)
                {
                    Line(sb, inner, "unset " + s.Param);
                    continue;
                }

                var values = s.Secret && mask ? ["********"] : s.Values.Select(v => Token(v, s.QuoteValues));
                Line(sb, inner, "set " + s.Param + " " + string.Join(' ', values));
            }

            foreach (var sub in e.Subs) Render(sb, sub, inner, mask);
            if (e.Key is not null) Line(sb, level + 1, "next");
        }

        Line(sb, level, "end");
    }

    /// <summary>Renders a value. Quoted values are escaped as documented in "CLI basics" (Special characters).</summary>
    public static string Token(string value, bool quote)
    {
        if (quote || value.Length == 0 || value.Any(c => CliPassword.Forbidden.Contains(c, StringComparison.Ordinal))) return ConfigToken.Render(value);
        return value;
    }

    private static void Line(StringBuilder sb, int level, string text)
    {
        for (var i = 0; i < level; i++) sb.Append(Indent);
        sb.Append(text).Append('\n');
    }
}
