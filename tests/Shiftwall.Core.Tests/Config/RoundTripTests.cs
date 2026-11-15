using System.Text;
using Shiftwall.Core.Config;

namespace Shiftwall.Core.Tests.Config;

/// <summary>parse + serialize must reproduce the input byte for byte when nothing is changed.</summary>
public sealed class RoundTripTests
{
    public static TheoryData<string> Fixtures() => [.. Directory.GetFiles(Golden.Dir, "*.fgtcfg").Select(f => Path.GetFileName(f))];

    private static void AssertRoundTrip(byte[] bytes)
    {
        var text = ConfigText.Decode(bytes);
        var doc = ConfigParser.Parse(text.Text);
        var output = text.Encode(doc.ToText());
        Assert.Equal(bytes, output);
    }

    [Theory]
    [MemberData(nameof(Fixtures))]
    public void Fixture_RoundTripsExactly(string file) => AssertRoundTrip(Golden.Bytes(file));

    [Theory]
    [MemberData(nameof(Fixtures))]
    public void Fixture_WithCrLf_RoundTripsExactly(string file) =>
        AssertRoundTrip(Encoding.UTF8.GetBytes(Golden.Text(file).Replace("\n", "\r\n", StringComparison.Ordinal)));

    [Theory]
    [MemberData(nameof(Fixtures))]
    public void Fixture_WithoutTrailingNewline_RoundTripsExactly(string file) =>
        AssertRoundTrip(Encoding.UTF8.GetBytes(Golden.Text(file).TrimEnd('\n')));

    [Theory]
    [MemberData(nameof(Fixtures))]
    public void Fixture_WithBom_RoundTripsExactly(string file) =>
        AssertRoundTrip([0xEF, 0xBB, 0xBF, .. Golden.Bytes(file)]);

    [Fact]
    public void Latin1Bytes_RoundTripExactly()
    {
        // "Società" encoded in ISO-8859-1 is not valid UTF-8.
        byte[] bytes = [.. Encoding.ASCII.GetBytes("config system global\n    set alias \"Societ"), 0xE0, .. Encoding.ASCII.GetBytes("\"\nend\n")];
        var text = ConfigText.Decode(bytes);
        Assert.Equal(ConfigTextEncoding.Latin1Fallback, text.Encoding);
        AssertRoundTrip(bytes);
    }

    [Fact]
    public void Utf8NonAscii_RoundTripsExactly() =>
        AssertRoundTrip(Encoding.UTF8.GetBytes("config system global\n    set alias \"Società – Ufficio № 1\"\nend\n"));

    [Fact]
    public void TabsTrailingSpacesAndBlankLines_AreKept() =>
        AssertRoundTrip(Encoding.UTF8.GetBytes("config system global   \n\tset hostname \"x\"\t\n\n\n   \nend  \n\n"));

    [Fact]
    public void RandomCliLikeText_RoundTrips()
    {
        string[] pieces = ["config", "edit", "next", "end", "set", "unset", "append", " ", "  ", "\t", "\n", "\r\n", "\"", "'", "\\", "#", "port1", "ENC", "x", "é", "\r"];
        var random = new Random(20261115);
        for (var i = 0; i < 3000; i++)
        {
            var sb = new StringBuilder();
            var n = random.Next(0, 60);
            for (var j = 0; j < n; j++) sb.Append(pieces[random.Next(pieces.Length)]);
            var s = sb.ToString();
            Assert.Equal(s, ConfigParser.Parse(s).ToText());
        }
    }

    [Fact]
    public void RandomBytes_RoundTrip()
    {
        var random = new Random(42);
        for (var i = 0; i < 500; i++)
        {
            var bytes = new byte[random.Next(0, 400)];
            random.NextBytes(bytes);
            AssertRoundTrip(bytes);
        }
    }
}
