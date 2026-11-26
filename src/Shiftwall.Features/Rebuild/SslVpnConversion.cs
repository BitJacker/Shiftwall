using System.Globalization;
using System.Net;
using Shiftwall.Core.Config;
using Shiftwall.Features.Migration;

namespace Shiftwall.Features.Rebuild;

/// <summary>The SSL VPN tunnel-mode configuration of the source, read for the conversion to IPsec dial-up.</summary>
internal sealed class SslVpnInfo
{
    public bool Present => Policies.Count > 0;

    public List<EditEntry> Policies { get; } = [];

    public List<string> ListenInterfaces { get; } = [];

    public List<string> Pools { get; } = [];

    public List<string> NameServers { get; } = [];

    public bool Split { get; private set; }

    private readonly HashSet<string> _sslAddresses = new(StringComparer.Ordinal);

    public bool IsSslObject(string path, string name) => path == "firewall address" && _sslAddresses.Contains(name);

    public static SslVpnInfo Read(ConfigDocument doc)
    {
        var info = new SslVpnInfo();
        foreach (var e in doc.Blocks.Where(b => b.Path == "firewall policy").SelectMany(b => b.Entries))
        {
            if (e.Field("srcintf")?.Values.Contains("ssl.root") == true) info.Policies.Add(e);
        }

        if (doc.Block("vpn ssl settings") is { } s)
        {
            info.ListenInterfaces.AddRange(s.Field("source-interface")?.Values ?? []);
            info.Pools.AddRange(s.Field("tunnel-ip-pools")?.Values ?? []);
            foreach (var d in new[] { "dns-server1", "dns-server2" })
            {
                if (s.Field(d)?.Values.FirstOrDefault() is { } ip && ip != "0.0.0.0") info.NameServers.Add(ip);
            }
        }

        foreach (var p in doc.Blocks.Where(b => b.Path == "vpn ssl web portal").SelectMany(b => b.Entries))
        {
            if (p.Field("split-tunneling")?.Values.FirstOrDefault() == "enable") info.Split = true;
        }

        foreach (var a in doc.Blocks.Where(b => b.Path == "firewall address").SelectMany(b => b.Entries))
        {
            if (a.Field("associated-interface")?.Values.FirstOrDefault() == "ssl.root") info._sslAddresses.Add(a.Key);
        }

        return info;
    }
}

public sealed partial class RebuildEngine
{
    /// <summary>Converted SSL VPN policies, emitted in place of the original ones (same id, same position).</summary>
    private readonly Dictionary<EditEntry, CliEntry> _convertedPolicies = [];

    /// <summary>
    /// SSL VPN tunnel mode → IPsec dial-up for FortiClient (IKEv2 + EAP), following the CLI of the FortiOS 8.0.2
    /// Administration Guide, "FortiClient as dialup client", and the "SSL VPN to IPsec VPN migration" guide.
    /// SSL VPN tunnel mode is no longer supported on any model from FortiOS 7.6.3 (Release Notes 7.6.3), and SSL VPN
    /// web mode is not available on 2 GB RAM models (Release Notes 8.0.2).
    /// </summary>
    private void ConvertSslVpn(ConfigDocument doc, SslVpnInfo ssl)
    {
        if (!_opt.ConvertSslVpn)
        {
            _findings.Add(MigrationFinding.Warning(RebuildCodes.SslVpnNotConverted));
            return;
        }

        var name = _opt.DialupTunnelName;
        string? Fail(string code, params string[] args)
        {
            _manual.Add(Item("vpn ssl settings", "SSL VPN", code, args, "", _rules.Rules.First(r => r.Id == "E001")));
            foreach (var p in ssl.Policies) _manual.Add(Item("firewall policy", p.Key, RebuildCodes.SslVpnPolicyNotConverted, [p.Key], Mask(p.ToText()), null));
            Count("firewall policy", converted: -ssl.Policies.Count, manual: ssl.Policies.Count);
            return null;
        }

        var listen = ssl.ListenInterfaces.Select(i => _ifMap.GetValueOrDefault(i, i)).FirstOrDefault(i => _known[Ref.Interface].Contains(i));
        if (listen is null)
        {
            Fail(RebuildCodes.SslVpnNoListenInterface, string.Join(" ", ssl.ListenInterfaces));
            return;
        }

        var groups = ssl.Policies.SelectMany(p => p.Field("groups")?.Values ?? []).Distinct(StringComparer.Ordinal).ToList();
        var users = ssl.Policies.SelectMany(p => p.Field("users")?.Values ?? []).Distinct(StringComparer.Ordinal).ToList();
        if (groups.Count != 1 || users.Count > 0)
        {
            Fail(RebuildCodes.SslVpnGroups, string.Join(", ", groups.Concat(users)));
            return;
        }

        if (!_known[Ref.Group].Contains(groups[0]))
        {
            Fail(RebuildCodes.UnresolvedReference, "groups", groups[0]);
            return;
        }

        if (!TryPoolRange(doc, ssl, out var start, out var end))
        {
            Fail(RebuildCodes.SslVpnNoPool, string.Join(" ", ssl.Pools));
            return;
        }

        var psk = Password(SecretCategory.VpnPsk);
        if (psk is null)
        {
            Fail(RebuildCodes.SecretRequired, "psksecret", CategoryName(SecretCategory.VpnPsk));
            return;
        }

        var dstaddrs = ssl.Policies.SelectMany(p => p.Field("dstaddr")?.Values ?? []).Distinct(StringComparer.Ordinal).ToList();
        if (dstaddrs.FirstOrDefault(a => !_known[Ref.Address].Contains(a) && !_known[Ref.Vip].Contains(a)) is { } missing)
        {
            Fail(RebuildCodes.UnresolvedReference, "dstaddr", missing);
            return;
        }

        var rangeName = name + "_range";
        var splitName = name + "_split";
        var split = ssl.Split && !dstaddrs.Contains("all");

        var range = new CliEntry(rangeName);
        Checked("firewall address", range, "type", ["iprange"]);
        Checked("firewall address", range, "start-ip", [start]);
        Checked("firewall address", range, "end-ip", [end]);
        BlockIn("ipsec", "firewall address").Entries.Add(range);
        _known[Ref.Address].Add(rangeName);
        if (split)
        {
            var grp = new CliEntry(splitName);
            Checked("firewall addrgrp", grp, "member", dstaddrs);
            BlockIn("ipsec", "firewall addrgrp").Entries.Add(grp);
            _known[Ref.Address].Add(splitName);
        }

        // Values as in "FortiClient as dialup client" (Administration Guide 8.0.2).
        var p1 = new CliEntry(name);
        Checked("vpn ipsec phase1-interface", p1, "type", ["dynamic"]);
        Checked("vpn ipsec phase1-interface", p1, "interface", [listen]);
        Checked("vpn ipsec phase1-interface", p1, "ike-version", ["2"]);
        Checked("vpn ipsec phase1-interface", p1, "peertype", ["any"]);
        Checked("vpn ipsec phase1-interface", p1, "net-device", ["disable"]);
        Checked("vpn ipsec phase1-interface", p1, "mode-cfg", ["enable"]);
        Checked("vpn ipsec phase1-interface", p1, "proposal", ["aes128-sha256", "aes256-sha256", "aes128gcm-prfsha256", "aes256gcm-prfsha384", "chacha20poly1305-prfsha256"]);
        Checked("vpn ipsec phase1-interface", p1, "dhgrp", ["20", "21"]);
        Checked("vpn ipsec phase1-interface", p1, "eap", ["enable"]);
        Checked("vpn ipsec phase1-interface", p1, "eap-identity", ["send-request"]);
        Checked("vpn ipsec phase1-interface", p1, "wizard-type", ["dialup-forticlient"]);
        Checked("vpn ipsec phase1-interface", p1, "authusrgrp", [groups[0]]);
        Checked("vpn ipsec phase1-interface", p1, "assign-ip-from", ["name"]);
        if (ssl.NameServers.Count > 0)
        {
            Checked("vpn ipsec phase1-interface", p1, "dns-mode", ["manual"]);
            Checked("vpn ipsec phase1-interface", p1, "ipv4-dns-server1", [ssl.NameServers[0]]);
            if (ssl.NameServers.Count > 1) Checked("vpn ipsec phase1-interface", p1, "ipv4-dns-server2", [ssl.NameServers[1]]);
        }
        else
        {
            Checked("vpn ipsec phase1-interface", p1, "dns-mode", ["auto"]);
        }

        if (split) Checked("vpn ipsec phase1-interface", p1, "ipv4-split-include", [splitName]);
        Checked("vpn ipsec phase1-interface", p1, "ipv4-name", [rangeName]);
        Checked("vpn ipsec phase1-interface", p1, "save-password", ["enable"]);
        p1.Sets.Add(new CliSet("psksecret", [psk], true, Secret: true));
        _secrets.Add(new SecretAssignment(SecretCategory.VpnPsk, "vpn ipsec phase1-interface", name, "psksecret", psk, !_req.Secrets.Provided.ContainsKey(SecretCategory.VpnPsk)));
        BlockIn("ipsec", "vpn ipsec phase1-interface").Entries.Add(p1);

        var p2 = new CliEntry(name);
        Checked("vpn ipsec phase2-interface", p2, "phase1name", [name]);
        Checked("vpn ipsec phase2-interface", p2, "proposal", ["aes128-sha256", "aes256-sha256", "aes128gcm", "aes256gcm", "chacha20poly1305"]);
        Checked("vpn ipsec phase2-interface", p2, "dhgrp", ["20", "21"]);
        BlockIn("ipsec", "vpn ipsec phase2-interface").Entries.Add(p2);
        _known[Ref.Interface].Add(name);
        _known[Ref.Phase1].Add(name);

        foreach (var p in ssl.Policies)
        {
            var dstintf = (p.Field("dstintf")?.Values ?? []).Select(i => _ifMap.GetValueOrDefault(i, i)).ToList();
            if (dstintf.Count == 0 || dstintf.Any(i => !_known[Ref.Interface].Contains(i)))
            {
                _manual.Add(Item("firewall policy", p.Key, RebuildCodes.SslVpnPolicyNotConverted, [p.Key], Mask(p.ToText()), null));
                Count("firewall policy", converted: -1, manual: 1);
                continue;
            }

            var e = new CliEntry(p.Key);
            Checked("firewall policy", e, "name", [Truncate($"{name} {p.Key}", 35)]);
            Checked("firewall policy", e, "srcintf", [name]);
            Checked("firewall policy", e, "dstintf", dstintf);
            Checked("firewall policy", e, "action", ["accept"]);
            Checked("firewall policy", e, "srcaddr", [rangeName]);
            Checked("firewall policy", e, "dstaddr", p.Field("dstaddr")?.Values ?? ["all"]);
            Checked("firewall policy", e, "schedule", [p.Field("schedule")?.Values.FirstOrDefault() ?? "always"]);
            Checked("firewall policy", e, "service", p.Field("service")?.Values ?? ["ALL"]);
            if (p.Field("nat")?.Values.FirstOrDefault() == "enable") Checked("firewall policy", e, "nat", ["enable"]);
            if (p.Field("logtraffic") is { } log) Checked("firewall policy", e, "logtraffic", log.Values);
            _convertedPolicies[p] = e;
        }

        _findings.Add(MigrationFinding.Warning(RebuildCodes.SslVpnConverted, name, groups[0], start + "-" + end));
        _findings.Add(MigrationFinding.Warning(RebuildCodes.ForticlientChecklist, name));
    }

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max];

    /// <summary>Adds a set line built by Shiftwall itself, after checking it on the target schema.</summary>
    private void Checked(string path, CliEntry e, string param, IReadOnlyList<string> values)
    {
        var p = _target.Find(path)?.Params.GetValueOrDefault(param);
        if (p is null || CliSchema.Check(p, values) is not null)
        {
            throw new InvalidOperationException($"Generated line not valid on FortiOS {_target.Version}: {path} {param}");
        }

        e.Sets.Add(new CliSet(param, values, IsQuoted(p)));
    }

    private static bool TryPoolRange(ConfigDocument doc, SslVpnInfo ssl, out string start, out string end)
    {
        start = end = "";
        foreach (var pool in ssl.Pools)
        {
            var a = doc.Blocks.Where(b => b.Path == "firewall address").SelectMany(b => b.Entries).FirstOrDefault(x => x.Key == pool);
            if (a is null) continue;
            var type = a.Field("type")?.Values.FirstOrDefault() ?? "ipmask";
            if (type == "iprange" && a.Field("start-ip")?.Values.FirstOrDefault() is { } s && a.Field("end-ip")?.Values.FirstOrDefault() is { } e)
            {
                start = s;
                end = e;
                return true;
            }

            if (type == "ipmask" && a.Field("subnet")?.Values is { Count: 2 } sub && TryParseAddress(sub[0] + " " + sub[1], out var ip, out var prefix) && prefix is >= 8 and <= 30)
            {
                var b = ip.GetAddressBytes();
                var n = (uint)(b[0] << 24 | b[1] << 16 | b[2] << 8 | b[3]) & (uint.MaxValue << (32 - prefix));
                start = ToIp(n + 1);
                end = ToIp(n + (uint)((1L << (32 - prefix)) - 2));
                return true;
            }
        }

        return false;
    }

    private static string ToIp(uint n) => new IPAddress([(byte)(n >> 24), (byte)(n >> 16), (byte)(n >> 8), (byte)n]).ToString();
}
