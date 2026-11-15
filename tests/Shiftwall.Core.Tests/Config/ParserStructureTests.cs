using Shiftwall.Core.Config;

namespace Shiftwall.Core.Tests.Config;

public sealed class ParserStructureTests
{
    private static readonly ConfigDocument Single = ConfigParser.Parse(Golden.Text("single-vdom.fgtcfg"));

    [Fact]
    public void SingleVdom_HasNoStructuralDiagnostics() => Assert.Empty(Single.Diagnostics);

    [Fact]
    public void TopLevelBlocks_AreInFileOrder() =>
        Assert.Equal(
            ["system global", "system admin", "system interface", "system zone", "firewall address", "firewall addrgrp",
             "firewall policy", "router static", "vpn certificate local", "system replacemsg http url-block", "user local", "system dns"],
            Single.Blocks.Select(b => b.Path));

    [Fact]
    public void EditKeys_AreUnescaped()
    {
        Assert.Equal(["wan1", "internal1", "port10", "vlan100"], Single.Block("system interface")!.Entries.Select(e => e.Key));
        Assert.Equal(["admin", "Security Administrator"], Single.Block("system admin")!.Entries.Select(e => e.Key));
        Assert.Equal(["1", "2"], Single.Block("firewall policy")!.Entries.Select(e => e.Key));
    }

    [Fact]
    public void DoubleQuotedValue_WithEscapes_IsUnescaped()
    {
        var internal1 = Single.Block("system interface")!.Entries.Single(e => e.Key == "internal1");
        Assert.Equal(["LAN \"office\" #1"], internal1.Field("description")!.Values);
    }

    [Fact]
    public void SingleQuotedValue_WithEscapedQuote_IsUnescaped()
    {
        var srv = Single.Block("firewall address")!.Entries.Single(e => e.Key == "srv-web");
        Assert.Equal(["Web server: it's public"], srv.Field("comment")!.Values);
    }

    [Fact]
    public void MultipleValues_AreSeparateTokens()
    {
        var grp = Single.Block("firewall addrgrp")!.Entries.Single();
        Assert.Equal(["srv-web", "net-office"], grp.Field("member")!.Values);
        var wan = Single.Block("system interface")!.Entries.Single(e => e.Key == "wan1");
        Assert.Equal(["ping", "https", "ssh"], wan.Field("allowaccess")!.Values);
    }

    [Fact]
    public void MultiLineQuotedValues_AreOneStatement()
    {
        var policy1 = Single.Block("firewall policy")!.Entries.First();
        var comments = policy1.Field("comments")!;
        Assert.Equal(["Line one\nline two with \"quotes\" and a back\\slash"], comments.Values);
        Assert.Equal(2, comments.Statement.LineCount);

        var cert = Single.Block("vpn certificate local")!.Entries.Single();
        Assert.StartsWith("-----BEGIN CERTIFICATE-----\n", cert.Field("certificate")!.Values[0], StringComparison.Ordinal);
        Assert.Equal(4, cert.Field("private-key")!.Statement.LineCount);

        var buffer = Single.Block("system replacemsg http url-block")!.Field("buffer")!;
        Assert.Contains("<h1 class=\"title\">", buffer.Values[0], StringComparison.Ordinal);
    }

    [Fact]
    public void NestedConfig_InsideEdit_IsAChildBlock()
    {
        var internal1 = Single.Block("system interface")!.Entries.Single(e => e.Key == "internal1");
        var ipv6 = internal1.Block("ipv6");
        Assert.NotNull(ipv6);
        Assert.Equal(["2001:db8:10::1/64"], ipv6.Field("ip6-address")!.Values);
        Assert.NotNull(ipv6.End);
    }

    [Fact]
    public void UnsetAndSet_AreFields()
    {
        var policy2 = Single.Block("firewall policy")!.Entries.Last();
        var unset = policy2.Field("logtraffic")!;
        Assert.Equal("unset", unset.Verb);
        Assert.Empty(unset.Values);
    }

    [Fact]
    public void EncValues_AreTwoTokens_KeptVerbatim()
    {
        var admin = Single.Block("system admin")!.Entries.First();
        var tokens = admin.Field("password")!.Statement.Tokens;
        Assert.Equal("ENC", tokens[2].Value);
        Assert.False(tokens[2].IsQuoted);
        Assert.Equal(4, tokens.Count);
    }

    [Fact]
    public void Header_IsParsed()
    {
        var h = Single.Header;
        Assert.True(h.HasConfigVersion);
        Assert.Equal("FGT60F", h.PlatformCode);
        Assert.Equal("7.4.13", h.Version);
        Assert.Equal("2933", h.Build);
        Assert.Equal("0", h.OpMode);
        Assert.Equal("0", h.VdomFlag);
        Assert.Equal("admin", h.User);
        Assert.Equal("2933", h.Get("buildno"));
        Assert.Equal("1", h.Get("global_vdom"));
        Assert.Equal(4, h.Lines.Count);
    }

    [Fact]
    public void MultiVdom_ScopesAreFound()
    {
        var doc = ConfigParser.Parse(Golden.Text("multi-vdom.fgtcfg"));
        Assert.Empty(doc.Diagnostics);
        Assert.True(doc.IsMultiVdom);
        Assert.NotNull(doc.GlobalScope.Block("system interface"));
        var scopes = doc.VdomScopes().ToList();
        Assert.Contains(scopes, s => s.Vdom == "root" && s.Scope.Block("firewall policy") is not null);
        Assert.Contains(scopes, s => s.Vdom == "branch" && s.Scope.Block("firewall address") is not null);
    }

    [Fact]
    public void EndInsideEdit_ClosesEntryAndBlock()
    {
        var doc = ConfigParser.Parse("config firewall address\n    edit \"a\"\n        set subnet 192.0.2.1 255.255.255.255\nend\n");
        Assert.Empty(doc.Diagnostics);
        var block = doc.Block("firewall address")!;
        Assert.NotNull(block.End);
        Assert.Null(block.Entries.Single().Next);
    }

    [Fact]
    public void TruncatedFile_ReportsUnclosedBlock_AndKeepsText()
    {
        var text = Golden.Text("truncated.fgtcfg");
        var doc = ConfigParser.Parse(text);
        var d = Assert.Single(doc.Diagnostics, x => x.Code == DiagnosticCodes.UnclosedBlock);
        Assert.Equal(DiagnosticSeverity.Error, d.Severity);
        Assert.Equal(["firewall address"], d.Arguments);
        Assert.Equal(text, doc.ToText());
    }

    [Theory]
    [InlineData("end\n", DiagnosticCodes.UnexpectedEnd)]
    [InlineData("next\n", DiagnosticCodes.UnexpectedNext)]
    [InlineData("edit 1\n", DiagnosticCodes.EditOutsideConfig)]
    [InlineData("set hostname x\n", DiagnosticCodes.FieldOutsideBlock)]
    [InlineData("config system global\n    sett hostname x\nend\n", DiagnosticCodes.UnknownStatement)]
    [InlineData("config system global\n    set alias \"never closed\nend\n", DiagnosticCodes.UnterminatedQuote)]
    public void MalformedInput_IsReported_NotThrown(string text, string code)
    {
        var doc = ConfigParser.Parse(text);
        Assert.Contains(doc.Diagnostics, d => d.Code == code);
        Assert.Equal(text, doc.ToText());
    }

    [Fact]
    public void LineContinuation_JoinsPhysicalLines()
    {
        var doc = ConfigParser.Parse("config system global\n    set alias \\\n        \"continued\"\nend\n");
        Assert.Empty(doc.Diagnostics);
        Assert.Equal(["continued"], doc.Block("system global")!.Field("alias")!.Values);
    }

    [Fact]
    public void BackslashEscapedSpace_IsPartOfToken()
    {
        var doc = ConfigParser.Parse("config system admin\n    edit Security\\ Administrator\n    next\nend\n");
        Assert.Equal("Security Administrator", doc.Block("system admin")!.Entries.Single().Key);
    }

    [Fact]
    public void LineCount_CountsPhysicalLines()
    {
        var text = Golden.Text("single-vdom.fgtcfg");
        Assert.Equal(text.Count(c => c == '\n'), Single.LineCount);
    }
}
