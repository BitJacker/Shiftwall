using System.Text;
using Shiftwall.Core.Config;

namespace Shiftwall.Features.Toolbox;

/// <summary>An object that nothing in its VDOM refers to.</summary>
public sealed record UnusedObject(string? Vdom, string Table, string Name, bool OnlyUsedByUnusedGroups);

/// <summary>
/// Finds addresses, groups, services and schedules that no other statement of the same VDOM references, and builds
/// the CLI script that deletes them. The script is only generated: the user reviews and applies it.
/// </summary>
/// <remarks>
/// A name counts as referenced when it appears as a value of any <c>set</c>/<c>append</c> statement in the VDOM
/// (comments included), which errs on the side of keeping objects. Groups are deleted before their members.
/// CLI <c>delete &lt;table&gt;</c>: FortiOS Handbook "Sub-commands",
/// https://docs.fortinet.com/document/fortigate/6.0.0/handbook/753045/sub-commands.
/// </remarks>
public static class UnusedObjects
{
    /// <summary>Object tables checked, groups first (deletion order).</summary>
    public static readonly IReadOnlyList<string> Tables =
    [
        "firewall addrgrp", "firewall addrgrp6", "firewall service group", "firewall schedule group",
        "firewall address", "firewall address6", "firewall service custom",
        "firewall schedule recurring", "firewall schedule onetime",
    ];

    private static readonly HashSet<string> GroupTables = ["firewall addrgrp", "firewall addrgrp6", "firewall service group", "firewall schedule group"];

    /// <summary>
    /// Finds unused objects. Names present in <paramref name="exclude"/> (e.g. the objects of a factory-default
    /// backup of the same model) are never reported.
    /// </summary>
    public static IReadOnlyList<UnusedObject> Find(ConfigDocument doc, ISet<string>? exclude = null)
    {
        var result = new List<UnusedObject>();
        foreach (var (vdom, scope) in ConfigTables.VdomScopes(doc))
        {
            var references = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var v in ConfigTables.ValueTokens(scope)) references[v] = references.GetValueOrDefault(v) + 1;

            // Members referenced only from groups that are themselves unused become unused once the groups go.
            var unused = new HashSet<(string Table, string Name)>();
            var removedRefs = new Dictionary<string, int>(StringComparer.Ordinal);
            bool changed;
            do
            {
                changed = false;
                foreach (var table in Tables)
                {
                    foreach (var e in ConfigTables.Entries(scope, table))
                    {
                        if (unused.Contains((table, e.Key)) || exclude?.Contains(e.Key) == true) continue;
                        var refs = references.GetValueOrDefault(e.Key) - removedRefs.GetValueOrDefault(e.Key);
                        if (refs > 0) continue;
                        unused.Add((table, e.Key));
                        changed = true;
                        if (GroupTables.Contains(table))
                        {
                            foreach (var m in ConfigTables.Values(e, "member") ?? []) removedRefs[m] = removedRefs.GetValueOrDefault(m) + 1;
                        }
                    }
                }
            }
            while (changed);

            foreach (var table in Tables)
            {
                foreach (var e in ConfigTables.Entries(scope, table))
                {
                    if (!unused.Contains((table, e.Key))) continue;
                    var direct = references.GetValueOrDefault(e.Key) == 0;
                    result.Add(new UnusedObject(vdom, table, e.Key, !direct));
                }
            }
        }

        return result;
    }

    /// <summary>All object names defined in <paramref name="doc"/> (used to exclude factory-default objects).</summary>
    public static HashSet<string> ObjectNames(ConfigDocument doc)
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (_, scope) in ConfigTables.VdomScopes(doc))
        {
            foreach (var t in Tables)
            {
                foreach (var e in ConfigTables.Entries(scope, t)) set.Add(e.Key);
            }
        }

        return set;
    }

    /// <summary>CLI script deleting <paramref name="objects"/>, groups first, per VDOM.</summary>
    public static string Script(IEnumerable<UnusedObject> objects, bool multiVdom, string header)
    {
        var sb = new StringBuilder();
        foreach (var line in header.Split('\n')) sb.Append("# ").Append(line.TrimEnd('\r')).Append('\n');
        foreach (var vdomGroup in objects.GroupBy(o => o.Vdom))
        {
            var indent = "";
            if (multiVdom && vdomGroup.Key is not null)
            {
                sb.Append("config vdom\n    edit ").Append(ConfigToken.Render(vdomGroup.Key)).Append('\n');
                indent = "        ";
            }

            foreach (var table in Tables)
            {
                var names = vdomGroup.Where(o => o.Table == table).Select(o => o.Name).ToList();
                if (names.Count == 0) continue;
                sb.Append(indent).Append("config ").Append(table).Append('\n');
                foreach (var n in names) sb.Append(indent).Append("    delete ").Append(ConfigToken.Render(n, '"')).Append('\n');
                sb.Append(indent).Append("end\n");
            }

            if (indent.Length > 0) sb.Append("    next\nend\n");
        }

        return sb.ToString();
    }
}
