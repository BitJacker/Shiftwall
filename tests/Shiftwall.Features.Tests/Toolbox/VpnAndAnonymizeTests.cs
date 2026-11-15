using System.Security.Cryptography;
using System.Text;
using Shiftwall.Core.Config;
using Shiftwall.Features.Toolbox.Anonymize;
using Shiftwall.Features.Toolbox.Vpn;

namespace Shiftwall.Features.Tests.Toolbox;

public sealed class VpnTests
{
    [Fact]
    public void Summary_TabulatesPhase1AndPhase2()
    {
        var t = Assert.Single(IpsecSummary.Tunnels(Tb.Doc("site-a.fgtcfg")));
        Assert.Equal("to-branch", t.Name);
        Assert.Equal("198.51.100.20", t.RemoteGateway);
        Assert.Equal("aes256-sha256 3des-sha1", t.Proposal);
        Assert.Equal("14 5", t.DhGroups);
        Assert.Null(t.KeyLife);
        var p2 = Assert.Single(t.Phase2);
        Assert.Equal(("192.168.10.0/24", "192.168.20.0/24"), (p2.Source, p2.Destination));
    }

    [Fact]
    public void NameSelectors_AreResolvedThroughAddressObjects()
    {
        var t = Assert.Single(IpsecSummary.Tunnels(Tb.Doc("site-b.fgtcfg")));
        var p2 = Assert.Single(t.Phase2);
        Assert.Equal(("192.168.20.0/24", "192.168.10.0/24"), (p2.Source, p2.Destination));
    }

    [Fact]
    public void Pairing_ByGatewayAddresses_AndComparison()
    {
        var pair = Assert.Single(IpsecSummary.Pair(Tb.Doc("site-a.fgtcfg"), Tb.Doc("site-b.fgtcfg")));
        Assert.Equal(("to-branch", "to-hq"), (pair.Local.Name, pair.Remote.Name));

        var cmp = IpsecSummary.Compare(pair.Local, pair.Remote);
        VpnCheckResult R(string p) => cmp.Checks.Single(c => c.Parameter == p).Result;
        Assert.Equal(VpnCheckResult.Match, R("ike-version"));
        Assert.Equal(VpnCheckResult.Match, R("phase1 proposal"));
        Assert.Equal(VpnCheckResult.Match, R("phase1 dhgrp"));
        Assert.Equal(VpnCheckResult.Unknown, R("phase1 keylife"));
        Assert.Equal(VpnCheckResult.Match, R("phase2 to-branch-p2 selectors"));
        Assert.Equal(VpnCheckResult.Mismatch, R("phase2 to-branch-p2 proposal"));
        Assert.Equal(VpnCheckResult.Mismatch, R("phase2 to-branch-p2 keylifeseconds"));
        Assert.True(cmp.HasMismatch);
    }

    [Fact]
    public void UnmatchedSelectors_AreMismatches()
    {
        var a = IpsecSummary.Tunnels(Tb.Doc("site-a.fgtcfg"))[0];
        var b = IpsecSummary.Tunnels(Tb.Doc("site-b.fgtcfg"))[0] with { Phase2 = [] };
        var cmp = IpsecSummary.Compare(a, b);
        Assert.Contains(cmp.Checks, c => c.Parameter == "phase2 to-branch-p2 selectors" && c.Result == VpnCheckResult.Mismatch && c.Remote is null);
    }
}

public sealed class AnonymizeTests
{
    private static (string Original, AnonymizeResult Result) Run(AnonymizeOptions? options = null)
    {
        var original = Tb.Text("site-a.fgtcfg");
        return (original, Anonymizer.Anonymize(ConfigParser.Parse(original), options ?? new AnonymizeOptions()));
    }

    [Fact]
    public void MasksPublicIpsNamesCommentsAndSecrets()
    {
        var (_, r) = Run();
        var text = r.Text;
        foreach (var leak in new[] { "203.0.113.10", "203.0.113.11", "198.51.100.20", "mrossi", "giulia.bianchi", "mario.rossi@example.com", "hq-fw01", "updates.example.com", "Server web gestionale", "ACME", "Fibra Uffici Milano", "U2hpZnR3YWxs" })
        {
            Assert.DoesNotContain(leak, text);
        }

        // Kept: private addresses (default), netmasks, structure and object names.
        Assert.Contains("set ip 192.168.10.1 255.255.255.0", text);
        Assert.Contains("edit \"net-lan\"", text);
        Assert.Contains("set password ENC anon-secret-", text);
        Assert.Contains(":user=anon-user-1", text);
        Assert.Equal(Tb.Text("site-a.fgtcfg").Split('\n').Length, text.Split('\n').Length);
        Assert.DoesNotContain(ConfigParser.Parse(text).Diagnostics, d => d.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void SameValue_GetsSameAlias()
    {
        var (_, r) = Run();
        var alias = r.Map.Entries.Single(e => e.Original == "203.0.113.10").Alias;
        Assert.StartsWith("198.18.", alias);
        Assert.Contains($"set ip {alias} 255.255.255.0", r.Text);
        Assert.Single(r.Map.Entries, e => e.Original == "mrossi");
        Assert.Single(r.Map.Entries, e => e.Original == "U2hpZnR3YWxsIHN5bnRoZXRpYyB0ZXN0IHZhbHVlIC0gbm90IGEgc2VjcmV0");
    }

    [Fact]
    public void PrivateIps_OptionallyMasked()
    {
        var (_, r) = Run(new AnonymizeOptions(PrivateIps: true));
        Assert.DoesNotContain("192.168.10.1 ", r.Text);
        Assert.Contains("255.255.255.0", r.Text);
        Assert.Contains(r.Map.Entries, e => e.Category == AnonCategory.PrivateIp && e.Alias.StartsWith("100.64.", StringComparison.Ordinal));
    }

    [Fact]
    public void Restore_GivesBackTheOriginal()
    {
        var (original, r) = Run(new AnonymizeOptions(PrivateIps: true));
        var restored = Anonymizer.Restore(ConfigParser.Parse(r.Text), r.Map);
        Assert.Equal(original, restored);
    }

    [Fact]
    public void Map_RoundTripsThroughEncryption()
    {
        var (_, r) = Run();
        var file = MapProtector.Encrypt(r.Map.ToJsonBytes(), "correct horse battery", iterations: 100_000);
        Assert.Equal("SWMAP"u8.ToArray(), file[..5]);
        Assert.DoesNotContain("mrossi", Encoding.UTF8.GetString(file));
        var back = AnonymizationMap.FromJsonBytes(MapProtector.Decrypt(file, "correct horse battery"));
        Assert.Equal(r.Map.Entries, back.Entries);
    }

    [Fact]
    public void Map_WrongPasswordOrTampering_Fails()
    {
        var file = MapProtector.Encrypt("{\"schema_version\":1,\"entries\":[]}"u8, "correct horse battery", iterations: 100_000);
        Assert.ThrowsAny<CryptographicException>(() => MapProtector.Decrypt(file, "wrong password!!"));
        file[^1] ^= 1;
        Assert.ThrowsAny<CryptographicException>(() => MapProtector.Decrypt(file, "correct horse battery"));
        Assert.Throws<InvalidDataException>(() => MapProtector.Decrypt("not a map at all, sorry"u8, "correct horse battery"));
    }

    [Fact]
    public void Map_RejectsShortPasswords() =>
        Assert.Throws<ArgumentException>(() => MapProtector.Encrypt("{}"u8, "short"));

    [Fact]
    public void DefaultIterations_FollowOwasp() => Assert.Equal(600_000, MapProtector.Iterations);
}
