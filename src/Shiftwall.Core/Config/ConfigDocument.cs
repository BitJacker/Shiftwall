using System.Text;

namespace Shiftwall.Core.Config;

/// <summary>
/// A parsed FortiOS configuration. The tree is lossless: <see cref="ToText"/> returns exactly the parsed text
/// as long as no node has been changed.
/// </summary>
public sealed class ConfigDocument : ConfigContainer
{
    private readonly List<ConfigDiagnostic> _diagnostics = [];

    internal ConfigDocument()
    {
    }

    public ConfigHeader Header { get; internal set; } = null!;

    /// <summary>Structural problems found while parsing (unbalanced blocks, stray statements...).</summary>
    public IReadOnlyList<ConfigDiagnostic> Diagnostics => _diagnostics;

    /// <summary>Total number of physical lines.</summary>
    public int LineCount { get; internal set; }

    /// <summary>True when the backup uses <c>config global</c> / <c>config vdom</c> at the top level.</summary>
    public bool IsMultiVdom => Block("vdom") is not null && Block("global") is not null;

    /// <summary>
    /// The containers that hold VDOM-level configuration: the document itself for single-VDOM backups,
    /// or each <c>edit &lt;vdom&gt;</c> under the top-level <c>config vdom</c> blocks for multi-VDOM backups.
    /// </summary>
    public IEnumerable<(string? Vdom, ConfigContainer Scope)> VdomScopes()
    {
        if (!IsMultiVdom)
        {
            yield return (null, this);
            yield break;
        }

        foreach (var vdomBlock in Blocks.Where(b => b.Path == "vdom"))
        {
            foreach (var entry in vdomBlock.Entries)
            {
                // The first "config vdom" in a multi-VDOM backup only declares the VDOMs (edit root / next);
                // later ones carry the configuration. Both are returned; empty entries simply have no blocks.
                yield return (entry.Key, entry);
            }
        }
    }

    /// <summary>The container holding global settings (<c>config global</c> in multi-VDOM backups, the document otherwise).</summary>
    public ConfigContainer GlobalScope => IsMultiVdom ? Block("global")! : this;

    internal void AddDiagnostic(ConfigDiagnostic d) => _diagnostics.Add(d);

    public override void WriteTo(StringBuilder sb) => WriteChildren(sb);

    /// <summary>Serializes the document back to text.</summary>
    public string ToText(int capacityHint = 0)
    {
        var sb = new StringBuilder(capacityHint);
        WriteTo(sb);
        return sb.ToString();
    }
}
