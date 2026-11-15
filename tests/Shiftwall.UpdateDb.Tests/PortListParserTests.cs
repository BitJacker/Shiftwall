using Shiftwall.UpdateDb;

namespace Shiftwall.UpdateDb.Tests;

/// <summary>
/// The inputs are short excerpts in the two layouts printed by FortiOS
/// (NP6XLite/NP6Lite and NP7Lite), as converted to text by <see cref="HtmlText"/>.
/// </summary>
public sealed class PortListParserTests
{
    private const string Np6XLite = """
        You can use the command diagnose npu np6xlite port-list to display the configuration.
        diagnose npu np6xlite port-list
        Chip XAUI Ports Max Cross-chip
         Speed offloading
        ------ ---- ------- ----- ----------
        np6xlite_0
         11 wan1 1000M NO
         7 dmz 1000M NO
         6 internal1 1000M NO
         19 x1 10000M NO
         9 a 1000M NO
         Bypass interfaces (WAN1 and 1)
         The model includes a bypass interface pair.
        """;

    private const string Np7Lite = """
        You can use the command diagnose npu np7lite port-list to display the configuration.
        diagnose npu np7lite port-list
        Front Panel Port:
        Name Max_speed(Mbps) Dflt_speed(Mbps) SW_port_id SW_port_name
        -------- --------------- ---------------- ---------- -----------
        port1 1000 1000 29 0/29
        port9 5000 5000 22 0/22
        x1 10000 10000 15 0/15
        -------- --------------- ---------------- ---------- -----------
         The command output also shows the maximum speeds of each interface.
        """;

    [Fact]
    public void Np6XLiteLayout_IsParsed()
    {
        var ports = HardwarePorts.ParsePortList(Np6XLite);
        Assert.Equal(["wan1", "dmz", "internal1", "x1", "a"], ports.Select(p => p.Name));
        Assert.Equal(10000, ports.Single(p => p.Name == "x1").SpeedMbps);
    }

    [Fact]
    public void Np7LiteLayout_IsParsed()
    {
        var ports = HardwarePorts.ParsePortList(Np7Lite);
        Assert.Equal(["port1", "port9", "x1"], ports.Select(p => p.Name));
        Assert.Equal([1000, 5000, 10000], ports.Select(p => p.SpeedMbps));
    }

    [Fact]
    public void TwoChipBlocks_AreBothRead()
    {
        const string text = """
            diagnose npu np6lite port-list
            get hardware npu np6lite port-list
            Chip XAUI Ports Max Cross-chip
            ------ ---- ------- ----- ----------
            np6lite_0
             2 port9 1000M NO
            np6lite_1
             2 wan1 1000M NO
            """;
        Assert.Equal(["port9", "wan1"], HardwarePorts.ParsePortList(text).Select(p => p.Name));
    }

    [Fact]
    public void PageWithoutPortList_YieldsNothing() =>
        Assert.Empty(HardwarePorts.ParsePortList("This page has no command output."));

    [Theory]
    [InlineData(1000, "1G")]
    [InlineData(10000, "10G")]
    [InlineData(5000, "5G")]
    [InlineData(100, "100M")]
    public void Speeds_AreFormatted(int mbps, string expected) => Assert.Equal(expected, HardwarePorts.FormatSpeed(mbps));
}
