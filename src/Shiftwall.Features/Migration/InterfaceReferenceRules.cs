namespace Shiftwall.Features.Migration;

/// <summary>
/// Where FortiOS configuration holds interface names. Verified against the FortiOS 7.6.8 CLI Reference
/// (docs.fortinet.com/document/fortigate/7.6.8/cli-reference), parameter tables of: firewall policy,
/// firewall shaping-policy, firewall proxy-policy, firewall multicast-policy, firewall local-in-policy(6),
/// firewall DoS-policy, firewall vip, firewall address, firewall ippool, firewall sniffer, router static,
/// router policy, router ospf, router rip, router bgp, router multicast, system interface, system zone,
/// system switch-interface, system virtual-switch, system ha, system sdwan, system dhcp server, system dhcp6 server,
/// system dns, system ntp, system gre-tunnel, system ipip-tunnel, vpn ipsec phase1-interface, vpn ssl settings,
/// log syslogd setting (checked 2026-11-15).
/// </summary>
/// <remarks>
/// Fields not listed here are never renamed. A value equal to an interface name found in any other field is reported
/// as a "possible reference" for manual review instead (see <see cref="InterfaceReferenceFinder"/>).
/// </remarks>
public static class InterfaceReferenceRules
{
    /// <summary>Fields whose values are interface names wherever they appear.</summary>
    private static readonly HashSet<string> AnywhereFields = new(StringComparer.Ordinal)
    {
        "srcintf",               // firewall policy / shaping / proxy / multicast policy, sdwan duplication
        "dstintf",
        "interface",             // zone, dhcp server, ipsec phase1-interface, sdwan members, address, dns, ntp, syslogd, bgp neighbor, ospf-interface, ...
        "intf",                  // firewall local-in-policy(6)
        "extintf",               // firewall vip
        "srcintf-filter",        // firewall vip
        "associated-interface",  // firewall address, firewall ippool
        "input-device",          // router policy, sdwan service
        "output-device",         // router policy
        "passive-interface",     // router ospf, router rip
        "source-interface",      // vpn ssl settings
        "source-ip-interface",   // log syslogd setting
        "dhcp-relay-interface",  // system interface
        "ip6-upstream-interface",// system interface > ipv6
        "upstream-interface",    // system dhcp6 server
        "fail-alert-interfaces", // system sdwan
        "span-source-port",      // system switch-interface
    };

    /// <summary>Fields whose values are interface names only inside a given block (last path element).</summary>
    private static readonly Dictionary<string, HashSet<string>> ScopedFields = new(StringComparer.Ordinal)
    {
        ["router static"] = ["device"],
        ["router static6"] = ["device"],
        ["system interface"] = ["member"],          // aggregate / redundant members
        ["system switch-interface"] = ["member"],
        ["system ha"] = ["monitor", "hbdev", "backup-hbdev", "pingserver-monitor-interface", "session-sync-dev", "auto-virtual-mac-interface"],
        ["vcluster"] = ["monitor", "pingserver-monitor-interface"], // system ha > vcluster
    };

    /// <summary>Blocks whose <c>edit</c> keys are interface names (path chain suffixes).</summary>
    private static readonly string[][] KeyedBlocks =
    [
        ["system interface"],              // definitions
        ["system virtual-switch", "port"], // "Physical interface name."
        ["router rip", "interface"],
        ["router multicast", "interface"],
    ];

    /// <summary>Free-text fields: never treated as references, never reported.</summary>
    private static readonly HashSet<string> FreeTextFields = new(StringComparer.Ordinal)
    {
        "comment", "comments", "description", "alias", "buffer", "certificate", "private-key", "csr",
        "password", "passwd", "psksecret", "psksecret-remote", "key", "secret", "uuid",
    };

    public static bool IsInterfaceField(IReadOnlyList<string> blockChain, string field)
    {
        if (AnywhereFields.Contains(field)) return true;
        return blockChain.Count > 0
               && ScopedFields.TryGetValue(blockChain[^1], out var set)
               && set.Contains(field);
    }

    public static bool IsInterfaceKeyedBlock(IReadOnlyList<string> blockChain) =>
        KeyedBlocks.Any(k => blockChain.Count >= k.Length && blockChain.Skip(blockChain.Count - k.Length).SequenceEqual(k, StringComparer.Ordinal));

    /// <summary>The block whose entries define interfaces.</summary>
    public static bool IsDefinitionBlock(IReadOnlyList<string> blockChain) =>
        blockChain.Count > 0 && blockChain[^1] == "system interface";

    public static bool IsFreeText(string field) => FreeTextFields.Contains(field);
}
