using DiffPlex;
using DiffPlex.DiffBuilder;
using DiffPlex.DiffBuilder.Model;

namespace Shiftwall.Features.Migration;

public enum DiffLineKind
{
    Unchanged,
    Removed,
    Added,
    Gap,
}

/// <summary>One line of a unified diff. <see cref="OldLine"/>/<see cref="NewLine"/> are 1-based or null.</summary>
public sealed record DiffLine(DiffLineKind Kind, int? OldLine, int? NewLine, string Text);

/// <summary>A side-by-side diff row. Texts are null where the other side has no line.</summary>
public sealed record SideBySideRow(int? OldLine, string? OldText, DiffLineKind OldKind, int? NewLine, string? NewText, DiffLineKind NewKind)
{
    public static SideBySideRow Gap { get; } = new(null, "…", DiffLineKind.Gap, null, "…", DiffLineKind.Gap);

    public bool IsChanged => OldKind is DiffLineKind.Removed || NewKind is DiffLineKind.Added || (OldText is null) != (NewText is null);

    public bool IsGap => ReferenceEquals(this, Gap);
}

/// <summary>Line diff between two configurations (DiffPlex, Apache-2.0).</summary>
public static class ConfigDiff
{
    /// <summary>All lines of the new file aligned with the old one (for side-by-side views).</summary>
    public static SideBySideDiffModel SideBySide(string before, string after) =>
        new SideBySideDiffBuilder(new Differ()).BuildDiffModel(before, after, ignoreWhitespace: false);

    /// <summary>
    /// Side-by-side rows (old | new) limited to changed regions plus <paramref name="context"/> lines;
    /// collapsed regions become a single <see cref="DiffLineKind.Gap"/> row. Use a negative context for all rows.
    /// </summary>
    public static IReadOnlyList<SideBySideRow> SideBySideRows(string before, string after, int context = 3)
    {
        var model = SideBySide(before, after);
        var rows = new List<SideBySideRow>(model.NewText.Lines.Count);
        for (var i = 0; i < model.NewText.Lines.Count; i++)
        {
            var o = model.OldText.Lines[i];
            var n = model.NewText.Lines[i];
            rows.Add(new SideBySideRow(o.Position, o.Type == ChangeType.Imaginary ? null : o.Text, Kind(o.Type, removed: true),
                                       n.Position, n.Type == ChangeType.Imaginary ? null : n.Text, Kind(n.Type, removed: false)));
        }

        if (context < 0) return rows;
        var keep = new bool[rows.Count];
        for (var i = 0; i < rows.Count; i++)
        {
            if (!rows[i].IsChanged) continue;
            for (var j = Math.Max(0, i - context); j <= Math.Min(rows.Count - 1, i + context); j++) keep[j] = true;
        }

        var result = new List<SideBySideRow>();
        var gap = false;
        for (var i = 0; i < rows.Count; i++)
        {
            if (keep[i]) { result.Add(rows[i]); gap = false; }
            else if (!gap) { result.Add(SideBySideRow.Gap); gap = true; }
        }

        return result;

        static DiffLineKind Kind(ChangeType t, bool removed) => t switch
        {
            ChangeType.Deleted => DiffLineKind.Removed,
            ChangeType.Inserted => DiffLineKind.Added,
            ChangeType.Modified => removed ? DiffLineKind.Removed : DiffLineKind.Added,
            ChangeType.Imaginary => DiffLineKind.Gap,
            _ => DiffLineKind.Unchanged,
        };
    }

    /// <summary>Changed lines with <paramref name="context"/> unchanged lines around them; gaps are collapsed.</summary>
    public static IReadOnlyList<DiffLine> Unified(string before, string after, int context = 3)
    {
        var model = InlineDiffBuilder.Diff(before, after, ignoreWhiteSpace: false);
        var all = new List<DiffLine>(model.Lines.Count);
        int oldNo = 0, newNo = 0;
        foreach (var line in model.Lines)
        {
            switch (line.Type)
            {
                case ChangeType.Deleted:
                    all.Add(new DiffLine(DiffLineKind.Removed, ++oldNo, null, line.Text));
                    break;
                case ChangeType.Inserted:
                    all.Add(new DiffLine(DiffLineKind.Added, null, ++newNo, line.Text));
                    break;
                default:
                    all.Add(new DiffLine(DiffLineKind.Unchanged, ++oldNo, ++newNo, line.Text));
                    break;
            }
        }

        var keep = new bool[all.Count];
        for (var i = 0; i < all.Count; i++)
        {
            if (all[i].Kind == DiffLineKind.Unchanged) continue;
            for (var j = Math.Max(0, i - context); j <= Math.Min(all.Count - 1, i + context); j++) keep[j] = true;
        }

        var result = new List<DiffLine>();
        var gap = false;
        for (var i = 0; i < all.Count; i++)
        {
            if (keep[i])
            {
                result.Add(all[i]);
                gap = false;
            }
            else if (!gap)
            {
                result.Add(new DiffLine(DiffLineKind.Gap, null, null, "…"));
                gap = true;
            }
        }

        return result;
    }
}
