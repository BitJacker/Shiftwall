using Shiftwall.Features.Migration;

namespace Shiftwall.Features.Tests.Migration;

public sealed class MappingSuggestionTests
{
    private static readonly string[] Fg60F = ["wan1", "wan2", "dmz", "internal1", "internal2", "internal3", "internal4", "internal5", "a", "b"];

    private static InterfaceMapping Suggest(IEnumerable<string> source, IEnumerable<string> target, params string[] used) =>
        InterfaceMapping.Suggest(source.Select(s => (s, used.Contains(s) ? 1 : 0)), target);

    [Fact]
    public void FiftyE_To_60F_MapsWanToWanAndLanPortsInOrder()
    {
        var m = Suggest(["wan1", "wan2", "lan1", "lan2", "lan3", "lan4", "lan5"], Fg60F);

        Assert.Equal("wan1", m["wan1"]);
        Assert.Equal("wan2", m["wan2"]);
        Assert.Equal(["internal1", "internal2", "internal3", "internal4", "internal5"], new[] { "lan1", "lan2", "lan3", "lan4", "lan5" }.Select(p => m[p]));
        Assert.Empty(m.DuplicateTargets());
    }

    [Fact]
    public void SameNames_AreKept()
    {
        var m = Suggest(Fg60F, ["wan1", "wan2", "port1", "port2", "port3", "port4", "port5", "port6", "a", "b"]);

        Assert.Equal("wan1", m["wan1"]);
        Assert.Equal("a", m["a"]);
        Assert.Equal(["port1", "port2", "port3", "port4", "port5"], new[] { "internal1", "internal2", "internal3", "internal4", "internal5" }.Select(p => m[p]));
        Assert.Equal("port6", m["dmz"]); // no dmz on the target: it takes the first LAN port left
    }

    [Fact]
    public void UsedPortsWin_WhenTheTargetHasFewerPorts()
    {
        var m = Suggest(["wan1", "wan2", "port1", "port2", "port3", "port4"], ["wan", "lan1", "lan2"], "wan2", "port4");

        Assert.Equal("wan", m["wan2"]); // used: gets the only WAN
        Assert.Equal("lan1", m["port4"]); // used: gets a LAN port first
        Assert.Equal("lan2", m["port1"]);
        Assert.Null(m["wan1"]); // no port left: stays unmapped (unused, so its definition is removed)
        Assert.Empty(m.DuplicateTargets());
    }

    [Fact]
    public void PortsWithoutARole_AreNotGuessed()
    {
        var m = Suggest(["mgmt", "ha1", "s1", "x1"], ["port1", "x1", "x2"]);

        Assert.Null(m["mgmt"]);
        Assert.Null(m["ha1"]);
        Assert.Null(m["s1"]);
        Assert.Equal("x1", m["x1"]);
    }
}
