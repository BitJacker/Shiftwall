using System.Globalization;
using System.Text;
using Shiftwall.Core.Config;
using Shiftwall.Features.Migration;
using Shiftwall.Features.Rebuild;
using Shiftwall.Features.Tests.Toolbox.TwoFactor;

namespace Shiftwall.Features.Tests.Rebuild;

/// <summary>Section 6b: logical switches mapped, recreated or split on the target.</summary>
public sealed class SwitchPlanTests
{
    private static readonly CultureInfo En = CultureInfo.GetCultureInfo("en");

    private static string Errors(RebuildResult r) => string.Join("; ", r.Findings.Where(f => f.Severity == FindingSeverity.Error).Select(f => f.Format(En)));

    private static void AssertClean(RebuildResult r)
    {
        Assert.False(r.IsBlocked, Errors(r));
        Assert.True(r.LintPassed, string.Join("; ", r.Lint.Select(l => $"{l.Line} {l.Code} {l.Detail}")));
        Assert.All(r.Counts, c => Assert.Equal(c.Source, c.Rebuilt + c.Converted + c.Manual));
        foreach (var f in r.GuiFiles("t").Concat(r.TerminalFiles()))
        {
            Assert.DoesNotContain(CliLint.Check(f.Text, r.TargetSchema, allowComments: true), l => l.Severity == LintSeverity.Error);
        }
    }

    [Fact]
    public void Recreated_MatchesTheReviewedGolden()
    {
        var r = RebuildEngine.Run(RebuildFixtures.Office110CRecreated());
        AssertClean(r);
        TwoFactorFixtures.AssertGolden(Path.Combine("..", "Rebuild", "110c-5.2-to-40f-8.0-recreated.txt"), RebuildTests.Describe(r));
    }

    [Fact]
    public void Split_MatchesTheReviewedGolden()
    {
        var r = RebuildEngine.Run(RebuildFixtures.Office110CSplit());
        AssertClean(r);
        TwoFactorFixtures.AssertGolden(Path.Combine("..", "Rebuild", "110c-5.2-to-40f-8.0-split.txt"), RebuildTests.Describe(r));
    }

    [Fact]
    public void Recreated_IsAHardwareSwitchOnThe40F_CreatedBeforeEverythingThatUsesIt()
    {
        var r = RebuildEngine.Run(RebuildFixtures.Office110CRecreated());
        var script = r.FullScript();

        Assert.Contains(r.Findings, f => f.Code == RebuildCodes.HardwareSwitchChosen);
        var remove = script.IndexOf("config system virtual-switch\n    edit \"lan\"\n        config port\n            delete \"lan1\"\n            delete \"lan2\"", StringComparison.Ordinal);
        var prepare = script.IndexOf("edit \"lan1\"\n        set mode static\n        set ip 0.0.0.0 0.0.0.0", StringComparison.Ordinal);
        var create = script.IndexOf("edit \"internal\"\n        set physical-switch \"sw0\"\n        config port\n            edit \"lan1\"\n            next\n            edit \"lan2\"", StringComparison.Ordinal);
        Assert.True(remove >= 0 && prepare > remove && create > prepare, script);

        // Everything that was on the source switch follows it: address, VLAN child, DHCP server, policies, address object.
        var firstUse = script.IndexOf("set interface \"internal\"", StringComparison.Ordinal);
        Assert.True(firstUse > create);
        Assert.Contains("set ip 192.168.10.1 255.255.255.0", script);
        Assert.Contains("set alias \"LAN Ufficio\"", script);
        Assert.Contains("set srcintf \"internal\"", script);
        Assert.Contains("set associated-interface \"internal\"", script);
        Assert.Contains("edit \"vlan20-ospiti\"", script);
        Assert.Matches("config system dhcp server[^#]*set interface \"internal\"", script);
        Assert.Contains(r.Counts, c => c.Path == "firewall policy" && c is { Source: 6, Rebuilt: 5, Converted: 1 });
    }

    [Fact]
    public void Recreated_AsSoftwareSwitch_FollowsTheDocumentedSyntax_AndWarnsAboutTheCpu()
    {
        var r = RebuildEngine.Run(RebuildFixtures.Office110CRecreated(NewInterfaceKind.SoftwareSwitch));
        AssertClean(r);
        var script = r.FullScript();
        Assert.Contains("config system switch-interface\n    edit \"internal\"\n        set vdom \"root\"\n        set member \"lan1\" \"lan2\"\n        set type switch", script);
        Assert.Contains("edit \"internal\"\n        set vdom \"root\"\n        set type switch", script);
        Assert.Contains(r.Findings, f => f.Code == RebuildCodes.SoftwareSwitchForced);
    }

    [Fact]
    public void Split_PutsTheAddressOnOnePort_AndWarnsAboutTheOthers()
    {
        var r = RebuildEngine.Run(RebuildFixtures.Office110CSplit());
        var script = r.FullScript();
        Assert.Contains("edit \"lan1\"\n        set vdom \"root\"\n        set mode static\n        set ip 192.168.10.1 255.255.255.0", script);
        Assert.Contains("delete \"lan1\"", script);
        Assert.Contains("delete \"lan2\"", script);
        Assert.Contains(r.Findings, f => f.Code == RebuildCodes.SplitPortNoAddress && f.Arguments[0] == "lan2");
        Assert.DoesNotContain("192.168.10.1 255.255.255.0\n", script.Replace("edit \"lan1\"\n        set vdom \"root\"\n        set mode static\n        set ip 192.168.10.1 255.255.255.0\n", "", StringComparison.Ordinal), StringComparison.Ordinal);
    }

    [Fact]
    public void SwitchThatWouldBeEmpty_BlocksTheExport()
    {
        var req = RebuildFixtures.Office110CRecreated() with
        {
            Interfaces = [new InterfacePlan("wan1", "wan"), new InterfacePlan("internal", "internal"), new InterfacePlan("wan2", "lan3")],
        };
        var r = RebuildEngine.Run(req);
        Assert.True(r.IsBlocked);
        Assert.Contains(r.Findings, f => f.Code == RebuildCodes.SwitchWouldBeEmpty && f.Arguments[0] == "lan");
    }

    [Fact]
    public void PortChosenTwice_BlocksTheExport()
    {
        var req = RebuildFixtures.Office110CRecreated() with
        {
            Interfaces = [new InterfacePlan("wan1", "wan"), new InterfacePlan("internal", "internal"), new InterfacePlan("wan2", "lan2")],
        };
        var r = RebuildEngine.Run(req);
        Assert.True(r.IsBlocked);
        Assert.Contains(r.Findings, f => f.Code == RebuildCodes.PortUsedTwice && f.Arguments[0] == "lan2");
    }

    [Fact]
    public void PortUsedByTheFactoryConfiguration_CannotJoinASwitch()
    {
        var backup = Parse(File.ReadAllText(Path.Combine(RebuildFixtures.Dir, "40f-8.0-target-synthetic.fgtcfg"))
            + "config firewall policy\n    edit 1\n        set srcintf \"lan\"\n        set dstintf \"a\"\n    next\nend\n");
        var target = RebuildFixtures.Target40F() with { DefaultBackup = backup };
        var layout = SwitchPlanner.Plan(target, [new InterfacePlan("internal", "internal")], [new NewInterfacePlan("internal", NewInterfaceKind.Switch, ["lan1", "a"])]);

        Assert.Contains(layout.Findings, f => f.Code == RebuildCodes.PortUsedOnTarget && f.Arguments[0] == "a" && f.Arguments[1] == "firewall policy 1");
        Assert.Contains(layout.Ports, p => p.Port == "a" && p.State == PortState.UsedOnTarget);
        Assert.Contains(layout.Ports, p => p.Port == "lan1" && p.State == PortState.InSwitch && p.Switch == "lan" && p.ChosenBy == "internal");
    }

    [Fact]
    public void PortsOnDifferentChips_OrUnknownModel_GiveASoftwareSwitch_OrAnErrorWhenHardwareIsForced()
    {
        var backup = Parse(File.ReadAllText(Path.Combine(RebuildFixtures.Dir, "40f-8.0-target-synthetic.fgtcfg"))
            .Replace("\n    edit \"lan3\"\n        set vdom", "\n    edit \"x1\"\n        set type physical\n    next\n    edit \"lan3\"\n        set vdom", StringComparison.Ordinal));
        var target = RebuildFixtures.Target40F() with { DefaultBackup = backup };
        NewInterfacePlan Plan(NewInterfaceKind k) => new("sw1", k, ["lan1", "x1"]);
        var plans = new[] { new InterfacePlan("internal", "sw1") };

        var auto = SwitchPlanner.Plan(target, plans, [Plan(NewInterfaceKind.Switch)]);
        Assert.False(auto.HasErrors);
        Assert.Contains(auto.Findings, f => f.Code == RebuildCodes.SwitchDifferentChips);
        Assert.Contains(auto.Findings, f => f.Code == RebuildCodes.SoftwareSwitchUsed);
        Assert.False(Assert.Single(auto.Changes, c => c.Name == "sw1").Hardware);

        var forced = SwitchPlanner.Plan(target, plans, [Plan(NewInterfaceKind.HardwareSwitch)]);
        Assert.Contains(forced.Findings, f => f.Code == RebuildCodes.HardwareSwitchNotPossible && f.Severity == FindingSeverity.Error);

        var unknown = SwitchPlanner.Plan(target with { ModelId = "FG-60E" }, plans, [new NewInterfacePlan("sw1", NewInterfaceKind.Switch, ["lan1", "lan2"])]);
        Assert.Contains(unknown.Findings, f => f.Code == RebuildCodes.SwitchChipUnknown);

        var noBackup = SwitchPlanner.Plan(RebuildFixtures.Target40F(withBackup: false), plans, [new NewInterfacePlan("sw1", NewInterfaceKind.Switch, ["lan1", "lan2"])]);
        Assert.Contains(noBackup.Findings, f => f.Code == RebuildCodes.SwitchNoPhysicalSwitch);
        Assert.False(Assert.Single(noBackup.Changes, c => c.Name == "sw1").Hardware);
    }

    [Fact]
    public void ExistingSwitch_CanBeRebuiltWithOtherPorts()
    {
        var layout = SwitchPlanner.Plan(RebuildFixtures.Target40F(), [new InterfacePlan("internal", "lan")], [new NewInterfacePlan("lan", NewInterfaceKind.Switch, ["lan1", "lan2"])]);
        Assert.False(layout.HasErrors);
        var change = Assert.Single(layout.Changes);
        Assert.Equal(["lan3"], change.Removed);
        Assert.Empty(change.Ports);
        Assert.Contains(layout.Ports, p => p.Port == "lan3" && p.ChosenBy is null);
    }

    [Fact]
    public void TypedName_CanBecomeAVlan_WithAValidParent()
    {
        var req = RebuildFixtures.Office110C() with
        {
            Interfaces = [new InterfacePlan("wan1", "wan"), new InterfacePlan("internal", "lan"), new InterfacePlan("wan2", "guest")],
            NewInterfaces = [new NewInterfacePlan("guest", NewInterfaceKind.Vlan, [], "lan", 30)],
        };
        var r = RebuildEngine.Run(req);
        AssertClean(r);
        Assert.Contains("edit \"guest\"\n        set vdom \"root\"\n        set interface \"lan\"\n        set vlanid 30", r.FullScript());

        var bad = SwitchPlanner.Plan(RebuildFixtures.Target40F(), [], [new NewInterfacePlan("guest", NewInterfaceKind.Vlan, [], "nope", 5000)]);
        Assert.Contains(bad.Findings, f => f.Code == RebuildCodes.InvalidVlan);
        var name = SwitchPlanner.Plan(RebuildFixtures.Target40F(), [], [new NewInterfacePlan("a name too long!", NewInterfaceKind.Switch, ["lan1"])]);
        Assert.Contains(name.Findings, f => f.Code == RebuildCodes.InvalidNewName);
    }

    private static ConfigDocument Parse(string text) => ConfigInspector.Inspect(Encoding.UTF8.GetBytes(text)).Document!;
}
