using Shiftwall.Core.Config;

namespace Shiftwall.Core.Tests.Config;

public sealed class InterfaceInventoryTests
{
    [Fact]
    public void Physical_UsesExplicitTypeOnly()
    {
        var doc = ConfigParser.Parse(Golden.Text("single-vdom.fgtcfg"));
        Assert.Equal(["wan1", "internal1", "port10"], InterfaceInventory.Physical(doc).Select(i => i.Name));
        var vlan = InterfaceInventory.Declared(doc).Single(i => i.Name == "vlan100");
        Assert.Equal(InterfaceInventory.DefaultType, vlan.Type);
    }

    [Fact]
    public void VirtualSwitches_ListMemberPorts()
    {
        var doc = ConfigParser.Parse(Golden.Text("default-synthetic.fgtcfg"));
        var sw = Assert.Single(InterfaceInventory.VirtualSwitches(doc));
        Assert.Equal("internal", sw.Name);
        Assert.Equal(["internal1", "internal2"], sw.Members);
        Assert.Equal(["wan1", "dmz", "internal1", "internal2"], InterfaceInventory.Physical(doc).Select(i => i.Name));
    }

    [Fact]
    public void MultiVdom_ReadsGlobalScope()
    {
        var doc = ConfigParser.Parse(Golden.Text("multi-vdom.fgtcfg"));
        var ifs = InterfaceInventory.Physical(doc);
        Assert.Equal(["port1", "port2"], ifs.Select(i => i.Name));
        Assert.Equal(["root", "branch"], ifs.Select(i => i.Vdom));
    }
}
