namespace Shiftwall.Features.Rebuild;

public enum LintSeverity
{
    Warning,
    Error,
}

public sealed record LintIssue(LintSeverity Severity, int Line, string Code, string Detail);

/// <summary>
/// Final check of a generated script before it is saved: line endings, forbidden characters, balanced
/// config/edit/next/end and every <c>set</c> valid in the CLI schema of the target version.
/// </summary>
public static class CliLint
{
    public const string CarriageReturn = "LINT-CR";
    public const string ForbiddenCharacter = "LINT-CHAR";
    public const string NonAscii = "LINT-UTF8";
    public const string Unbalanced = "LINT-BALANCE";
    public const string UnknownStatement = "LINT-STATEMENT";
    public const string Schema = "LINT-SCHEMA";
    public const string BlankLine = "LINT-BLANK";
    public const string Comment = "LINT-COMMENT";

    /// <param name="allowComments">Script files for the GUI may contain # comment lines (Administration Guide, "Configuration scripts").</param>
    public static IReadOnlyList<LintIssue> Check(string text, CliSchema schema, bool allowComments)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(schema);
        var issues = new List<LintIssue>();
        if (text.Contains('\r', StringComparison.Ordinal)) issues.Add(new(LintSeverity.Error, 0, CarriageReturn, ""));
        var lines = text.Split('\n');
        var stack = new List<Frame>();
        for (var i = 0; i < lines.Length; i++)
        {
            var n = i + 1;
            var raw = lines[i];
            if (i == lines.Length - 1 && raw.Length == 0) break;
            if (raw.Contains('\t', StringComparison.Ordinal) || raw.Contains('?', StringComparison.Ordinal))
            {
                issues.Add(new(LintSeverity.Error, n, ForbiddenCharacter, raw.Contains('?', StringComparison.Ordinal) ? "?" : "TAB"));
            }

            if (raw.Any(c => c > 126 || (c < 32 && c != '\t'))) issues.Add(new(LintSeverity.Warning, n, NonAscii, ""));
            var s = raw.Trim();
            if (s.Length == 0)
            {
                issues.Add(new(LintSeverity.Error, n, BlankLine, ""));
                continue;
            }

            if (s[0] == '#')
            {
                if (!allowComments || stack.Count > 0) issues.Add(new(LintSeverity.Error, n, Comment, ""));
                continue;
            }

            var tokens = Tokenize(s);
            switch (tokens[0])
            {
                case "config":
                {
                    var name = string.Join(' ', tokens.Skip(1));
                    SchemaNode? node;
                    if (stack.Count == 0)
                    {
                        node = schema.Find(name);
                    }
                    else
                    {
                        var parent = stack[^1].Node;
                        node = parent is not null && parent.Sub.TryGetValue(name, out var sub) ? sub : null;
                    }

                    if (node is null) issues.Add(new(LintSeverity.Error, n, Schema, "config " + name));
                    stack.Add(new Frame(node, false));
                    break;
                }

                case "edit":
                    if (stack.Count == 0 || stack[^1].InEdit) issues.Add(new(LintSeverity.Error, n, Unbalanced, "edit"));
                    else stack[^1] = stack[^1] with { InEdit = true };
                    break;
                case "next":
                    if (stack.Count == 0 || !stack[^1].InEdit) issues.Add(new(LintSeverity.Error, n, Unbalanced, "next"));
                    else stack[^1] = stack[^1] with { InEdit = false };
                    break;
                case "end":
                    if (stack.Count == 0 || stack[^1].InEdit) issues.Add(new(LintSeverity.Error, n, Unbalanced, "end"));
                    if (stack.Count > 0) stack.RemoveAt(stack.Count - 1);
                    break;
                case "delete":
                    if (stack.Count == 0 || stack[^1].InEdit) issues.Add(new(LintSeverity.Error, n, Unbalanced, "delete"));
                    break;
                case "set" or "unset":
                {
                    if (stack.Count == 0 || tokens.Count < 2)
                    {
                        issues.Add(new(LintSeverity.Error, n, Unbalanced, tokens[0]));
                        break;
                    }

                    var node = stack[^1].Node;
                    if (node is null) break; // already reported on the config line
                    var param = tokens[1];
                    if (tokens[0] == "unset")
                    {
                        if (!node.Params.ContainsKey(param)) issues.Add(new(LintSeverity.Error, n, Schema, param));
                    }
                    else if (CliSchema.Check(node, param, [.. tokens.Skip(2)]) is { } problem)
                    {
                        issues.Add(new(LintSeverity.Error, n, Schema, $"{param}: {problem}"));
                    }

                    break;
                }

                default:
                    issues.Add(new(LintSeverity.Error, n, UnknownStatement, tokens[0]));
                    break;
            }
        }

        if (stack.Count > 0) issues.Add(new(LintSeverity.Error, lines.Length, Unbalanced, "end"));
        return issues;
    }

    private sealed record Frame(SchemaNode? Node, bool InEdit);

    /// <summary>Splits a CLI line into unescaped tokens (quotes and backslash escapes as in "CLI basics").</summary>
    internal static List<string> Tokenize(string s)
    {
        var tokens = new List<string>();
        var cur = new System.Text.StringBuilder();
        char? quote = null;
        var any = false;
        for (var i = 0; i < s.Length; i++)
        {
            var c = s[i];
            if (c == '\\' && i + 1 < s.Length)
            {
                cur.Append(s[++i]);
                any = true;
            }
            else if (quote is not null)
            {
                if (c == quote) quote = null;
                else cur.Append(c);
            }
            else if (c is '"' or '\'')
            {
                quote = c;
                any = true;
            }
            else if (c == ' ')
            {
                if (any) tokens.Add(cur.ToString());
                cur.Clear();
                any = false;
            }
            else
            {
                cur.Append(c);
                any = true;
            }
        }

        if (any) tokens.Add(cur.ToString());
        return tokens;
    }
}
