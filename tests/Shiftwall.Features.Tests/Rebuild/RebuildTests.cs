using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Shiftwall.Features.Migration;
using Shiftwall.Features.Rebuild;
using Shiftwall.Features.Tests.Toolbox.TwoFactor;

namespace Shiftwall.Features.Tests.Rebuild;

public sealed partial class RebuildTests
{
    private static readonly CultureInfo En = CultureInfo.GetCultureInfo("en");

    /// <summary>Script (secrets masked) plus manual list, counts and findings, as reviewed by a person.</summary>
    internal static string Describe(RebuildResult r)
    {
        var sb = new StringBuilder();
        sb.Append(r.FullScript(maskSecrets: true));
        sb.Append("#### MANUAL\n");
        foreach (var m in r.Manual) sb.Append(CultureInfo.InvariantCulture, $"{m.Path} | {m.Object} | {m.Code} | {m.Format(En)}\n");
        sb.Append("#### COUNTS\n");
        foreach (var c in r.Counts) sb.Append(CultureInfo.InvariantCulture, $"{c.Path}: {c.Source} -> {c.Rebuilt} rebuilt, {c.Converted} converted, {c.Manual} manual\n");
        sb.Append("#### FINDINGS\n");
        foreach (var f in r.Findings) sb.Append(CultureInfo.InvariantCulture, $"{f.Severity} {f.Code} {f.Format(En)}\n");
        sb.Append("#### RULES\n");
        foreach (var a in r.Applied) sb.Append(CultureInfo.InvariantCulture, $"{a.RuleId} {a.Path} '{a.Object}' {a.Param}\n");
        return sb.ToString();
    }

    [Fact]
    public void Office110CTo40F_MatchesTheReviewedGolden()
    {
        var r = RebuildEngine.Run(RebuildFixtures.Office110C());
        TwoFactorFixtures.AssertGolden(Path.Combine("..", "Rebuild", "110c-5.2-to-40f-8.0.txt"), Describe(r));
    }

    [Fact]
    public void Office110CTo40F_IsCleanAndComplete()
    {
        var r = RebuildEngine.Run(RebuildFixtures.Office110C());

        Assert.False(r.IsBlocked, string.Join("; ", r.Findings.Where(f => f.Severity == FindingSeverity.Error).Select(f => f.Format(En))));
        Assert.True(r.LintPassed, string.Join("; ", r.Lint.Select(l => $"{l.Line} {l.Code} {l.Detail}")));
        Assert.DoesNotContain(r.Findings, f => f.Code == RebuildCodes.BrokenReference);

        // Nothing disappears silently: every source object is rebuilt, converted or listed for manual work.
        Assert.All(r.Counts, c => Assert.Equal(c.Source, c.Rebuilt + c.Converted + c.Manual));
        Assert.Contains(r.Counts, c => c.Path == "firewall policy" && c is { Source: 6, Rebuilt: 5, Converted: 1, Manual: 0 });

        var script = r.FullScript();
        Assert.DoesNotContain(" ENC ", script);
        Assert.DoesNotContain("c3ludGhldGlj", script); // base64 of "synthetic": the ENC values of the fixture
        Assert.Contains("set ip 203.0.113.10 255.255.255.248", script); // the static WAN is kept
        Assert.Contains("edit \"wan\"\n        set vdom \"root\"\n        set mode static", script);
        Assert.Contains("set protocol TCP/UDP/UDP-Lite/SCTP", script);
        Assert.Contains("set ike-version 1", script);
        Assert.DoesNotContain("ssl.root", script);
        Assert.DoesNotContain("internal", script.Replace("set comments", "", StringComparison.Ordinal), StringComparison.Ordinal);

        // The management interface is changed last, after every other section.
        var last = r.Sections.Where(s => !s.IsEmpty).OrderBy(s => s.Order).Select(s => s.Key).ToList();
        Assert.Equal(["management", "management-dhcp"], last.TakeLast(2));
    }

    [Fact]
    public void Secrets_AreNewPerRun_SafeForTheCli_AndNeverInTheMaskedOutput()
    {
        var a = RebuildEngine.Run(RebuildFixtures.Office110C());
        var b = RebuildEngine.Run(RebuildFixtures.Office110C());
        Assert.NotEmpty(a.Secrets);
        Assert.All(a.Secrets, s => Assert.Empty(CliPassword.Check(s.Password)));
        Assert.NotEqual(a.Secrets.Select(s => s.Password), b.Secrets.Select(s => s.Password));
        var masked = a.FullScript(maskSecrets: true) + Describe(a);
        Assert.All(a.Secrets, s => Assert.DoesNotContain(s.Password, masked));
        Assert.All(a.Secrets, s => Assert.Contains(s.Password, a.FullScript()));
        Assert.Contains(a.Secrets, s => s.Category == SecretCategory.VpnPsk && s.Object == "dialup_vpn");
    }

    [Fact]
    public void ProvidedPasswords_AreUsed_AndServerSecretsAreNeverGenerated()
    {
        var plan = new SecretPlan { Provided = new Dictionary<SecretCategory, string> { [SecretCategory.Admin] = "Adm1n-Nuova!2026", [SecretCategory.VpnPsk] = "Psk-Sede2-xx99!", [SecretCategory.LocalUser] = "Utente-2026%ok" } };
        var r = RebuildEngine.Run(RebuildFixtures.Office110C(plan));
        Assert.Contains("set password \"Adm1n-Nuova!2026\"", r.FullScript());
        Assert.All(r.Secrets, s => Assert.False(s.Generated));
        Assert.Contains(r.Manual, m => m.Path == "user ldap" && m.Code == RebuildCodes.SecretRequired);
    }

    [Fact]
    public void WithoutPasswords_ObjectsThatNeedThemStayOut()
    {
        var r = RebuildEngine.Run(RebuildFixtures.Office110C(new SecretPlan()));
        Assert.DoesNotContain("edit \"tecnico\"", r.FullScript());
        Assert.DoesNotContain("edit \"to-sede2\"\n        set interface", r.FullScript());
        Assert.Contains(r.Manual, m => m.Object == "tecnico" && m.Code == RebuildCodes.SecretRequired);
        // Everything that depends on the missing tunnel is out too, with the reason.
        Assert.Contains(r.Manual, m => m.Path == "firewall policy" && m.Object == "4" && m.Code == RebuildCodes.UnresolvedReference);
        Assert.False(r.IsBlocked);
        Assert.True(r.LintPassed);
    }

    [Fact]
    public void CurrentAdmin_IsNeverChangedByTheScript()
    {
        var r = RebuildEngine.Run(RebuildFixtures.Office110C());
        Assert.DoesNotContain("edit \"admin\"", r.FullScript());
        Assert.Contains(r.Manual, m => m.Object == "admin" && m.Code == RebuildCodes.CurrentAdmin);
    }

    [Fact]
    public void GuiAndTerminalFiles_PassTheLint_AndKeepTheOrder()
    {
        var r = RebuildEngine.Run(RebuildFixtures.Office110C());
        var gui = r.GuiFiles("110C to 40F");
        var cli = r.TerminalFiles();
        Assert.NotEmpty(gui);
        Assert.All(gui, f => Assert.DoesNotContain(CliLint.Check(f.Text, r.TargetSchema, allowComments: true), l => l.Severity == LintSeverity.Error));
        Assert.All(cli, f => Assert.DoesNotContain(CliLint.Check(f.Text, r.TargetSchema, allowComments: false), l => l.Severity == LintSeverity.Error));
        Assert.All(cli, f => Assert.True(f.Text.Count(c => c == '\n') <= ScriptPackager.DefaultTerminalMaxLines + 10, f.FileName));
        Assert.All(gui.Concat(cli), f => Assert.DoesNotContain('\r', f.Text));
        Assert.EndsWith("management-dhcp.txt", cli[^1].FileName, StringComparison.Ordinal);

        // Same statements in both deliverables, in the same order.
        static string Body(IEnumerable<ScriptFile> files) => string.Concat(files.Select(f => string.Concat(f.Text.Split('\n').Where(l => !l.StartsWith('#')).Select(l => l + "\n"))));
        Assert.Equal(Normalize(Body(gui)), Normalize(Body(cli)));
    }

    /// <summary>The statements that change the configuration (config/end repeated at chunk boundaries are not compared).</summary>
    private static string Normalize(string s) => string.Concat(s.Split('\n').Select(l => l.Trim()).Where(l => l.StartsWith("set ", StringComparison.Ordinal) || l.StartsWith("edit ", StringComparison.Ordinal) || l.StartsWith("delete ", StringComparison.Ordinal)).Select(l => l + "\n"));

    [Fact]
    public void Lint_RejectsWhatTheCliCannotTake()
    {
        var schema = CliSchema.Load("8.0");
        Assert.Contains(CliLint.Check("config firewall address\n    edit \"a\"\n        set subnet 10.0.0.0 255.0.0.0\n    next\n", schema, false), l => l.Code == CliLint.Unbalanced);
        Assert.Contains(CliLint.Check("config firewall address\n    edit \"a?\"\n    next\nend\n", schema, false), l => l.Code == CliLint.ForbiddenCharacter);
        Assert.Contains(CliLint.Check("config firewall address\n    edit \"a\"\n        set visibility disable\n    next\nend\n", schema, false), l => l.Code == CliLint.Schema);
        Assert.Contains(CliLint.Check("config firewall service custom\n    edit \"a\"\n        set protocol TCP/UDP/SCTP\n    next\nend\n", schema, false), l => l.Code == CliLint.Schema);
        Assert.Contains(CliLint.Check("config firewall address\r\nend\r\n", schema, false), l => l.Code == CliLint.CarriageReturn);
        Assert.Contains(CliLint.Check("# comment\nconfig system dns\nend\n", schema, false), l => l.Code == CliLint.Comment);
        Assert.Empty(CliLint.Check("# comment\nconfig system dns\n    set primary 192.0.2.1\nend\n", schema, true));
    }

    [Fact]
    public void GeneratedPasswords_FollowThePolicyAndAvoidSpecialCharacters()
    {
        var all = Enumerable.Range(0, 300).Select(_ => CliPassword.Generate()).ToList();
        Assert.Equal(all.Count, all.Distinct().Count());
        Assert.All(all, p =>
        {
            Assert.Equal(CliPassword.GeneratedLength, p.Length);
            Assert.Empty(CliPassword.Check(p));
            Assert.DoesNotContain(p, c => "?<>()#'\"\\ \t".Contains(c, StringComparison.Ordinal));
        });
        Assert.Contains(CliPassword.Problem.ForbiddenCharacter, CliPassword.Check("Abcdefgh12!?xyz"));
        Assert.Contains(CliPassword.Problem.TooShort, CliPassword.Check("Ab1!"));
    }

    [Fact]
    public void NoFixedPasswordInTheSource()
    {
        // The project is open source: a password written in the code would be known to everybody.
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "global.json"))) root = root.Parent;
        foreach (var file in Directory.GetFiles(Path.Combine(root!.FullName, "src"), "*.cs", SearchOption.AllDirectories))
        {
            var text = File.ReadAllText(file);
            Assert.DoesNotMatch(FixedPassword(), text);
        }
    }

    [GeneratedRegex("""(?i)(password|passwd|psksecret|secret)\s*=\s*"[^"{]{6,}""")]
    private static partial Regex FixedPassword();

    [Fact]
    public void StaticPlan_WritesAddressRouteAndDns_AndChecksTheGateway()
    {
        var req = RebuildFixtures.Office110C() with
        {
            Interfaces = [new InterfacePlan("wan1", "wan", AddressingMode.Static, "198.51.100.2/29", "198.51.100.1", ["192.0.2.1"]), new InterfacePlan("internal", "lan"), new InterfacePlan("wan2", "lan3")],
        };
        var r = RebuildEngine.Run(req);
        var script = r.FullScript();
        Assert.Contains("set ip 198.51.100.2 255.255.255.248", script);
        Assert.Contains("set gateway 198.51.100.1", script);

        var bad = RebuildEngine.Run(req with { Interfaces = [new InterfacePlan("wan1", "wan", AddressingMode.Static, "198.51.100.2/29", "192.0.2.1"), new InterfacePlan("internal", "lan")] });
        Assert.Contains(bad.Findings, f => f.Code == RebuildCodes.GatewayOutsideSubnet);
    }

    [Fact]
    public void UnmappedInterface_KeepsDependentObjectsOutWithTheReason()
    {
        var req = RebuildFixtures.Office110C() with { Interfaces = [new InterfacePlan("wan1", "wan"), new InterfacePlan("internal", "lan")] };
        var r = RebuildEngine.Run(req);
        Assert.Contains(r.Manual, m => m.Object == "wan2" && m.Code == RebuildCodes.InterfaceNotMapped);
        Assert.Contains(r.Manual, m => m.Path == "firewall policy" && m.Object == "6" && m.Code == RebuildCodes.UnresolvedReference);
        Assert.DoesNotContain(r.Findings, f => f.Code == RebuildCodes.BrokenReference);
        Assert.All(r.Counts, c => Assert.Equal(c.Source, c.Rebuilt + c.Converted + c.Manual));
    }

    [Fact]
    public void ManualItems_NeverContainEncValues()
    {
        var r = RebuildEngine.Run(RebuildFixtures.Office110C(new SecretPlan()));
        Assert.All(r.Manual, m => Assert.DoesNotContain("c3ludGhldGlj", m.Original));
    }

    [Theory]
    [InlineData("5.02", "5.2")]
    [InlineData("5.2.15", "5.2")]
    [InlineData("8.0.2", "8.0")]
    [InlineData("7.06", "7.6")]
    public void MajorMinor_ReadsBothHeaderStyles(string version, string expected) => Assert.Equal(expected, RebuildEngine.MajorMinor(version));
}
