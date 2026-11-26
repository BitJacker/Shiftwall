using System.Text;
using Shiftwall.Core.Config;
using Shiftwall.Features.Rebuild;

namespace Shiftwall.Features.Tests.Rebuild;

/// <summary>Rules and secret handling on small synthetic 5.2 configurations.</summary>
public sealed class RebuildEdgeTests
{
    private const string Header = "#config-version=FG110C-5.02-FW-build688-150722:opmode=0:vdom=0:user=admin\n#buildno=0688\n#global_vdom=1\n";

    private const string Interfaces = """
        config system interface
            edit "wan1"
                set vdom "root"
                set ip 203.0.113.10 255.255.255.248
                set type physical
            next
            edit "internal"
                set vdom "root"
                set ip 192.168.10.1 255.255.255.0
                set allowaccess ping https ssh
                set type physical
            next
        end
        config firewall address
            edit "all"
            next
        end
        config firewall service custom
            edit "ALL"
                set protocol IP
            next
        end
        config firewall schedule recurring
            edit "always"
                set day sunday monday tuesday wednesday thursday friday saturday
            next
        end

        """;

    private static RebuildResult Run(string body, SecretPlan? secrets = null)
    {
        var doc = ConfigInspector.Inspect(Encoding.UTF8.GetBytes(Header + Interfaces + body.Replace("\r\n", "\n", StringComparison.Ordinal))).Document!;
        return RebuildEngine.Run(new RebuildRequest(doc, "5.02", RebuildFixtures.Target40F(),
            [new InterfacePlan("wan1", "wan"), new InterfacePlan("internal", "lan")], secrets ?? new SecretPlan { Generate = true },
            new RebuildOptions(ManagementInterface: "lan")));
    }

    [Fact]
    public void MaskedPasswords_AreNeverWrittenAsPasswords()
    {
        var r = Run("""
            config system admin
                edit "tecnico"
                    set accprofile "super_admin"
                    set vdom "root"
                    set password FortinetPasswordMask
                next
            end
            config user local
                edit "u1"
                    set type password
                    set passwd FortinetPasswordMask
                next
            end

            """);
        var script = r.FullScript();
        Assert.DoesNotContain(ConfigInspector.PasswordMaskToken, script);
        Assert.Equal(2, r.Secrets.Count);
        Assert.All(r.Secrets, s => Assert.True(s.Generated));

        var none = Run("""
            config user local
                edit "u1"
                    set type password
                    set passwd FortinetPasswordMask
                next
            end

            """, new SecretPlan());
        Assert.DoesNotContain(ConfigInspector.PasswordMaskToken, none.FullScript());
        Assert.Contains(none.Manual, m => m.Object == "u1" && m.Code == RebuildCodes.SecretRequired);
    }

    [Fact]
    public void ClearTextSecrets_AreKeptButMaskedEverywhere()
    {
        var r = Run("""
            config user radius
                edit "nps"
                    set server "192.168.10.6"
                    set secret plain-radius-secret
                next
            end

            """);
        Assert.Contains("set secret \"plain-radius-secret\"", r.FullScript());
        Assert.DoesNotContain("plain-radius-secret", r.FullScript(maskSecrets: true));
        Assert.DoesNotContain("plain-radius-secret", RebuildEngine.Mask("    set secret plain-radius-secret"));
        Assert.Contains(r.Secrets, s => s.Object == "nps" && s.Password == "plain-radius-secret");
        Assert.Contains(r.Findings, f => f.Code == RebuildCodes.RadiusMessageAuthenticator);
    }

    [Fact]
    public void DenyPolicies_KeepNotMatchingVips_AcceptPoliciesAreUntouched()
    {
        var r = Run("""
            config firewall policy
                edit 1
                    set srcintf "wan1"
                    set dstintf "internal"
                    set srcaddr "all"
                    set dstaddr "all"
                    set schedule "always"
                    set service "ALL"
                next
                edit 2
                    set srcintf "internal"
                    set dstintf "wan1"
                    set srcaddr "all"
                    set dstaddr "all"
                    set action accept
                    set schedule "always"
                    set service "ALL"
                next
            end

            """);
        Assert.Contains(r.Applied, a => a.RuleId == "D008" && a.Object == "1");
        Assert.DoesNotContain(r.Applied, a => a.RuleId == "D008" && a.Object == "2");
        Assert.True(r.LintPassed);
    }

    [Fact]
    public void Ldaps_KeepsTheOldCertificateBehaviour_PlainLdapIsUntouched()
    {
        var r = Run("""
            config user ldap
                edit "ad-ssl"
                    set server "192.168.10.5"
                    set secure ldaps
                next
                edit "ad"
                    set server "192.168.10.5"
                next
            end

            """);
        Assert.Contains(r.Applied, a => a.RuleId == "D009" && a.Object == "ad-ssl");
        Assert.DoesNotContain(r.Applied, a => a.RuleId == "D009" && a.Object == "ad");
    }

    [Fact]
    public void ValueRules_MatchOnlyTheirValues()
    {
        var r = Run("""
            config vpn ipsec phase1-interface
                edit "t-enable"
                    set interface "wan1"
                    set remote-gw 198.51.100.1
                    set dpd enable
                    set psksecret ENC c3ludGhldGljLXBzaw==
                next
                edit "t-disable"
                    set interface "wan1"
                    set remote-gw 198.51.100.2
                    set dpd disable
                    set psksecret ENC c3ludGhldGljLXBzaw==
                next
            end

            """);
        Assert.Contains(r.Manual, m => m.Object == "t-enable" && m.RuleId == "M001");
        Assert.Contains("set dpd disable", r.FullScript());
        Assert.DoesNotContain("set dpd enable", r.FullScript());
    }

    [Fact]
    public void RemoveWithEquivalent_AndSkippedTables_AreReported()
    {
        var r = Run("""
            config firewall policy
                edit 3
                    set srcintf "internal"
                    set dstintf "wan1"
                    set srcaddr "all"
                    set dstaddr "all"
                    set action accept
                    set schedule "always"
                    set service "ALL"
                    set dlp-sensor "custom-dlp"
                next
            end
            config system admin
                edit "tecnico"
                    set accprofile "super_admin"
                    set vdom "root"
                    set password ENC c3ludGhldGljLWFkbWlu
                    config dashboard
                        edit 1
                            set widget-type sysinfo
                        next
                    end
                next
            end

            """);
        Assert.Contains(r.Manual, m => m.Object == "3" && (m.RuleId == "E002" || m.Code == RebuildCodes.ProfilesNotMigrated));
        Assert.DoesNotContain("dlp-sensor", r.FullScript());
        Assert.Contains(r.Applied, a => a.RuleId == "S004" && a.Object == "tecnico");
        Assert.DoesNotContain(r.Manual, m => m.Object == "tecnico");
        Assert.DoesNotContain("dashboard", r.FullScript());
        Assert.True(r.LintPassed);
    }
}
