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

    [Fact]
    public void MultiChipNp6Layout_WithGigabitSpeedsAndSeparators_IsParsedAcrossChips()
    {
        const string text = """
            You can use the diagnose npu np6 port-list command to display this information.
            get hardware npu np6 port-list
            Chip   XAUI Ports            QSGMII Max   Cross-chip
                                                Speed offloading
            ------ ---- -------          ------ ----- ----------
            np6_0  0    port20           NA     1G    Yes
                   3
                   0-3  port13           25G    Yes
            ------ ---- -------          ------ ----- ----------
            np6_1  0    port4            NA     1G    Yes
                   0-3  port31           40G    Yes
            ------ ---- -------          ------ ----- ----------
            Distributing traffic evenly among the NP6 processors can optimize performance.
            """;
        var ports = HardwarePorts.ParsePortList(text);
        Assert.Equal(["port20", "port13", "port4", "port31"], ports.Select(p => p.Name));
        Assert.Equal([1000, 25000, 1000, 40000], ports.Select(p => p.SpeedMbps));
    }

    [Fact]
    public void Np7Layout_WithManyColumns_SkipsRowsThatAreNotConfigNames()
    {
        const string text = """
            diag npu np7 port-list
            Front Panel Port:
            Name     Max_speed(Mbps) Dflt_speed(Mbps) Sw_Trunk_Id Sw_Tcam_Id Group_from_vdom Switch_id SW_port_id SW_port_name
            -------- --------------- ---------------  ----------- ---------- --------------- --------- ---------- ------------
            HA1_np   10000           10000            8           1          1               0         13         n/a
            wan1     25000           25000            8           3          1               0         15         n/a
            port1    25000           25000            8           5          1               0         17         n/a
            -------- --------------- ---------------  ----------- ---------- --------------- --------- ---------- ------------
            """;
        Assert.Equal(["wan1", "port1"], HardwarePorts.ParsePortList(text).Select(p => p.Name));
    }

    [Fact]
    public void RepeatedNames_AreReported()
    {
        const string text = """
            get hardware npu np6 port-list
            Chip   XAUI Ports        Max  Cross-chip
            ------ ---- -------     ----- ----------
            np6_0   2   port23       1G   Yes
                    2   port2        1G   Yes
                    3   port25       1G   Yes
                    3   port2        1G   Yes
            """;
        var duplicates = new List<string>();
        Assert.Equal(["port23", "port2", "port25"], HardwarePorts.ParsePortList(text, duplicates).Select(p => p.Name));
        Assert.Equal(["port2"], duplicates);
    }

    [Theory]
    [InlineData("FG-400F", "FortiGate 400F")]
    [InlineData("FGR-70G", "FortiGate Rugged 70G")]
    public void DisplayNames_FollowTheModelId(string id, string expected) => Assert.Equal(expected, HardwarePorts.DisplayName(id));

    [Theory]
    [InlineData(1000, "1G")]
    [InlineData(10000, "10G")]
    [InlineData(5000, "5G")]
    [InlineData(100, "100M")]
    public void Speeds_AreFormatted(int mbps, string expected) => Assert.Equal(expected, HardwarePorts.FormatSpeed(mbps));
}
