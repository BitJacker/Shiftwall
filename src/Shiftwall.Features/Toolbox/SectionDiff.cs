using Shiftwall.Core.Config;

namespace Shiftwall.Features.Toolbox;

public enum SectionChangeKind
{
    Added,
    Removed,
    Changed,
}

/// <summary>One changed field of an entry. Values are display text (encrypted values masked).</summary>
public sealed record FieldChange(string Field, string? Before, string? After);

/// <summary>
/// A difference between two configurations at entry level: a table entry (<c>edit</c>) or a settings block that was
/// added, removed or changed.
/// </summary>
public sealed record SectionChange(string? Scope, string Section, string? Entry, SectionChangeKind Kind, IReadOnlyList<FieldChange> Fields)
{
    /// <summary>"vdom › section › entry" label.</summary>
    public string Location => string.Join(" › ", new[] { Scope, Section, Entry }.Where(s => !string.IsNullOrEmpty(s)));
}

/// <summary>Summary of one section (e.g. "firewall policy"): how many entries were added, removed and changed.</summary>
public sealed record SectionSummary(string? Scope, string Section, int Added, int Removed, int Changed);

public sealed record SectionDiffResult(IReadOnlyList<SectionChange> Changes, IReadOnlyList<SectionSummary> Sections)
{
    public bool Identical => Changes.Count == 0;
}

/// <summary>
/// Compares two configurations by section and entry instead of line by line: moving an entry or re-indenting
/// does not count as a change, a field changed inside a policy is reported as that field.
/// </summary>
public static class SectionDiff
{
    private const string SettingsEntry = "";

    public static SectionDiffResult Compare(ConfigDocument before, ConfigDocument after)
    {
        var a = Flatten(before);
        var b = Flatten(after);
        var changes = new List<SectionChange>();

        foreach (var key in a.Keys.Union(b.Keys).OrderBy(k => k.Scope ?? "", StringComparer.Ordinal).ThenBy(k => k.Section, StringComparer.Ordinal))
        {
            a.TryGetValue(key, out var ea);
            b.TryGetValue(key, out var eb);
            ea ??= new Dictionary<string, Dictionary<string, IReadOnlyList<string>>>();
            eb ??= new Dictionary<string, Dictionary<string, IReadOnlyList<string>>>();

            // Entries in "before" order, then entries only in "after", in their order.
            foreach (var entry in ea.Keys.Concat(eb.Keys.Where(k => !ea.ContainsKey(k))))
            {
                var inA = ea.TryGetValue(entry, out var fa);
                var inB = eb.TryGetValue(entry, out var fb);
                var label = entry == SettingsEntry ? null : entry;
                if (inA && !inB)
                {
                    changes.Add(new SectionChange(key.Scope, key.Section, label, SectionChangeKind.Removed, Fields(fa!, null)));
                }
                else if (!inA && inB)
                {
                    changes.Add(new SectionChange(key.Scope, key.Section, label, SectionChangeKind.Added, Fields(null, fb!)));
                }
                else
                {
                    var diff = Fields(fa!, fb!);
                    if (diff.Count > 0) changes.Add(new SectionChange(key.Scope, key.Section, label, SectionChangeKind.Changed, diff));
                }
            }
        }

        var sections = changes
            .GroupBy(c => (c.Scope, c.Section))
            .Select(g => new SectionSummary(g.Key.Scope, g.Key.Section,
                g.Count(c => c.Kind == SectionChangeKind.Added),
                g.Count(c => c.Kind == SectionChangeKind.Removed),
                g.Count(c => c.Kind == SectionChangeKind.Changed)))
            .ToList();
        return new SectionDiffResult(changes, sections);
    }

    /// <summary>
    /// Field differences between two flattened entries. With one side null every field of the other side is listed.
    /// Fields keep the order of the "before" entry, then new fields in "after" order.
    /// </summary>
    private static List<FieldChange> Fields(Dictionary<string, IReadOnlyList<string>>? a, Dictionary<string, IReadOnlyList<string>>? b)
    {
        var list = new List<FieldChange>();
        var keys = (a?.Keys ?? Enumerable.Empty<string>()).Concat((b?.Keys ?? Enumerable.Empty<string>()).Where(k => a is null || !a.ContainsKey(k)));
        foreach (var k in keys)
        {
            IReadOnlyList<string>? va = null;
            IReadOnlyList<string>? vb = null;
            a?.TryGetValue(k, out va);
            b?.TryGetValue(k, out vb);
            if (va is not null && vb is not null && va.SequenceEqual(vb, StringComparer.Ordinal)) continue;
            list.Add(new FieldChange(k, va is null ? null : ConfigTables.Display(va), vb is null ? null : ConfigTables.Display(vb)));
        }

        return list;
    }

    private readonly record struct SectionKey(string? Scope, string Section);

    /// <summary>
    /// scope + section path → entry key → field path → values. Nested blocks inside an entry become field paths
    /// such as "config members › 1 › interface". Settings blocks (no <c>edit</c>) use the empty entry key.
    /// </summary>
    private static Dictionary<SectionKey, Dictionary<string, Dictionary<string, IReadOnlyList<string>>>> Flatten(ConfigDocument doc)
    {
        var result = new Dictionary<SectionKey, Dictionary<string, Dictionary<string, IReadOnlyList<string>>>>();
        foreach (var (scope, container) in ConfigTables.Scopes(doc))
        {
            foreach (var block in container.Blocks)
            {
                var key = new SectionKey(scope, block.Path);
                if (!result.TryGetValue(key, out var entries))
                {
                    entries = new Dictionary<string, Dictionary<string, IReadOnlyList<string>>>(StringComparer.Ordinal);
                    result[key] = entries;
                }

                AddBlock(block, entries);
            }
        }

        return result;
    }

    private static void AddBlock(ConfigBlock block, Dictionary<string, Dictionary<string, IReadOnlyList<string>>> entries)
    {
        if (block.Fields.Any() || block.Blocks.Any() || !block.Entries.Any())
        {
            var settings = Get(entries, SettingsEntry);
            AddFields(block, "", settings);
        }

        foreach (var entry in block.Entries)
        {
            var fields = Get(entries, entry.Key);
            AddFields(entry, "", fields);
        }
    }

    private static Dictionary<string, IReadOnlyList<string>> Get(Dictionary<string, Dictionary<string, IReadOnlyList<string>>> entries, string key)
    {
        if (!entries.TryGetValue(key, out var fields))
        {
            fields = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
            entries[key] = fields;
        }

        return fields;
    }

    private static void AddFields(ConfigContainer node, string prefix, Dictionary<string, IReadOnlyList<string>> fields)
    {
        foreach (var child in node.Children)
        {
            switch (child)
            {
                case FieldNode { Verb: "set" } f:
                    fields[prefix + f.Name] = f.Values;
                    break;
                case FieldNode { Verb: "unset" } f:
                    fields.Remove(prefix + f.Name);
                    break;
                case ConfigBlock nested:
                    var nestedPrefix = $"{prefix}config {nested.Path} › ";
                    AddFields(nested, nestedPrefix, fields);
                    foreach (var e in nested.Entries) AddFields(e, $"{nestedPrefix}{e.Key} › ", fields);
                    if (!nested.Children.Any(c => c is FieldNode or EditEntry or ConfigBlock)) fields[nestedPrefix.TrimEnd(' ', '›')] = [];
                    break;
                case EditEntry:
                    // Entries of the block itself are handled by AddBlock; entries of nested blocks above.
                    break;
            }
        }
    }
}
