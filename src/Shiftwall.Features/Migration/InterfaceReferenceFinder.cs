using Shiftwall.Core.Config;

namespace Shiftwall.Features.Migration;

public enum ReferenceKind
{
    /// <summary><c>edit "port1"</c> in <c>config system interface</c>.</summary>
    Definition,

    /// <summary>An <c>edit</c> key in a block keyed by interface name (e.g. virtual-switch ports).</summary>
    Key,

    /// <summary>A value of a documented interface field (srcintf, interface, device...).</summary>
    Field,

    /// <summary>The same text found in an undocumented field: reported, never renamed.</summary>
    Possible,
}

/// <summary>One place where an interface name appears.</summary>
/// <param name="Path">Readable location, e.g. <c>firewall policy [3] srcintf</c>.</param>
public sealed record InterfaceReference(string Interface, ReferenceKind Kind, int Line, string Path, string? Vdom);

/// <summary>Finds where interface names are defined and used in a configuration.</summary>
public static class InterfaceReferenceFinder
{
    public static IReadOnlyList<InterfaceReference> Find(ConfigDocument doc, IReadOnlySet<string> names)
    {
        ArgumentNullException.ThrowIfNull(doc);
        ArgumentNullException.ThrowIfNull(names);
        var result = new List<InterfaceReference>();
        Walk(doc, [], [], null, (node, chain, keys, vdom) =>
        {
            switch (node)
            {
                case EditEntry entry when chain.Count > 0 && InterfaceReferenceRules.IsInterfaceKeyedBlock(chain) && names.Contains(entry.Key):
                    var kind = InterfaceReferenceRules.IsDefinitionBlock(chain) ? ReferenceKind.Definition : ReferenceKind.Key;
                    result.Add(new(entry.Key, kind, entry.Statement.Line, Describe(chain, keys, null), vdom));
                    break;

                case FieldNode field:
                    var known = InterfaceReferenceRules.IsInterfaceField(chain, field.Name);
                    if (!known && InterfaceReferenceRules.IsFreeText(field.Name)) break;
                    foreach (var token in InterfaceTokens(field))
                    {
                        if (names.Contains(token.Value))
                        {
                            result.Add(new(token.Value, known ? ReferenceKind.Field : ReferenceKind.Possible, field.Statement.Line, Describe(chain, keys, field.Name), vdom));
                        }
                    }

                    break;
            }
        });
        return result;
    }

    /// <summary>Value tokens of a field, skipping the token that follows <c>ENC</c> (encrypted secrets are never touched).</summary>
    internal static IEnumerable<(int Index, ConfigToken Token)> ValueTokens(FieldNode field)
    {
        var tokens = field.Statement.Tokens;
        for (var i = 2; i < tokens.Count; i++)
        {
            if (!tokens[i].IsQuoted && tokens[i].Value == "ENC")
            {
                i++;
                continue;
            }

            yield return (i, tokens[i]);
        }
    }

    private static IEnumerable<ConfigToken> InterfaceTokens(FieldNode field) => ValueTokens(field).Select(t => t.Token);

    internal static string Describe(IReadOnlyList<string> chain, IReadOnlyList<string> keys, string? field)
    {
        var parts = new List<string>();
        for (var i = 0; i < chain.Count; i++)
        {
            parts.Add(chain[i]);
            if (i < keys.Count && keys[i].Length > 0) parts.Add($"[{keys[i]}]");
        }

        if (field is not null) parts.Add(field);
        return string.Join(' ', parts);
    }

    /// <summary>
    /// Visits every node with its block chain (config paths, outermost first, without the multi-VDOM wrappers),
    /// the edit keys at each level and the VDOM name (null outside multi-VDOM VDOM sections).
    /// </summary>
    internal static void Walk(ConfigContainer container, List<string> chain, List<string> keys, string? vdom, Action<ConfigNode, IReadOnlyList<string>, IReadOnlyList<string>, string?> visit)
    {
        foreach (var child in container.Children)
        {
            switch (child)
            {
                case ConfigBlock block when container is ConfigDocument && block.Path is "global":
                    Walk(block, chain, keys, null, visit);
                    break;

                case ConfigBlock block when container is ConfigDocument && block.Path is "vdom":
                    foreach (var entry in block.Entries)
                    {
                        Walk(entry, chain, keys, entry.Key, visit);
                    }

                    break;

                case ConfigBlock block:
                    visit(block, chain, keys, vdom);
                    chain.Add(block.Path);
                    keys.Add("");
                    Walk(block, chain, keys, vdom, visit);
                    chain.RemoveAt(chain.Count - 1);
                    keys.RemoveAt(keys.Count - 1);
                    break;

                case EditEntry entry when keys.Count == 0:
                    // Malformed input (edit outside config): visit without key tracking.
                    visit(entry, chain, keys, vdom);
                    Walk(entry, chain, keys, vdom, visit);
                    break;

                case EditEntry entry:
                    visit(entry, chain, keys, vdom);
                    var saved = keys[^1];
                    keys[^1] = entry.Key;
                    Walk(entry, chain, keys, vdom, visit);
                    keys[^1] = saved;
                    break;

                default:
                    visit(child, chain, keys, vdom);
                    break;
            }
        }
    }
}
