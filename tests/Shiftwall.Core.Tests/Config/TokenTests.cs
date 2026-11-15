using Shiftwall.Core.Config;

namespace Shiftwall.Core.Tests.Config;

public sealed class TokenTests
{
    [Theory]
    [InlineData("port1", "\"port1\"")]
    [InlineData("LAN \"office\"", "\"LAN \\\"office\\\"\"")]
    [InlineData("a\\b", "\"a\\\\b\"")]
    [InlineData("it's", "\"it\\'s\"")]
    [InlineData("", "\"\"")]
    public void Render_QuotesAndEscapes(string value, string expected) => Assert.Equal(expected, ConfigToken.Render(value));

    [Theory]
    [InlineData("port1")]
    [InlineData("LAN \"office\" #1")]
    [InlineData("back\\slash and 'single'")]
    [InlineData("multi\nline")]
    public void Render_ThenLex_YieldsSameValue(string value)
    {
        var s = ConfigLexer.Lex($"set description {ConfigToken.Render(value)}\n").Single();
        Assert.Equal(value, s.Tokens[2].Value);
        Assert.True(s.Tokens[2].IsQuoted);
    }

    [Fact]
    public void WithTokenReplaced_ChangesOnlyThatToken()
    {
        var s = ConfigLexer.Lex("        set srcintf \"port1\" \"port10\"   \r\n").Single();
        var renamed = s.WithTokenReplaced(2, ConfigToken.Render("wan1"));
        Assert.Equal("        set srcintf \"wan1\" \"port10\"   \r\n", renamed.Raw);
        Assert.Equal(["wan1", "port10"], renamed.FieldValues.Select(t => t.Value));
        Assert.Equal(s.Line, renamed.Line);
    }

    [Fact]
    public void WithTokenReplaced_RejectsTextThatSplitsTheStatement()
    {
        var s = ConfigLexer.Lex("set alias \"x\"\n").Single();
        Assert.Throws<ArgumentException>(() => s.WithTokenReplaced(2, "\"x\"\nset other y"));
    }

    [Fact]
    public void MixedQuoting_IsOneUnquotedToken()
    {
        var s = ConfigLexer.Lex("set alias ab\"c d\"e\n").Single();
        Assert.Equal("abc de", s.Tokens[2].Value);
        Assert.False(s.Tokens[2].IsQuoted);
    }

    [Fact]
    public void CommentLine_IsNotTokenized()
    {
        var s = ConfigLexer.Lex("    #note with \"unbalanced quote\n").Single();
        Assert.Equal(StatementKind.Comment, s.Kind);
        Assert.Empty(s.Tokens);
    }
}
