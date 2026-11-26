using System.Globalization;
using System.Text;

namespace Shiftwall.Features.Rebuild;

/// <summary>A file of the generated output (GUI script part or terminal block).</summary>
public sealed record ScriptFile(string FileName, string Text, int Number, int Total, string SectionKey);

/// <summary>
/// Splits the rebuilt configuration into the two deliverables:
/// <list type="bullet">
/// <item>GUI: numbered script files for "Configuration &gt; Scripts &gt; Run Script" (Administration Guide 8.0.2,
/// "Configuration scripts"); comment lines start with #. No size limit is documented, so parts are kept small.</item>
/// <item>Terminal: small blocks to paste one at a time in the CLI Console or an SSH client, without comments.</item>
/// </list>
/// Every block is complete (config ... end). Parts with only edit/set give the same result when run twice; the parts
/// that take ports out of a switch or delete factory DHCP servers use delete and must be run once, in order.
/// </summary>
public static class ScriptPackager
{
    public const int DefaultGuiMaxLines = 800;
    public const int DefaultTerminalMaxLines = 40;

    public static IReadOnlyList<ScriptFile> ForGui(IReadOnlyList<ScriptSection> sections, string title, int maxLines = DefaultGuiMaxLines, bool maskSecrets = false)
    {
        var chunks = Chunk(sections, maxLines, maskSecrets);
        var files = new List<ScriptFile>();
        for (var i = 0; i < chunks.Count; i++)
        {
            var (key, text) = chunks[i];
            var sb = new StringBuilder();
            sb.Append(CultureInfo.InvariantCulture, $"# Shiftwall - {Clean(title)} - part {i + 1} of {chunks.Count} ({key})\n");
            sb.Append(CultureInfo.InvariantCulture, $"# Run the parts in order: admin menu > Configuration > Scripts > Run Script.\n");
            sb.Append(text);
            files.Add(new ScriptFile($"gui-{i + 1:000}-{key}.txt", sb.ToString(), i + 1, chunks.Count, key));
        }

        return files;
    }

    public static IReadOnlyList<ScriptFile> ForTerminal(IReadOnlyList<ScriptSection> sections, int maxLines = DefaultTerminalMaxLines, bool maskSecrets = false)
    {
        var chunks = Chunk(sections, maxLines, maskSecrets);
        return [.. chunks.Select((c, i) => new ScriptFile($"cli-{i + 1:000}-{c.Key}.txt", c.Text, i + 1, chunks.Count, c.Key))];
    }

    private static string Clean(string s) => new([.. s.Where(c => c >= 32 && c < 127 && c != '?')]);

    /// <summary>Packs blocks into chunks of at most <paramref name="maxLines"/> lines. A table that does not fit is
    /// split between entries, repeating its config line. Sections are never mixed in one chunk.</summary>
    private static List<(string Key, string Text)> Chunk(IReadOnlyList<ScriptSection> sections, int maxLines, bool mask)
    {
        var result = new List<(string, string)>();
        foreach (var section in sections.Where(s => !s.IsEmpty).OrderBy(s => s.Order))
        {
            var current = new StringBuilder();
            var lines = 0;
            void Flush()
            {
                if (current.Length == 0) return;
                result.Add((section.Key, current.ToString()));
                current.Clear();
                lines = 0;
            }

            foreach (var block in section.Blocks)
            {
                foreach (var piece in Pieces(block, maxLines, mask))
                {
                    var n = piece.Count(c => c == '\n');
                    if (lines > 0 && lines + n > maxLines) Flush();
                    current.Append(piece);
                    lines += n;
                }
            }

            Flush();
        }

        return result;
    }

    private static IEnumerable<string> Pieces(CliBlock block, int maxLines, bool mask)
    {
        var whole = CliRenderer.Render(block, mask);
        if (whole.Count(c => c == '\n') <= maxLines || !block.IsTable || block.Entries.Count <= 1)
        {
            yield return whole;
            yield break;
        }

        var part = new CliBlock(block.Path, true);
        part.Deletes.AddRange(block.Deletes);
        var lines = 2 + block.Deletes.Count;
        foreach (var e in block.Entries)
        {
            var single = new CliBlock(block.Path, true);
            single.Entries.Add(e);
            var n = CliRenderer.Render(single, mask).Count(c => c == '\n') - 2;
            if (part.Entries.Count > 0 && lines + n > maxLines)
            {
                yield return CliRenderer.Render(part, mask);
                part = new CliBlock(block.Path, true);
                lines = 2;
            }

            part.Entries.Add(e);
            lines += n;
        }

        if (part.Entries.Count > 0 || part.Deletes.Count > 0) yield return CliRenderer.Render(part, mask);
    }
}
