using System.Globalization;
using System.Text;
using Shiftwall.Core.Config;
using Shiftwall.Features.Toolbox;
using Shiftwall.Features.Toolbox.Audit;

namespace Shiftwall.Features.Tests.Toolbox;

internal static class Tb
{
    public static string Dir => Path.Combine(AppContext.BaseDirectory, "Golden", "Toolbox");

    public static string Text(string f) => File.ReadAllText(Path.Combine(Dir, f), Encoding.UTF8);

    public static ConfigDocument Doc(string f) => ConfigParser.Parse(Text(f));

    public static ConfigDocument Parse(string text) => ConfigParser.Parse(text);

    public const string Header = "#config-version=FGTSYN-7.4.13-FW-build2933-261022:opmode=0:vdom=0:user=admin\n#buildno=2933\n";
}

public sealed class AuditTests
{
    private static readonly AuditReport SiteA = AuditEngine.Run(Tb.Doc("site-a.fgtcfg"));

    private static IEnumerable<AuditFinding> Of(string rule) => SiteA.Findings.Where(f => f.RuleId == rule);

    [Fact]
    public void EveryRule_HasSourcesAndLocalizedTexts()
    {
        foreach (var rule in AuditEngine.Rules)
        {
            Assert.NotEmpty(rule.Sources);
            Assert.All(rule.Sources, s => Assert.StartsWith("https://", s.Url));
            foreach (var culture in new[] { CultureInfo.GetCultureInfo("en"), CultureInfo.GetCultureInfo("it-IT") })
            {
                Assert.NotEqual(rule.Id + ".Title", rule.Title(culture));
                Assert.NotEqual(rule.Id + ".Why", rule.Why(culture));
                Assert.NotEqual(rule.Id + ".Fix", rule.Fix(culture));
            }
        }
    }

    [Fact]
    public void Sources_AreOfficialOrIetf()
    {
        var hosts = AuditEngine.Rules.SelectMany(r => r.Sources).Select(s => new Uri(s.Url).Host).Distinct().ToList();
        Assert.All(hosts, h => Assert.Contains(h, new[] { "docs.fortinet.com", "fortinetweb.s3.amazonaws.com", "www.rfc-editor.org" }));
        Assert.All(AuditEngine.Rules.SelectMany(r => r.Sources).Where(s => s.Url.Contains("fortinetweb", StringComparison.Ordinal)),
            s => Assert.Contains("/docs.fortinet.com/", s.Url));
    }

    [Fact]
    public void AdminAccess_HttpOnWanIsHigh_TelnetOnLanIsMedium()
    {
        var wan = Assert.Single(Of("AUD-001"));
        Assert.Equal(AuditSeverity.High, wan.Severity);
        Assert.Equal(["wan1", "http"], wan.Arguments);
        var lan = Assert.Single(Of("AUD-002"));
        Assert.Equal(["internal", "telnet"], lan.Arguments);
        Assert.Single(Of("AUD-003"));
    }

    [Fact]
    public void Policies_DuplicateShadowedDisabledAnyAndLogging()
    {
        Assert.Equal(["2", "1"], Assert.Single(Of("AUD-012")).Arguments);

        // Policy 3 is narrower than policy 1 (all/all/ALL to wan1): shadowed.
        Assert.Contains(Of("AUD-013"), f => f.Arguments[0] == "3" && f.Arguments[1] == "1");
        Assert.Equal("6", Assert.Single(Of("AUD-014")).Arguments[0]);
        Assert.Equal("4", Assert.Single(Of("AUD-020")).Arguments[0]);

        // Policy 7 allows all/all/ALL towards a VPN interface (not internet): any-any.
        Assert.Equal("7", Assert.Single(Of("AUD-010")).Arguments[0]);

        // Policy 1 and 2 go to the internet without security profiles; policy 3 has antivirus.
        var noProfiles = Of("AUD-021").Select(f => f.Arguments[0]).ToList();
        Assert.Contains("1", noProfiles);
        Assert.DoesNotContain("3", noProfiles);
    }

    [Fact]
    public void AdminsGlobalCryptoAndTime()
    {
        Assert.Single(Of("AUD-030"));
        Assert.Equal("admin", Assert.Single(Of("AUD-031")).Arguments[0]);
        Assert.Equal("admin", Assert.Single(Of("AUD-032")).Arguments[0]);
        Assert.Equal("30", Assert.Single(Of("AUD-033")).Arguments[0]);
        Assert.Equal("HTTPS 443, SSH 22", Assert.Single(Of("AUD-034")).Arguments[0]);
        Assert.Single(Of("AUD-044"));
        Assert.Equal("admin-https-ssl-versions", Assert.Single(Of("AUD-045")).Arguments[0]);
        Assert.Single(Of("AUD-050"));
        Assert.Empty(Of("AUD-051"));
    }

    [Fact]
    public void Ipsec_WeakProposalsAndGroups()
    {
        Assert.Equal(["to-branch", "3des-sha1"], Assert.Single(Of("AUD-041")).Arguments);
        Assert.Equal(["to-branch", "5"], Assert.Single(Of("AUD-043")).Arguments);
        Assert.Empty(Of("AUD-040"));
        Assert.Empty(Of("AUD-042"));
    }

    [Fact]
    public void Score_SubtractsEachFailedRuleOnce()
    {
        var expected = 100 - SiteA.FailedRules.Sum(r => AuditReport.Weight(r.Severity));
        Assert.Equal(Math.Max(0, expected), SiteA.Score);
        Assert.InRange(SiteA.Score, 0, 100);
    }

    [Fact]
    public void CleanConfig_ScoresHigh()
    {
        var doc = Tb.Parse(Tb.Header +
            "config system global\n    set admin-sport 8443\n    set admin-ssh-port 2222\nend\n" +
            "config system ntp\n    set ntpsync enable\nend\nconfig system dns\n    set primary 192.0.2.53\nend\n" +
            "config system admin\n    edit \"ops\"\n        set trusthost1 192.168.1.0 255.255.255.0\n        set two-factor fortitoken\n    next\nend\n");
        var report = AuditEngine.Run(doc);
        Assert.Empty(report.Findings);
        Assert.Equal(100, report.Score);
    }

    [Fact]
    public void MissingNtpAndDns_NotReportedForOldOrUnknownVersions()
    {
        var doc = Tb.Parse("#config-version=FGTSYN-7.2.10-FW-build1706-240101:opmode=0:vdom=0:user=admin\nconfig system global\n    set hostname \"x\"\nend\n");
        var report = AuditEngine.Run(doc);
        Assert.DoesNotContain(report.Findings, f => f.RuleId is "AUD-050" or "AUD-051");
        Assert.Contains("AUDN-002", report.Notes);
    }

    [Fact]
    public void FindingDetails_AreLocalized()
    {
        var f = Assert.Single(Of("AUD-001"));
        Assert.Equal("Interface wan1 allows http.", f.Detail(CultureInfo.GetCultureInfo("en")));
        Assert.Equal("L'interfaccia wan1 consente http.", f.Detail(CultureInfo.GetCultureInfo("it-IT")));
    }
}

public sealed class SectionDiffTests
{
    [Fact]
    public void SameFile_NoChanges() =>
        Assert.True(SectionDiff.Compare(Tb.Doc("site-a.fgtcfg"), Tb.Doc("site-a.fgtcfg")).Identical);

    [Fact]
    public void MovingAnEntry_IsNotAChange()
    {
        var a = Tb.Parse(Tb.Header + "config firewall address\n    edit \"x\"\n        set subnet 192.0.2.1 255.255.255.255\n    next\n    edit \"y\"\n        set subnet 192.0.2.2 255.255.255.255\n    next\nend\n");
        var b = Tb.Parse(Tb.Header + "config firewall address\n    edit \"y\"\n        set subnet 192.0.2.2 255.255.255.255\n    next\n    edit \"x\"\n            set subnet 192.0.2.1 255.255.255.255\n    next\nend\n");
        Assert.True(SectionDiff.Compare(a, b).Identical);
    }

    [Fact]
    public void ReportsAddedRemovedAndChangedFields()
    {
        var text = Tb.Text("site-a.fgtcfg");
        var changed = text
            .Replace("    edit \"old-nas\"\n        set subnet 192.168.10.98 255.255.255.255\n    next\n", "", StringComparison.Ordinal)
            .Replace("set admintimeout 30", "set admintimeout 5", StringComparison.Ordinal)
            .Replace("config user local\n", "config user local\n    edit \"new.user\"\n        set type password\n    next\n", StringComparison.Ordinal)
            .Replace("set psksecret ENC U2hpZnR3YWxsIHN5bnRoZXRpYyB0ZXN0IHZhbHVlIC0gbm90IGEgc2VjcmV0", "set psksecret ENC QW5vdGhlciBzeW50aGV0aWMgdmFsdWU=", StringComparison.Ordinal);
        var diff = SectionDiff.Compare(Tb.Doc("site-a.fgtcfg"), Tb.Parse(changed));

        var removed = Assert.Single(diff.Changes, c => c.Kind == SectionChangeKind.Removed);
        Assert.Equal(("firewall address", "old-nas"), (removed.Section, removed.Entry));

        var added = Assert.Single(diff.Changes, c => c.Kind == SectionChangeKind.Added);
        Assert.Equal(("user local", "new.user"), (added.Section, added.Entry));

        var global = Assert.Single(diff.Changes, c => c.Section == "system global");
        Assert.Null(global.Entry);
        Assert.Equal(new FieldChange("admintimeout", "30", "5"), Assert.Single(global.Fields));

        // Encrypted values are compared but never shown.
        var vpn = Assert.Single(diff.Changes, c => c.Section == "vpn ipsec phase1-interface");
        var psk = Assert.Single(vpn.Fields);
        Assert.Equal("ENC ••••", psk.Before);
        Assert.Equal("ENC ••••", psk.After);
        Assert.DoesNotContain(diff.Changes.SelectMany(c => c.Fields), f => (f.Before ?? "").Contains("U2hp", StringComparison.Ordinal));
    }

    [Fact]
    public void NestedBlocks_BecomeFieldPaths()
    {
        var a = Tb.Parse(Tb.Header + "config system sdwan\n    config members\n        edit 1\n            set interface \"wan1\"\n        next\n    end\nend\n");
        var b = Tb.Parse(Tb.Header + "config system sdwan\n    config members\n        edit 1\n            set interface \"wan2\"\n        next\n    end\nend\n");
        var change = Assert.Single(SectionDiff.Compare(a, b).Changes);
        Assert.Equal("config members › 1 › interface", Assert.Single(change.Fields).Field);
    }
}
