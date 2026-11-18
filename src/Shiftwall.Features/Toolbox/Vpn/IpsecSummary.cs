using System.Globalization;
using Shiftwall.Core.Config;
using T = Shiftwall.Features.Toolbox.ConfigTables;

namespace Shiftwall.Features.Toolbox.Vpn;

/// <summary>Phase 2 selector of a tunnel. Values are as written in the backup; null means "not set" (default).</summary>
public sealed record Phase2Info(string Name, string? Proposal, string? DhGroups, string? Pfs, string? KeyLifeSeconds, string Source, string Destination);

/// <summary>A route-based (interface mode) IPsec tunnel. Null fields are not set in the backup (FortiOS default applies).</summary>
public sealed record TunnelInfo(
    string? Vdom,
    string Name,
    string? Interface,
    string? Type,
    string? RemoteGateway,
    string? IkeVersion,
    string? Proposal,
    string? DhGroups,
    string? KeyLife,
    string? AuthMethod,
    IReadOnlyList<Phase2Info> Phase2);

public enum VpnCheckResult
{
    Match,
    Mismatch,

    /// <summary>At least one side relies on the FortiOS default, which is not compared.</summary>
    Unknown,
}

/// <summary>One parameter compared between the two ends of a tunnel.</summary>
public sealed record VpnCheck(string Parameter, string? Local, string? Remote, VpnCheckResult Result);

public sealed record TunnelComparison(TunnelInfo Local, TunnelInfo Remote, IReadOnlyList<VpnCheck> Checks)
{
    public bool HasMismatch => Checks.Any(c => c.Result == VpnCheckResult.Mismatch);
}

/// <summary>
/// Tabulates phase 1 / phase 2 of every interface-mode IPsec tunnel and compares the two ends of a tunnel taken
/// from two backups (proposals, DH groups, lifetimes, IKE version and mirrored selectors).
/// </summary>
/// <remarks>
/// Fields: FortiOS 7.6.8 CLI Reference, config vpn ipsec phase1-interface / phase2-interface. Defaults of these
/// fields change between FortiOS versions, so a field that is not set is never assumed: the comparison says "unknown".
/// Pre-shared keys are never read.
/// </remarks>
public static class IpsecSummary
{
    public static IReadOnlyList<TunnelInfo> Tunnels(ConfigDocument doc)
    {
        var list = new List<TunnelInfo>();
        foreach (var (vdom, scope) in T.VdomScopes(doc))
        {
            var addresses = AddressMap(scope);
            var p2 = T.Entries(scope, "vpn ipsec phase2-interface").ToList();
            foreach (var p1 in T.Entries(scope, "vpn ipsec phase1-interface"))
            {
                var phase2 = p2.Where(e => T.Value(e, "phase1name") == p1.Key).Select(e => new Phase2Info(
                    e.Key,
                    T.Joined(e, "proposal"),
                    T.Joined(e, "dhgrp"),
                    T.Value(e, "pfs"),
                    T.Value(e, "keylifeseconds"),
                    Selector(e, "src", addresses),
                    Selector(e, "dst", addresses))).ToList();
                list.Add(new TunnelInfo(
                    vdom, p1.Key, T.Value(p1, "interface"), T.Value(p1, "type"), T.Value(p1, "remote-gw"),
                    T.Value(p1, "ike-version"), T.Joined(p1, "proposal"), T.Joined(p1, "dhgrp"), T.Value(p1, "keylife"),
                    T.Value(p1, "authmethod"), phase2));
            }
        }

        return list;
    }

    /// <summary>Address object name → "subnet" text, used to resolve name-based selectors.</summary>
    private static Dictionary<string, string> AddressMap(ConfigContainer scope)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var e in T.Entries(scope, "firewall address"))
        {
            var type = T.Value(e, "type") ?? "ipmask";
            if (type == "ipmask" && T.Values(e, "subnet") is { } s && IpAddressRules.TryParseSubnet(string.Join(' ', s), out var ip, out var prefix))
            {
                map[e.Key] = $"{IpAddressRules.Network(ip, prefix)}/{prefix}";
            }
            else if (type == "iprange")
            {
                map[e.Key] = $"{T.Value(e, "start-ip")}-{T.Value(e, "end-ip")}";
            }
        }

        return map;
    }

    /// <summary>Selector text: "10.0.0.0/24", "a-b", the resolved address name, or "0.0.0.0/0" when not set.</summary>
    private static string Selector(EditEntry e, string side, Dictionary<string, string> addresses)
    {
        var type = T.Value(e, $"{side}-addr-type") ?? "subnet";
        switch (type)
        {
            case "subnet":
                var subnet = T.Values(e, $"{side}-subnet");
                if (subnet is null) return "0.0.0.0/0";
                return IpAddressRules.TryParseSubnet(string.Join(' ', subnet), out var ip, out var prefix) ? $"{IpAddressRules.Network(ip, prefix)}/{prefix}" : string.Join(' ', subnet);
            case "ip":
                return $"{T.Value(e, $"{side}-start-ip")}/32";
            case "range":
                return $"{T.Value(e, $"{side}-start-ip")}-{T.Value(e, $"{side}-end-ip")}";
            case "name":
                var name = T.Value(e, $"{side}-name") ?? "";
                return addresses.TryGetValue(name, out var resolved) ? resolved : name;
            default:
                return type;
        }
    }

    /// <summary>
    /// Pairs tunnels of two sites: a local tunnel whose remote gateway is an interface address of the remote site
    /// (and vice versa when both are static), or with the same name.
    /// </summary>
    public static IReadOnlyList<(TunnelInfo Local, TunnelInfo Remote)> Pair(ConfigDocument local, ConfigDocument remote)
    {
        var localTunnels = Tunnels(local);
        var remoteTunnels = Tunnels(remote);
        var localIps = InterfaceIps(local);
        var remoteIps = InterfaceIps(remote);
        var pairs = new List<(TunnelInfo, TunnelInfo)>();
        var used = new HashSet<TunnelInfo>();
        foreach (var l in localTunnels)
        {
            var match = remoteTunnels.FirstOrDefault(r => !used.Contains(r)
                && l.RemoteGateway is not null && remoteIps.Contains(l.RemoteGateway)
                && (r.RemoteGateway is null || localIps.Contains(r.RemoteGateway)))
                ?? remoteTunnels.FirstOrDefault(r => !used.Contains(r) && r.Name == l.Name);
            if (match is null) continue;
            used.Add(match);
            pairs.Add((l, match));
        }

        return pairs;
    }

    private static HashSet<string> InterfaceIps(ConfigDocument doc)
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        foreach (var e in T.Entries(doc.GlobalScope, "system interface"))
        {
            if (T.Values(e, "ip") is { Count: > 0 } ip && IpAddressRules.TryParseSubnet(string.Join(' ', ip), out var a, out _)) set.Add(a.ToString());
            foreach (var sec in T.Entries(e, "secondaryip"))
            {
                if (T.Values(sec, "ip") is { Count: > 0 } sip && IpAddressRules.TryParseSubnet(string.Join(' ', sip), out var b, out _)) set.Add(b.ToString());
            }
        }

        return set;
    }

    public static TunnelComparison Compare(TunnelInfo local, TunnelInfo remote)
    {
        var checks = new List<VpnCheck>
        {
            Same("ike-version", local.IkeVersion, remote.IkeVersion),
            Overlap("phase1 proposal", local.Proposal, remote.Proposal),
            Overlap("phase1 dhgrp", local.DhGroups, remote.DhGroups),
            Same("phase1 keylife", local.KeyLife, remote.KeyLife),
        };

        // Phase 2: each local selector must have a mirrored remote selector (local src = remote dst and vice versa).
        foreach (var lp in local.Phase2)
        {
            var rp = remote.Phase2.FirstOrDefault(r => r.Source == lp.Destination && r.Destination == lp.Source);
            var label = string.Format(CultureInfo.InvariantCulture, "phase2 {0}", lp.Name);
            if (rp is null)
            {
                checks.Add(new VpnCheck(label + " selectors", $"{lp.Source} → {lp.Destination}", null, VpnCheckResult.Mismatch));
                continue;
            }

            checks.Add(new VpnCheck(label + " selectors", $"{lp.Source} → {lp.Destination}", $"{rp.Source} → {rp.Destination}", VpnCheckResult.Match));
            checks.Add(Overlap(label + " proposal", lp.Proposal, rp.Proposal));
            checks.Add(Same(label + " pfs", lp.Pfs, rp.Pfs));
            if (lp.Pfs != "disable" && rp.Pfs != "disable") checks.Add(Overlap(label + " dhgrp", lp.DhGroups, rp.DhGroups));
            checks.Add(Same(label + " keylifeseconds", lp.KeyLifeSeconds, rp.KeyLifeSeconds));
        }

        foreach (var rp in remote.Phase2.Where(r => !local.Phase2.Any(l => l.Source == r.Destination && l.Destination == r.Source)))
        {
            checks.Add(new VpnCheck($"phase2 {rp.Name} selectors", null, $"{rp.Source} → {rp.Destination}", VpnCheckResult.Mismatch));
        }

        return new TunnelComparison(local, remote, checks);
    }

    private static VpnCheck Same(string parameter, string? a, string? b) => new(parameter, a, b,
        a is null || b is null ? VpnCheckResult.Unknown
        : a == b ? VpnCheckResult.Match : VpnCheckResult.Mismatch);

    /// <summary>Lists of proposals / DH groups must share at least one value.</summary>
    private static VpnCheck Overlap(string parameter, string? a, string? b)
    {
        if (a is null || b is null) return new VpnCheck(parameter, a, b, VpnCheckResult.Unknown);
        var sa = a.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var sb = b.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return new VpnCheck(parameter, a, b, sa.Intersect(sb, StringComparer.Ordinal).Any() ? VpnCheckResult.Match : VpnCheckResult.Mismatch);
    }
}
