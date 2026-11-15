namespace Shiftwall.Core.Config;

/// <summary>What a statement does, decided by its first token.</summary>
public enum StatementKind
{
    /// <summary>Empty or whitespace-only line.</summary>
    Blank,

    /// <summary>A line starting with <c>#</c> (backup header lines, comments).</summary>
    Comment,

    Config,
    Edit,
    Next,
    End,

    /// <summary><c>set</c>, <c>unset</c>, <c>append</c>, <c>select</c>, <c>unselect</c>, <c>clear</c>.</summary>
    Field,

    /// <summary>Anything else (e.g. <c>abort</c>, <c>delete</c>, or text that is not FortiOS CLI).</summary>
    Other,
}

/// <summary>
/// One logical CLI statement. <see cref="Raw"/> is the exact source text, including leading indentation,
/// any multi-line quoted value and the line terminator, so concatenating every statement reproduces the file.
/// </summary>
public sealed class Statement
{
    private static readonly string[] FieldVerbs = ["set", "unset", "append", "select", "unselect", "clear"];

    internal Statement(string raw, int line, IReadOnlyList<ConfigToken> tokens, bool unterminatedQuote)
    {
        Raw = raw;
        Line = line;
        Tokens = tokens;
        UnterminatedQuote = unterminatedQuote;
        Kind = Classify(raw, tokens);
    }

    /// <summary>Exact source text including the trailing line terminator (if any).</summary>
    public string Raw { get; }

    /// <summary>1-based line number of the first character.</summary>
    public int Line { get; }

    public IReadOnlyList<ConfigToken> Tokens { get; }

    public StatementKind Kind { get; }

    /// <summary>A quoted value was still open at end of file.</summary>
    public bool UnterminatedQuote { get; }

    /// <summary>The verb (<c>set</c>, <c>edit</c>...), lower case, or empty.</summary>
    public string Verb => Tokens.Count > 0 && Kind is not (StatementKind.Comment or StatementKind.Blank) ? Tokens[0].Value : "";

    /// <summary>For <see cref="StatementKind.Field"/>: the field name (second token).</summary>
    public string? FieldName => Kind == StatementKind.Field && Tokens.Count > 1 ? Tokens[1].Value : null;

    /// <summary>For <see cref="StatementKind.Field"/>: the value tokens after the field name.</summary>
    public IEnumerable<ConfigToken> FieldValues => Kind == StatementKind.Field ? Tokens.Skip(2) : [];

    /// <summary>Number of physical lines the statement spans.</summary>
    public int LineCount
    {
        get
        {
            var n = 1;
            for (var i = 0; i < Raw.Length - 1; i++)
            {
                if (Raw[i] == '\n') n++;
            }

            return n;
        }
    }

    /// <summary>Returns a copy where token <paramref name="index"/> is replaced by <paramref name="newRaw"/>; every other character is preserved.</summary>
    public Statement WithTokenReplaced(int index, string newRaw)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, Tokens.Count);
        var t = Tokens[index];
        var raw = string.Concat(Raw.AsSpan(0, t.Start), newRaw, Raw.AsSpan(t.End));
        return ConfigLexer.LexSingle(raw, Line);
    }

    public override string ToString() => Raw.TrimEnd('\r', '\n');

    private static StatementKind Classify(string raw, IReadOnlyList<ConfigToken> tokens)
    {
        if (tokens.Count == 0)
        {
            return raw.AsSpan().TrimStart().StartsWith("#") ? StatementKind.Comment : StatementKind.Blank;
        }

        if (raw.AsSpan().TrimStart().StartsWith("#"))
        {
            return StatementKind.Comment;
        }

        var first = tokens[0];
        if (first.IsQuoted)
        {
            return StatementKind.Other;
        }

        return first.Value switch
        {
            "config" => StatementKind.Config,
            "edit" => StatementKind.Edit,
            "next" => StatementKind.Next,
            "end" => StatementKind.End,
            var v when FieldVerbs.Contains(v) => StatementKind.Field,
            _ => StatementKind.Other,
        };
    }
}
