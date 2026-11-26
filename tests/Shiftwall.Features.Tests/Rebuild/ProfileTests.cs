using System.Text;
using Shiftwall.Core.Config;
using Shiftwall.Features.Migration;
using Shiftwall.Features.Rebuild;

namespace Shiftwall.Features.Tests.Rebuild;

/// <summary>Point 2: no policy that had security profiles on the source leaves without protection.</summary>
public sealed class ProfileTests
{
    [Fact]
    public void CustomProfile_IsReplacedByThePreloadedDefault_AndReportedPerPolicy()
    {
        var r = RebuildEngine.Run(RebuildFixtures.Office110C());
        var script = r.FullScript();
        Assert.Contains("set utm-status enable\n        set av-profile \"default\"\n        set webfilter-profile \"default\"", script);
        Assert.DoesNotContain("set status disable", script);

        var row = Assert.Single(r.Profiles);
        Assert.Equal("1", row.Policy);
        Assert.Equal(["av-profile default", "webfilter-profile Ufficio-Web"], row.Original);
        Assert.Equal(["av-profile default", "webfilter-profile default"], row.Applied);
        Assert.False(row.Disabled);
        Assert.Contains(r.Findings, f => f.Code == RebuildCodes.DefaultProfilesApplied && f.Arguments[0] == "1");
        Assert.Contains(r.Applied, a => a.RuleId == "P001" && a.Param == "webfilter-profile");
    }

    [Fact]
    public void DisableOption_WritesThePolicyDisabled()
    {
        var req = RebuildFixtures.Office110C() with { Options = new RebuildOptions(ManagementInterface: "lan", Profiles: ProfileHandling.DisablePolicy) };
        var r = RebuildEngine.Run(req);
        Assert.Contains("    edit 1\n        set status disable\n", r.FullScript());
        Assert.True(Assert.Single(r.Profiles).Disabled);
        Assert.Contains(r.Findings, f => f.Code == RebuildCodes.PolicyDisabledForProfiles);
        Assert.True(r.LintPassed);
    }

    [Fact]
    public void ProfileWithoutDocumentedDefault_OrMissingOnTheTarget_DisablesThePolicy()
    {
        // A DNS filter profile has no documented preloaded name: never dropped silently.
        var text = File.ReadAllText(Path.Combine(RebuildFixtures.Dir, "110c-5.2-office.fgtcfg"))
            .Replace("        set av-profile \"default\"\n", "        set av-profile \"default\"\n        set dnsfilter-profile \"dns-ufficio\"\n", StringComparison.Ordinal);
        var req = RebuildFixtures.Office110C() with { Source = ConfigInspector.Inspect(Encoding.UTF8.GetBytes(text)).Document! };
        var r = RebuildEngine.Run(req);
        Assert.Contains("    edit 1\n        set status disable\n", r.FullScript());
        Assert.Contains(r.Findings, f => f.Code == RebuildCodes.PolicyDisabledForProfiles && f.Arguments[1].Contains("dnsfilter-profile", StringComparison.Ordinal));

        // A factory backup without the preloaded web filter profile: the replacement cannot be checked, the policy is disabled.
        var backup = File.ReadAllText(Path.Combine(RebuildFixtures.Dir, "40f-8.0-target-synthetic.fgtcfg"))
            .Replace("config webfilter profile\n    edit \"default\"\n    next\n", "config webfilter profile\n", StringComparison.Ordinal);
        var target = RebuildFixtures.Target40F() with { DefaultBackup = ConfigInspector.Inspect(Encoding.UTF8.GetBytes(backup)).Document };
        var r2 = RebuildEngine.Run(RebuildFixtures.Office110C() with { Target = target });
        Assert.Contains("    edit 1\n        set status disable\n", r2.FullScript());
    }

    [Fact]
    public void WithoutFactoryBackup_DefaultsComeFromTheDocumentation_AndAreFlagged()
    {
        var r = RebuildEngine.Run(RebuildFixtures.Office110C() with { Target = RebuildFixtures.Target40F(withBackup: false) });
        Assert.Contains("set webfilter-profile \"default\"", r.FullScript());
        Assert.Contains(r.Findings, f => f.Code == RebuildCodes.DefaultProfilesUnverified && f.Severity == FindingSeverity.Warning);
    }
}

/// <summary>Point 4: weak site-to-site VPNs are kept and reported with the best practices.</summary>
public sealed class WeakVpnTests
{
    [Fact]
    public void OldSiteToSiteTunnel_IsKept_AndReported()
    {
        var r = RebuildEngine.Run(RebuildFixtures.Office110C());
        var f = Assert.Single(r.Findings, f => f.Code == RebuildCodes.WeakVpn);
        Assert.Equal("to-sede2", f.Arguments[0]);
        Assert.Contains("IKEv1", f.Arguments[1], StringComparison.Ordinal);
        Assert.Contains("DH 14 5", f.Arguments[1], StringComparison.Ordinal);
        Assert.Contains("3des-sha1", f.Arguments[1], StringComparison.Ordinal);
        Assert.Contains("set ike-version 1", r.FullScript());

        // The dial-up tunnel written by Shiftwall follows the guide (IKEv2, DH 20 21): not reported.
        Assert.DoesNotContain(r.Findings, x => x.Code == RebuildCodes.WeakVpn && x.Arguments[0] == "dialup_vpn");
    }
}
