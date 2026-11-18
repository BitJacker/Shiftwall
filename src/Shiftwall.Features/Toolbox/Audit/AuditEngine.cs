using System.Net;
using Shiftwall.Core.Config;
using Shiftwall.Core.Data;
using static Shiftwall.Features.Toolbox.Audit.AuditSources;
using T = Shiftwall.Features.Toolbox.ConfigTables;

namespace Shiftwall.Features.Toolbox.Audit;

/// <summary>
/// Security and best-practice audit of a FortiOS backup. Works only on what the backup states: values not present
/// in a standard backup are defaults, and a default is used only when the CLI reference documents it for the
/// backup's FortiOS version (otherwise the check is skipped and a note says so).
/// </summary>
public static class AuditEngine
{
    public static readonly IReadOnlyList<AuditRule> Rules =
    [
        new("AUD-001", AuditSeverity.High, [BpAdministrativeSettings, BpExternalAdminPorts]),
        new("AUD-002", AuditSeverity.Medium, [BpAdministrativeSettings, BpEncryptedProtocols]),
        new("AUD-003", AuditSeverity.Medium, [BpExternalAdminPorts, BpUserAccess]),
        new("AUD-010", AuditSeverity.High, [BpSecurityPolicies]),
        new("AUD-011", AuditSeverity.Medium, [BpSecurityPolicies]),
        new("AUD-012", AuditSeverity.Medium, [BpSecurityPolicies]),
        new("AUD-013", AuditSeverity.Medium, [BpSecurityPolicies]),
        new("AUD-014", AuditSeverity.Low, [BpSecurityPolicies]),
        new("AUD-020", AuditSeverity.Medium, [BpLogging, CliPolicy]),
        new("AUD-021", AuditSeverity.Medium, [BpSecurityProfiles]),
        new("AUD-030", AuditSeverity.Medium, [BpAdminPortsAndNames]),
        new("AUD-031", AuditSeverity.Low, [BpUserAccess]),
        new("AUD-032", AuditSeverity.Low, [BpUserAccess]),
        new("AUD-033", AuditSeverity.Low, [BpAdministrativeSettings, CliGlobal]),
        new("AUD-034", AuditSeverity.Low, [BpAdministrativeSettings, BpAdminPortsAndNames]),
        new("AUD-040", AuditSeverity.High, [Rfc8221, Rfc8247, BpStrongCiphers]),
        new("AUD-041", AuditSeverity.Medium, [Rfc8221, Rfc8247]),
        new("AUD-042", AuditSeverity.High, [Rfc8247]),
        new("AUD-043", AuditSeverity.Medium, [Rfc8247]),
        new("AUD-044", AuditSeverity.High, [BpStrongCiphers, CliGlobal]),
        new("AUD-045", AuditSeverity.Medium, [Rfc8996, BpAdministrativeSettings, CliGlobal, CliSslVpn]),
        new("AUD-050", AuditSeverity.Medium, [BpSystemTime, CliNtp]),
        new("AUD-051", AuditSeverity.Low, [CliDns]),
    ];

    /// <summary>First FortiOS version whose CLI reference documents the NTP/DNS defaults used by AUD-050/051.</summary>
    private static readonly FortiOsVersion DefaultsVerifiedFrom = FortiOsVersion.Parse("7.4.0");

    private static readonly HashSet<string> AdminProtocolsInsecure = ["http", "telnet"];
    private static readonly HashSet<string> AdminProtocols = ["https", "ssh", "http", "telnet", "snmp", "fgfm"];

    public static AuditReport Run(ConfigDocument doc)
    {
        var findings = new List<AuditFinding>();
        var notes = new List<string>();
        var global = doc.GlobalScope;
        var internet = InternetFacingInterfaces(doc);

        AuditInterfaces(doc, global, internet, findings);
        AuditAdmins(global, findings);
        AuditGlobal(global, findings);
        AuditTimeAndDns(doc, global, findings, notes);

        foreach (var (vdom, scope) in T.VdomScopes(doc))
        {
            var wan = WanNames(scope, internet);
            AuditPolicies(vdom, scope, wan, findings);
            AuditIpsec(vdom, scope, findings);
            AuditSslVpn(vdom, scope, findings);
        }

        notes.Add("AUDN-001");
        var score = Math.Max(0, 100 - Rules.Where(r => findings.Any(f => f.RuleId == r.Id)).Sum(r => AuditReport.Weight(r.Severity)));
        return new AuditReport(Rules, findings, score, notes);
    }

    private static AuditFinding F(string rule, string location, params string[] args) =>
        new(rule, Rules.First(r => r.Id == rule).Severity, location, args);

    private static string Loc(string? vdom, string section, string? entry = null) =>
        string.Join(" › ", new[] { vdom, section, entry }.Where(s => !string.IsNullOrEmpty(s)));

    // ---------------------------------------------------------------- interfaces

    /// <summary>
    /// Interfaces considered internet-facing: role wan, a static public IPv4 address (documentation ranges included),
    /// or PPPoE addressing.
    /// </summary>
    internal static HashSet<string> InternetFacingInterfaces(ConfigDocument doc)
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        foreach (var e in T.Entries(doc.GlobalScope, "system interface"))
        {
            if (T.Value(e, "role") == "wan" || T.Value(e, "mode") == "pppoe")
            {
                set.Add(e.Key);
                continue;
            }

            var ip = T.Values(e, "ip");
            if (ip is { Count: > 0 } && IpAddressRules.TryParseSubnet(string.Join(' ', ip), out var addr, out _) && IpAddressRules.IsInternetFacingIPv4(addr))
            {
                set.Add(e.Key);
            }
        }

        return set;
    }

    /// <summary>Names a policy can use to reach the internet in this VDOM: internet-facing interfaces, zones containing them, SD-WAN zones.</summary>
    private static HashSet<string> WanNames(ConfigContainer scope, HashSet<string> internet)
    {
        var names = new HashSet<string>(internet, StringComparer.Ordinal);
        foreach (var z in T.Entries(scope, "system zone"))
        {
            if (T.Values(z, "interface") is { } members && members.Any(internet.Contains)) names.Add(z.Key);
        }

        if (scope.Block("system sdwan") is { } sdwan)
        {
            foreach (var zone in T.Entries(sdwan, "zone")) names.Add(zone.Key);
        }

        if (scope.Block("system virtual-wan-link") is not null) names.Add("virtual-wan-link");
        return names;
    }

    private static void AuditInterfaces(ConfigDocument doc, ConfigContainer global, HashSet<string> internet, List<AuditFinding> findings)
    {
        var anyAdminUnrestricted = T.Entries(global, "system admin").Any(a => !HasTrustedHosts(a));
        var hasLocalIn = T.VdomScopes(doc).Any(s => T.Entries(s.Scope, "firewall local-in-policy").Any());

        foreach (var e in T.Entries(global, "system interface"))
        {
            var access = T.Values(e, "allowaccess") ?? [];
            var insecure = access.Where(AdminProtocolsInsecure.Contains).ToList();
            var isInternet = internet.Contains(e.Key);
            var location = Loc(null, "system interface", e.Key);
            if (insecure.Count > 0)
            {
                findings.Add(F(isInternet ? "AUD-001" : "AUD-002", location, e.Key, string.Join(", ", insecure)));
            }

            var admin = access.Where(AdminProtocols.Contains).ToList();
            if (isInternet && admin.Count > 0 && anyAdminUnrestricted && !hasLocalIn)
            {
                findings.Add(F("AUD-003", location, e.Key, string.Join(", ", admin)));
            }
        }
    }

    // ---------------------------------------------------------------- administrators and global settings

    private static bool HasTrustedHosts(EditEntry admin)
    {
        for (var i = 1; i <= 10; i++)
        {
            foreach (var field in new[] { $"trusthost{i}", $"ip6-trusthost{i}" })
            {
                var v = T.Joined(admin, field);
                if (v is not null && v is not "0.0.0.0 0.0.0.0" and not "::/0") return true;
            }
        }

        return false;
    }

    private static void AuditAdmins(ConfigContainer global, List<AuditFinding> findings)
    {
        foreach (var a in T.Entries(global, "system admin"))
        {
            var location = Loc(null, "system admin", a.Key);
            if (a.Key == "admin") findings.Add(F("AUD-030", location));
            if (!HasTrustedHosts(a)) findings.Add(F("AUD-031", location, a.Key));
            if (T.Value(a, "two-factor") is null or "disable") findings.Add(F("AUD-032", location, a.Key));
        }
    }

    private static void AuditGlobal(ConfigContainer global, List<AuditFinding> findings)
    {
        var g = global.Block("system global");
        var location = "system global";

        if (T.Value(g, "strong-crypto") == "disable") findings.Add(F("AUD-044", location));

        if (T.Values(g, "admin-https-ssl-versions") is { } versions && versions.Any(v => v is "tlsv1-0" or "tlsv1-1"))
        {
            findings.Add(F("AUD-045", location, "admin-https-ssl-versions", string.Join(' ', versions)));
        }

        if (T.Value(g, "ssl-min-proto-version") is ("SSLv3" or "TLSv1" or "TLSv1-1") and var min)
        {
            findings.Add(F("AUD-045", location, "ssl-min-proto-version", min));
        }

        if (int.TryParse(T.Value(g, "admintimeout"), out var timeout) && timeout > 10)
        {
            findings.Add(F("AUD-033", location, timeout.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        }

        var https = T.Value(g, "admin-sport") ?? "443";
        var ssh = T.Value(g, "admin-ssh-port") ?? "22";
        var defaults = new List<string>();
        if (https == "443") defaults.Add("HTTPS 443");
        if (ssh == "22") defaults.Add("SSH 22");
        if (defaults.Count > 0) findings.Add(F("AUD-034", location, string.Join(", ", defaults)));
    }

    private static void AuditTimeAndDns(ConfigDocument doc, ConfigContainer global, List<AuditFinding> findings, List<string> notes)
    {
        var ntp = global.Block("system ntp");
        var dns = global.Block("system dns");
        var version = doc.Header.Version is { } v && FortiOsVersion.TryParse(v, out var parsed) ? parsed : (FortiOsVersion?)null;
        var defaultsKnown = version is not null && version.Value.CompareTo(DefaultsVerifiedFrom) >= 0;

        var ntpsync = T.Value(ntp, "ntpsync");
        if (ntpsync == "disable" || (ntpsync is null && defaultsKnown)) findings.Add(F("AUD-050", "system ntp"));

        var primary = T.Value(dns, "primary");
        if (primary == "0.0.0.0" || (primary is null && defaultsKnown)) findings.Add(F("AUD-051", "system dns"));

        if (!defaultsKnown && (ntpsync is null || primary is null)) notes.Add("AUDN-002");
    }

    // ---------------------------------------------------------------- policies

    private sealed record PolicyInfo(EditEntry Entry, string Id, bool Enabled, string Action, Dictionary<string, HashSet<string>> Match, bool Analysable);

    private static readonly string[] MatchFields = ["srcintf", "dstintf", "srcaddr", "dstaddr", "srcaddr6", "dstaddr6", "service", "schedule", "users", "groups"];

    private static readonly string[] ProfileFields =
    [
        "av-profile", "webfilter-profile", "dnsfilter-profile", "emailfilter-profile", "dlp-profile", "file-filter-profile",
        "ips-sensor", "application-list", "voip-profile", "waf-profile", "ssh-filter-profile", "casb-profile",
        "videofilter-profile", "icap-profile", "virtual-patch-profile", "profile-group",
    ];

    private static PolicyInfo Policy(EditEntry e)
    {
        var match = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (var f in MatchFields) match[f] = new HashSet<string>(T.Values(e, f) ?? [], StringComparer.Ordinal);
        if (match["schedule"].Count == 0) match["schedule"].Add("always");

        // Policies whose matching depends on things compared here only by name are left out of duplicate/shadow analysis.
        var analysable = T.Value(e, "srcaddr-negate") != "enable" && T.Value(e, "dstaddr-negate") != "enable"
            && T.Value(e, "service-negate") != "enable" && T.Value(e, "internet-service") != "enable"
            && T.Value(e, "internet-service-src") != "enable" && T.Value(e, "ztna-status") != "enable";
        return new PolicyInfo(e, e.Key, T.Value(e, "status") != "disable", T.Value(e, "action") ?? "deny", match, analysable);
    }

    private static bool Covers(string field, HashSet<string> earlier, HashSet<string> later)
    {
        var wildcard = field switch
        {
            "srcintf" or "dstintf" => "any",
            "srcaddr" or "dstaddr" or "srcaddr6" or "dstaddr6" => "all",
            "service" => "ALL",
            "schedule" => "always",
            _ => null,
        };

        if (field is "users" or "groups")
        {
            // No users/groups on the earlier policy matches everyone.
            return earlier.Count == 0 || earlier.SetEquals(later);
        }

        if (wildcard is not null && earlier.Contains(wildcard)) return true;
        if (later.Count == 0) return earlier.Count == 0;
        return earlier.IsSupersetOf(later);
    }

    private static void AuditPolicies(string? vdom, ConfigContainer scope, HashSet<string> wan, List<AuditFinding> findings)
    {
        var policies = T.Entries(scope, "firewall policy").Select(Policy).ToList();
        for (var i = 0; i < policies.Count; i++)
        {
            var p = policies[i];
            var location = Loc(vdom, "firewall policy", p.Id);
            if (!p.Enabled)
            {
                findings.Add(F("AUD-014", location, p.Id));
                continue;
            }

            if (p.Action == "accept")
            {
                var src = p.Match["srcaddr"];
                var dst = p.Match["dstaddr"];
                var svc = p.Match["service"];
                var toInternet = p.Match["dstintf"].Count > 0 && p.Match["dstintf"].All(wan.Contains);
                if (src.Contains("all") && dst.Contains("all") && svc.Contains("ALL") && !toInternet)
                {
                    findings.Add(F("AUD-010", location, p.Id));
                }

                var anyIntf = p.Match["srcintf"].Contains("any") || p.Match["dstintf"].Contains("any");
                if (anyIntf) findings.Add(F("AUD-011", location, p.Id));

                if (T.Value(p.Entry, "logtraffic") == "disable") findings.Add(F("AUD-020", location, p.Id));

                var hasProfiles = ProfileFields.Any(f => T.Value(p.Entry, f) is not null) || T.Value(p.Entry, "utm-status") == "enable";
                if (toInternet && !hasProfiles) findings.Add(F("AUD-021", location, p.Id));
            }

            if (!p.Analysable) continue;
            for (var j = 0; j < i; j++)
            {
                var q = policies[j];
                if (!q.Enabled || !q.Analysable) continue;
                var equal = MatchFields.All(f => q.Match[f].SetEquals(p.Match[f]));
                if (equal)
                {
                    findings.Add(F("AUD-012", location, p.Id, q.Id));
                    break;
                }

                if (MatchFields.All(f => Covers(f, q.Match[f], p.Match[f])))
                {
                    findings.Add(F("AUD-013", location, p.Id, q.Id, q.Action));
                    break;
                }
            }
        }
    }

    // ---------------------------------------------------------------- VPN

    /// <summary>Splits a FortiOS proposal ("aes128-sha256", "3des-md5", "aes256gcm-prfsha384") into cipher and hash.</summary>
    internal static (string Cipher, string Hash) SplitProposal(string proposal)
    {
        var dash = proposal.IndexOf('-', StringComparison.Ordinal);
        return dash < 0 ? (proposal, "") : (proposal[..dash], proposal[(dash + 1)..]);
    }

    private static void AuditIpsec(string? vdom, ConfigContainer scope, List<AuditFinding> findings)
    {
        foreach (var path in new[] { "vpn ipsec phase1-interface", "vpn ipsec phase2-interface", "vpn ipsec phase1", "vpn ipsec phase2" })
        {
            foreach (var e in T.Entries(scope, path))
            {
                var location = Loc(vdom, path, e.Key);
                var high = new List<string>();
                var medium = new List<string>();
                foreach (var proposal in T.Values(e, "proposal") ?? [])
                {
                    var (cipher, hash) = SplitProposal(proposal);
                    if (cipher is "des" or "null" || hash is "md5" or "null" or "prfmd5") high.Add(proposal);
                    else if (cipher == "3des" || hash is "sha1" or "prfsha1") medium.Add(proposal);
                }

                if (high.Count > 0) findings.Add(F("AUD-040", location, e.Key, string.Join(' ', high)));
                if (medium.Count > 0) findings.Add(F("AUD-041", location, e.Key, string.Join(' ', medium)));

                var groups = T.Values(e, "dhgrp") ?? [];
                var badHigh = groups.Where(g => g is "1" or "22").ToList();
                var badMedium = groups.Where(g => g is "2" or "5" or "23" or "24").ToList();
                if (badHigh.Count > 0) findings.Add(F("AUD-042", location, e.Key, string.Join(' ', badHigh)));
                if (badMedium.Count > 0) findings.Add(F("AUD-043", location, e.Key, string.Join(' ', badMedium)));
            }
        }
    }

    private static void AuditSslVpn(string? vdom, ConfigContainer scope, List<AuditFinding> findings)
    {
        var ssl = scope.Block("vpn ssl settings");
        if (T.Value(ssl, "ssl-min-proto-ver") is ("tls1-0" or "tls1-1") and var min)
        {
            findings.Add(F("AUD-045", Loc(vdom, "vpn ssl settings"), "ssl-min-proto-ver", min));
        }
    }

    /// <summary>Exposed for tests: true when <paramref name="ip"/> is treated as internet-facing.</summary>
    internal static bool IsInternetFacing(string ip) => IPAddress.TryParse(ip, out var a) && IpAddressRules.IsInternetFacingIPv4(a);
}
