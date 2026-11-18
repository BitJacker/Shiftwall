using Shiftwall.Core.Config;

namespace Shiftwall.Features.Toolbox;

/// <summary>Read helpers over the lossless tree: tables, entries and field values.</summary>
public static class ConfigTables
{
    /// <summary>The entries of every <c>config &lt;path&gt;</c> block directly under <paramref name="scope"/>, in file order.</summary>
    public static IEnumerable<EditEntry> Entries(ConfigContainer scope, string path) =>
        scope.Blocks.Where(b => b.Path == path).SelectMany(b => b.Entries);

    /// <summary>The first <c>config &lt;path&gt;</c> block directly under <paramref name="scope"/> (settings-type tables).</summary>
    public static ConfigBlock? Settings(ConfigContainer scope, string path) => scope.Block(path);

    /// <summary>Values of the last <c>set &lt;field&gt;</c> directly under <paramref name="node"/>; null when not set (default value).</summary>
    public static IReadOnlyList<string>? Values(ConfigContainer? node, string field)
    {
        if (node is null) return null;
        IReadOnlyList<string>? result = null;
        foreach (var f in node.Fields)
        {
            if (f.Name != field) continue;
            result = f.Verb switch
            {
                "set" => f.Values,
                "unset" => null,
                _ => result,
            };
        }

        return result;
    }

    /// <summary>First value of <c>set &lt;field&gt;</c>, or null when the field is not set.</summary>
    public static string? Value(ConfigContainer? node, string field)
    {
        var v = Values(node, field);
        return v is { Count: > 0 } ? v[0] : null;
    }

    /// <summary>All values joined by a space (multi-value fields such as <c>allowaccess</c>), or null.</summary>
    public static string? Joined(ConfigContainer? node, string field)
    {
        var v = Values(node, field);
        return v is null ? null : string.Join(' ', v);
    }

    /// <summary>
    /// The scopes to analyse: (null, document) for single-VDOM backups; for multi-VDOM backups the global scope
    /// (name "global") and every VDOM that carries configuration.
    /// </summary>
    public static IEnumerable<(string? Name, ConfigContainer Scope)> Scopes(ConfigDocument doc)
    {
        if (!doc.IsMultiVdom)
        {
            yield return (null, doc);
            yield break;
        }

        yield return ("global", doc.GlobalScope);
        foreach (var (vdom, scope) in doc.VdomScopes())
        {
            if (scope.Blocks.Any()) yield return (vdom, scope);
        }
    }

    /// <summary>VDOM-level scopes only (policies, objects, VPNs live here).</summary>
    public static IEnumerable<(string? Name, ConfigContainer Scope)> VdomScopes(ConfigDocument doc) =>
        doc.IsMultiVdom ? doc.VdomScopes().Where(s => s.Scope.Blocks.Any()).Select(s => ((string?)s.Vdom, s.Scope)) : [(null, doc)];

    /// <summary>
    /// Every value token of every <c>set</c> statement below <paramref name="scope"/> (encrypted values excluded:
    /// the token after <c>ENC</c> is never returned).
    /// </summary>
    public static IEnumerable<string> ValueTokens(ConfigContainer scope)
    {
        foreach (var node in scope.Descendants())
        {
            if (node is not FieldNode { Verb: "set" or "append" } f) continue;
            var values = f.Values;
            for (var i = 0; i < values.Count; i++)
            {
                if (values[i] == "ENC")
                {
                    i++;
                    continue;
                }

                yield return values[i];
            }
        }
    }

    /// <summary>Renders a value for display: encrypted values are shown as <c>ENC ••••</c>.</summary>
    public static string Display(IReadOnlyList<string>? values)
    {
        if (values is null) return "";
        var parts = new List<string>(values.Count);
        for (var i = 0; i < values.Count; i++)
        {
            if (values[i] == "ENC" && i + 1 < values.Count)
            {
                parts.Add("ENC ••••");
                i++;
                continue;
            }

            parts.Add(values[i].Contains(' ', StringComparison.Ordinal) ? $"\"{values[i]}\"" : values[i]);
        }

        return string.Join(' ', parts);
    }
}
