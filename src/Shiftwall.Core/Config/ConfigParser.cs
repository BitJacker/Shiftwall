namespace Shiftwall.Core.Config;

/// <summary>
/// Builds a lossless tree from FortiOS configuration text. Never throws on malformed input: problems are
/// reported as <see cref="ConfigDocument.Diagnostics"/> and the offending text is kept verbatim.
/// </summary>
public static class ConfigParser
{
    public static ConfigDocument Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var statements = ConfigLexer.Lex(text);
        var doc = new ConfigDocument();

        // Header: leading comment lines only (blank lines end it).
        var header = new List<Statement>();
        foreach (var s in statements)
        {
            if (s.Kind != StatementKind.Comment) break;
            header.Add(s);
        }

        doc.Header = ConfigHeader.Parse(header);

        var stack = new Stack<ConfigContainer>();
        stack.Push(doc);
        var lines = 0;

        foreach (var s in statements)
        {
            lines += s.LineCount;
            if (s.UnterminatedQuote)
            {
                doc.AddDiagnostic(ConfigDiagnostic.Error(DiagnosticCodes.UnterminatedQuote, s.Line));
            }

            var current = stack.Peek();
            switch (s.Kind)
            {
                case StatementKind.Config:
                    var block = new ConfigBlock(s);
                    current.Add(block);
                    stack.Push(block);
                    break;

                case StatementKind.Edit:
                    if (current is ConfigBlock)
                    {
                        var entry = new EditEntry(s);
                        current.Add(entry);
                        stack.Push(entry);
                    }
                    else if (current is EditEntry openEntry && openEntry.Parent is ConfigBlock parentBlock)
                    {
                        // "edit" directly after an entry without "next": close the previous entry implicitly.
                        stack.Pop();
                        var entry = new EditEntry(s);
                        parentBlock.Add(entry);
                        stack.Push(entry);
                    }
                    else
                    {
                        doc.AddDiagnostic(ConfigDiagnostic.Warning(DiagnosticCodes.EditOutsideConfig, s.Line));
                        current.Add(new TriviaNode(s));
                    }

                    break;

                case StatementKind.Next:
                    if (current is EditEntry e)
                    {
                        e.Next = s;
                        stack.Pop();
                    }
                    else
                    {
                        doc.AddDiagnostic(ConfigDiagnostic.Warning(DiagnosticCodes.UnexpectedNext, s.Line));
                        current.Add(new TriviaNode(s));
                    }

                    break;

                case StatementKind.End:
                    // "end" inside an entry closes the entry (without "next") and its block.
                    if (current is EditEntry)
                    {
                        stack.Pop();
                        current = stack.Peek();
                    }

                    if (current is ConfigBlock b)
                    {
                        b.End = s;
                        stack.Pop();
                    }
                    else
                    {
                        doc.AddDiagnostic(ConfigDiagnostic.Warning(DiagnosticCodes.UnexpectedEnd, s.Line));
                        current.Add(new TriviaNode(s));
                    }

                    break;

                case StatementKind.Field:
                    if (current is ConfigDocument)
                    {
                        doc.AddDiagnostic(ConfigDiagnostic.Warning(DiagnosticCodes.FieldOutsideBlock, s.Line));
                    }

                    current.Add(new FieldNode(s));
                    break;

                case StatementKind.Other:
                    doc.AddDiagnostic(ConfigDiagnostic.Warning(DiagnosticCodes.UnknownStatement, s.Line, s.Verb));
                    current.Add(new TriviaNode(s));
                    break;

                default:
                    current.Add(new TriviaNode(s));
                    break;
            }
        }

        // Any block still open: the file is truncated or partial. (An open entry always has an open parent block.)
        while (stack.Count > 1)
        {
            if (stack.Pop() is ConfigBlock open)
            {
                doc.AddDiagnostic(ConfigDiagnostic.Error(DiagnosticCodes.UnclosedBlock, open.Statement.Line, open.Path));
            }
        }

        doc.LineCount = lines;
        return doc;
    }
}
