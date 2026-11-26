using System.Text;
using Shiftwall.Core.Config;
using Shiftwall.Features.Migration;
using Shiftwall.Features.Rebuild;

namespace Shiftwall.Features.Tests.Rebuild;

/// <summary>Regression tests for the problems found by the migration-verifier on the switch, profile and password work.</summary>
public sealed class VerifierFindingsTests
{
    private static ConfigDocument Source(Func<string, string> edit) =>
        ConfigInspector.Inspect(Encoding.UTF8.GetBytes(edit(File.ReadAllText(Path.Combine(RebuildFixtures.Dir, "110c-5.2-office.fgtcfg"))))).Document!;

    [Fact]
    public void GeneratedSecrets_AreDifferentForEveryObject()
    {
        var r = RebuildEngine.Run(RebuildFixtures.Office110C());
        var psks = r.Secrets.Where(s => s.Category == SecretCategory.VpnPsk).ToList();
        Assert.Equal(2, psks.Count); // to-sede2 and the dial-up tunnel
        Assert.NotEqual(psks[0].Password, psks[1].Password);
    }

    [Fact]
    public void TypedSecret_SharedByTunnels_IsReported()
    {
        var r = RebuildEngine.Run(RebuildFixtures.Office110C(new SecretPlan { Generate = true, Provided = new Dictionary<SecretCategory, string> { [SecretCategory.VpnPsk] = "Abcdefgh-12345678" } }));
        Assert.Contains(r.Findings, f => f.Code == RebuildCodes.ReusedTypedCredential);
    }

    [Fact]
    public void ProfileRemovedByARule_DisablesThePolicy()
    {
        var doc = Source(t => t.Replace("        set av-profile \"default\"\n", "        set av-profile \"default\"\n        set dlp-sensor \"dlp-ufficio\"\n", StringComparison.Ordinal));
        var r = RebuildEngine.Run(RebuildFixtures.Office110C() with { Source = doc });
        Assert.Contains("    edit 1\n        set status disable\n", r.FullScript());
        Assert.Contains(r.Findings, f => f.Code == RebuildCodes.PolicyDisabledForProfiles && f.Arguments[1].Contains("dlp-sensor", StringComparison.Ordinal));
    }

    [Fact]
    public void ProfilesWithUtmDisabledOnTheSource_AreNotWritten()
    {
        var doc = Source(t => t.Replace("        set utm-status enable\n", "", StringComparison.Ordinal));
        var r = RebuildEngine.Run(RebuildFixtures.Office110C() with { Source = doc });
        var script = r.FullScript();
        Assert.DoesNotContain("utm-status", script);
        Assert.DoesNotContain("av-profile", script);
        Assert.DoesNotContain("set status disable", script);
        Assert.Contains(r.Findings, f => f.Code == RebuildCodes.ProfilesInactiveOnSource);
    }

    [Fact]
    public void ExistingSwitch_IsNeverEmptied_BeforeItsNewPortsArrive()
    {
        var layout = SwitchPlanner.Plan(RebuildFixtures.Target40F(), [new InterfacePlan("internal", "lan")], [new NewInterfacePlan("lan", NewInterfaceKind.Switch, ["a"])]);
        Assert.Contains(layout.Findings, f => f.Code == RebuildCodes.SwitchWouldBeEmpty && f.Arguments[0] == "lan");
    }

    [Fact]
    public void PortOnAnotherChip_CannotJoinAnExistingHardwareSwitch()
    {
        var backup = File.ReadAllText(Path.Combine(RebuildFixtures.Dir, "40f-8.0-target-synthetic.fgtcfg"))
            .Replace("\n    edit \"lan3\"\n        set vdom", "\n    edit \"x1\"\n        set type physical\n    next\n    edit \"lan3\"\n        set vdom", StringComparison.Ordinal);
        var target = RebuildFixtures.Target40F() with { DefaultBackup = ConfigInspector.Inspect(Encoding.UTF8.GetBytes(backup)).Document };
        var layout = SwitchPlanner.Plan(target, [new InterfacePlan("internal", "lan")], [new NewInterfacePlan("lan", NewInterfaceKind.Switch, ["lan1", "lan2", "lan3", "x1"])]);
        Assert.Contains(layout.Findings, f => f.Code == RebuildCodes.HardwareSwitchNotPossible && f.Severity == FindingSeverity.Error);
    }

    [Fact]
    public void VlanOnASwitchMember_OrDuplicatedId_IsRejected()
    {
        var member = SwitchPlanner.Plan(RebuildFixtures.Target40F(), [], [new NewInterfacePlan("v30", NewInterfaceKind.Vlan, [], "lan1", 30)]);
        Assert.Contains(member.Findings, f => f.Code == RebuildCodes.InvalidVlan);

        var req = RebuildFixtures.Office110C() with
        {
            Interfaces = [new InterfacePlan("wan1", "wan"), new InterfacePlan("internal", "lan"), new InterfacePlan("wan2", "guest")],
            NewInterfaces = [new NewInterfacePlan("guest", NewInterfaceKind.Vlan, [], "lan", 20)], // vlan20-ospiti already uses ID 20 on lan
        };
        var r = RebuildEngine.Run(req);
        Assert.Contains(r.Findings, f => f.Code == RebuildCodes.DuplicateVlan && f.Arguments[0] == "20");
    }

    [Fact]
    public void NewSwitch_NamedLikeASourceZone_IsRejected()
    {
        var req = RebuildFixtures.Office110CRecreated() with
        {
            Interfaces = [new InterfacePlan("wan1", "wan"), new InterfacePlan("internal", "OSPITI"), new InterfacePlan("wan2", "lan")],
            NewInterfaces = [new NewInterfacePlan("OSPITI", NewInterfaceKind.Switch, ["lan1", "lan2"])],
        };
        Assert.Contains(RebuildEngine.Run(req).Findings, f => f.Code == RebuildCodes.DuplicateTarget && f.Arguments[0] == "OSPITI");
    }

    [Fact]
    public void SplitPortEqualToTheTarget_IsIgnored_AndAContestedPortIsNotListedTwice()
    {
        var split = SwitchPlanner.Plan(RebuildFixtures.Target40F(), [new InterfacePlan("internal", "lan1", SplitPorts: ["lan1", "lan2"]), new InterfacePlan("wan2", "lan")], []);
        Assert.DoesNotContain(split.Findings, f => f.Code == RebuildCodes.SplitPortNoAddress && f.Arguments[0] == "lan1");

        var twice = SwitchPlanner.Plan(RebuildFixtures.Target40F(), [new InterfacePlan("wan2", "lan1")], [new NewInterfacePlan("sw2", NewInterfaceKind.Switch, ["lan1", "lan2"])]);
        Assert.Contains(twice.Findings, f => f.Code == RebuildCodes.PortUsedTwice);
        Assert.DoesNotContain(twice.Changes, c => c.Name == "sw2" && c.Ports.Contains("lan1"));
    }
}
