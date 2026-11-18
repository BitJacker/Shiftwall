using System.Text;

namespace Shiftwall.Core.Config;

/// <summary>A node of the configuration tree. Every node owns the exact source text of its statements.</summary>
public abstract class ConfigNode
{
    public ConfigContainer? Parent { get; internal set; }

    /// <summary>Appends the exact source text of this node (and its descendants) to <paramref name="sb"/>.</summary>
    public abstract void WriteTo(StringBuilder sb);

    /// <summary>Exact source text of this node and its descendants.</summary>
    public string ToText()
    {
        var sb = new StringBuilder();
        WriteTo(sb);
        return sb.ToString();
    }
}

/// <summary>A node that contains child nodes: the document, a <c>config</c> block or an <c>edit</c> entry.</summary>
public abstract class ConfigContainer : ConfigNode
{
    private readonly List<ConfigNode> _children = [];

    public IReadOnlyList<ConfigNode> Children => _children;

    internal void Add(ConfigNode node)
    {
        node.Parent = this;
        _children.Add(node);
    }

    /// <summary>Replaces <paramref name="oldNode"/> with <paramref name="newNode"/> in place.</summary>
    public void Replace(ConfigNode oldNode, ConfigNode newNode)
    {
        var i = _children.IndexOf(oldNode);
        if (i < 0) throw new ArgumentException("Node is not a child of this container.", nameof(oldNode));
        newNode.Parent = this;
        _children[i] = newNode;
        oldNode.Parent = null;
    }

    /// <summary>Inserts <paramref name="node"/> at <paramref name="index"/>.</summary>
    public void Insert(int index, ConfigNode node)
    {
        ArgumentNullException.ThrowIfNull(node);
        node.Parent = this;
        _children.Insert(index, node);
    }

    /// <summary>Index of <paramref name="node"/> among the children, or -1.</summary>
    public int IndexOf(ConfigNode node) => _children.IndexOf(node);

    /// <summary>Removes <paramref name="node"/> (with all its text) from this container.</summary>
    public bool Remove(ConfigNode node)
    {
        if (!_children.Remove(node)) return false;
        node.Parent = null;
        return true;
    }

    public IEnumerable<ConfigBlock> Blocks => _children.OfType<ConfigBlock>();

    public IEnumerable<EditEntry> Entries => _children.OfType<EditEntry>();

    public IEnumerable<FieldNode> Fields => _children.OfType<FieldNode>();

    /// <summary>First direct <c>config</c> child whose path equals <paramref name="path"/> (e.g. "system interface").</summary>
    public ConfigBlock? Block(string path) => Blocks.FirstOrDefault(b => b.Path == path);

    /// <summary>First direct field statement named <paramref name="name"/> (e.g. "vdom").</summary>
    public FieldNode? Field(string name) => Fields.FirstOrDefault(f => f.Name == name);

    /// <summary>All descendants, depth first, in file order.</summary>
    public IEnumerable<ConfigNode> Descendants()
    {
        foreach (var child in _children)
        {
            yield return child;
            if (child is ConfigContainer c)
            {
                foreach (var d in c.Descendants()) yield return d;
            }
        }
    }

    protected void WriteChildren(StringBuilder sb)
    {
        foreach (var child in _children) child.WriteTo(sb);
    }
}

/// <summary>A <c>config &lt;path&gt;</c> ... <c>end</c> block.</summary>
public sealed class ConfigBlock : ConfigContainer
{
    internal ConfigBlock(Statement open)
    {
        Statement = open;
        Path = string.Join(' ', open.Tokens.Skip(1).Select(t => t.Value));
    }

    /// <summary>The opening <c>config</c> statement.</summary>
    public Statement Statement { get; }

    /// <summary>Path after <c>config</c>, words separated by one space (e.g. "firewall policy").</summary>
    public string Path { get; }

    /// <summary>The closing <c>end</c>, or null if the file ended (or an unexpected statement appeared) first.</summary>
    public Statement? End { get; internal set; }

    public override void WriteTo(StringBuilder sb)
    {
        sb.Append(Statement.Raw);
        WriteChildren(sb);
        if (End is not null) sb.Append(End.Raw);
    }
}

/// <summary>An <c>edit &lt;key&gt;</c> ... <c>next</c> table entry.</summary>
public sealed class EditEntry : ConfigContainer
{
    internal EditEntry(Statement open) => Statement = open;

    /// <summary>The opening <c>edit</c> statement.</summary>
    public Statement Statement { get; private set; }

    /// <summary>The entry key, unescaped (e.g. <c>port1</c>, <c>1</c>).</summary>
    public string Key => Statement.Tokens.Count > 1 ? Statement.Tokens[1].Value : "";

    /// <summary>The closing <c>next</c>, or null when the entry is closed by <c>end</c> or not closed at all.</summary>
    public Statement? Next { get; internal set; }

    /// <summary>Replaces the opening statement (used to rename the key).</summary>
    public void SetStatement(Statement statement)
    {
        if (statement.Kind != StatementKind.Edit) throw new ArgumentException("Not an edit statement.", nameof(statement));
        Statement = statement;
    }

    public override void WriteTo(StringBuilder sb)
    {
        sb.Append(Statement.Raw);
        WriteChildren(sb);
        if (Next is not null) sb.Append(Next.Raw);
    }
}

/// <summary>A <c>set</c>/<c>unset</c>/<c>append</c>/... statement.</summary>
public sealed class FieldNode(Statement statement) : ConfigNode
{
    public Statement Statement { get; } = statement;

    public string Verb => Statement.Verb;

    public string Name => Statement.FieldName ?? "";

    /// <summary>Unescaped values after the field name.</summary>
    public IReadOnlyList<string> Values => [.. Statement.FieldValues.Select(t => t.Value)];

    public override void WriteTo(StringBuilder sb) => sb.Append(Statement.Raw);
}

/// <summary>Blank lines, comments, header lines, stray or unknown statements: kept verbatim.</summary>
public sealed class TriviaNode(Statement statement) : ConfigNode
{
    public Statement Statement { get; } = statement;

    /// <summary>Creates a trivia node from raw text that forms exactly one statement (e.g. a header line).</summary>
    public static TriviaNode FromText(string raw) => new(ConfigLexer.LexSingle(raw, 1));

    public override void WriteTo(StringBuilder sb) => sb.Append(Statement.Raw);
}
