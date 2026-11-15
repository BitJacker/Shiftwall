using System.Text;

namespace Shiftwall.Core.Config;

/// <summary>
/// Splits FortiOS configuration text into statements and tokens without losing a single character.
/// </summary>
/// <remarks>
/// Rules (FortiOS Administration Guide, "CLI basics"): values may be enclosed in double or single quotes;
/// a backslash escapes the next character (<c>\"</c>, <c>\'</c>, <c>\\</c>, <c>\ </c>); a backslash at end of
/// line continues the statement on the next line. Quoted values may span several lines (certificates,
/// replacement messages). Lines whose first non-blank character is <c>#</c> are comments/header lines.
/// </remarks>
public static class ConfigLexer
{
    public static IReadOnlyList<Statement> Lex(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var statements = new List<Statement>(Math.Max(16, text.Length / 24));
        var pos = 0;
        var line = 1;
        while (pos < text.Length)
        {
            var (statement, next, nextLine) = LexOne(text, pos, line);
            statements.Add(statement);
            pos = next;
            line = nextLine;
        }

        return statements;
    }

    /// <summary>Lexes text that must form exactly one statement (used when editing a statement).</summary>
    internal static Statement LexSingle(string raw, int line)
    {
        var (statement, next, _) = LexOne(raw, 0, line);
        if (next != raw.Length)
        {
            throw new ArgumentException("The edited text does not form a single statement.", nameof(raw));
        }

        return statement;
    }

    private static (Statement Statement, int Next, int NextLine) LexOne(string text, int start, int line)
    {
        var startLine = line;
        var len = text.Length;

        // Comment / header line: no tokenization (it may contain unbalanced quotes).
        var p = start;
        while (p < len && (text[p] == ' ' || text[p] == '\t')) p++;
        if (p < len && text[p] == '#')
        {
            var nl = text.IndexOf('\n', p);
            var end = nl < 0 ? len : nl + 1;
            return (new Statement(text[start..end], startLine, [], false), end, line + 1);
        }

        var tokens = new List<ConfigToken>();
        var value = new StringBuilder();
        var tokenStart = -1;
        char? inQuote = null;
        char firstQuote = '\0';
        var quotedSections = 0;
        var bareChars = 0;
        var i = start;

        void Finish(int endExclusive)
        {
            if (tokenStart < 0) return;
            char? quote = quotedSections == 1 && bareChars == 0 ? firstQuote : null;
            tokens.Add(new ConfigToken(tokenStart - start, text[tokenStart..endExclusive], value.ToString(), quote));
            value.Clear();
            tokenStart = -1;
            quotedSections = 0;
            bareChars = 0;
            firstQuote = '\0';
        }

        void Begin(int at)
        {
            if (tokenStart < 0) tokenStart = at;
        }

        while (i < len)
        {
            var c = text[i];
            if (inQuote is { } q)
            {
                if (c == '\\' && i + 1 < len)
                {
                    if (text[i + 1] == '\n') line++;
                    value.Append(text[i + 1]);
                    i += 2;
                    continue;
                }

                if (c == q)
                {
                    inQuote = null;
                    i++;
                    continue;
                }

                if (c == '\n') line++;
                value.Append(c);
                i++;
                continue;
            }

            if (c == '\n')
            {
                Finish(i);
                i++;
                line++;
                return (new Statement(text[start..i], startLine, tokens, false), i, line);
            }

            if (c is ' ' or '\t' or '\r')
            {
                Finish(i);
                i++;
                continue;
            }

            if (c == '\\')
            {
                // Line continuation: backslash followed by (CR)LF.
                if (i + 1 < len && text[i + 1] == '\n')
                {
                    Finish(i);
                    i += 2;
                    line++;
                    continue;
                }

                if (i + 2 < len && text[i + 1] == '\r' && text[i + 2] == '\n')
                {
                    Finish(i);
                    i += 3;
                    line++;
                    continue;
                }

                Begin(i);
                bareChars++;
                if (i + 1 < len)
                {
                    value.Append(text[i + 1]);
                    i += 2;
                }
                else
                {
                    value.Append(c);
                    i++;
                }

                continue;
            }

            if (c is '"' or '\'')
            {
                Begin(i);
                if (quotedSections == 0 && bareChars == 0) firstQuote = c;
                quotedSections++;
                inQuote = c;
                i++;
                continue;
            }

            Begin(i);
            bareChars++;
            value.Append(c);
            i++;
        }

        // End of text.
        var unterminated = inQuote is not null;
        Finish(len);
        return (new Statement(text[start..len], startLine, tokens, unterminated), len, line);
    }
}
