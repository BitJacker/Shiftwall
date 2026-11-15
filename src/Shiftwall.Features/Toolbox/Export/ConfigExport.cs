using System.Globalization;
using Shiftwall.Core.Config;
using T = Shiftwall.Features.Toolbox.ConfigTables;

namespace Shiftwall.Features.Toolbox.Export;

/// <summary>
/// Builds documentation tables (policies, addresses, groups, services, VIPs, schedules) from a backup.
/// Fields absent from a standard backup are shown with their default only where the FortiOS 7.6.8 CLI Reference
/// documents it (policy status enable, action deny, nat disable, logtraffic utm; address type ipmask); otherwise empty.
/// </summary>
public static class ConfigExport
{
    private static readonly string[] Profiles =
    [
        "av-profile", "webfilter-profile", "dnsfilter-profile", "emailfilter-profile", "dlp-profile", "file-filter-profile",
        "ips-sensor", "application-list", "voip-profile", "waf-profile", "ssh-filter-profile", "videofilter-profile",
        "casb-profile", "icap-profile", "virtual-patch-profile", "profile-group", "ssl-ssh-profile",
    ];

    public static IReadOnlyList<ExportTable> Build(ConfigDocument doc, CultureInfo? culture = null)
    {
        string H(string key) => ToolText.Get("Export.Col." + key, culture);
        var multi = doc.IsMultiVdom;
        var scopes = T.VdomScopes(doc).ToList();

        List<IReadOnlyList<string>> Rows(string path, Func<EditEntry, string[]> map)
        {
            var rows = new List<IReadOnlyList<string>>();
            foreach (var (vdom, scope) in scopes)
            {
                foreach (var e in T.Entries(scope, path))
                {
                    var cells = map(e);
                    rows.Add(multi ? [vdom ?? "", .. cells] : cells);
                }
            }

            return rows;
        }

        string[] Cols(params string[] keys) => multi ? [H("Vdom"), .. keys.Select(H)] : [.. keys.Select(H)];
        static string J(EditEntry e, string f) => T.Display(T.Values(e, f));

        var seq = 0;
        var policies = new ExportTable(ToolText.Get("Export.Sheet.Policies", culture),
            Cols("Seq", "Id", "Name", "Status", "SrcIntf", "DstIntf", "SrcAddr", "DstAddr", "Service", "Schedule", "Action", "Nat", "Users", "Profiles", "Log", "Comments"),
            Rows("firewall policy", e =>
            {
                seq++;
                var users = string.Join(' ', new[] { J(e, "users"), J(e, "groups") }.Where(s => s.Length > 0));
                var profiles = string.Join("; ", Profiles.Where(p => T.Value(e, p) is not null).Select(p => $"{p}={T.Value(e, p)}"));
                var src = string.Join(' ', new[] { J(e, "srcaddr"), J(e, "srcaddr6") }.Where(s => s.Length > 0));
                var dst = string.Join(' ', new[] { J(e, "dstaddr"), J(e, "dstaddr6") }.Where(s => s.Length > 0));
                return
                [
                    seq.ToString(CultureInfo.InvariantCulture), e.Key, J(e, "name"), T.Value(e, "status") ?? "enable",
                    J(e, "srcintf"), J(e, "dstintf"), src, dst, J(e, "service"), J(e, "schedule"),
                    T.Value(e, "action") ?? "deny", T.Value(e, "nat") ?? "disable", users, profiles,
                    T.Value(e, "logtraffic") ?? "utm", J(e, "comments"),
                ];
            }));

        var addresses = new ExportTable(ToolText.Get("Export.Sheet.Addresses", culture),
            Cols("Name", "Type", "Value", "Interface", "Comment"),
            Rows("firewall address", e =>
            {
                var type = T.Value(e, "type") ?? "ipmask";
                var value = type switch
                {
                    "ipmask" => Cidr(T.Values(e, "subnet")),
                    "iprange" => $"{T.Value(e, "start-ip")}-{T.Value(e, "end-ip")}",
                    "fqdn" => T.Value(e, "fqdn") ?? "",
                    "geography" => T.Value(e, "country") ?? "",
                    "wildcard" => J(e, "wildcard"),
                    "interface-subnet" => J(e, "interface"),
                    _ => "",
                };
                return [e.Key, type, value, J(e, "associated-interface"), J(e, "comment")];
            }));

        var address6 = new ExportTable(ToolText.Get("Export.Sheet.Addresses6", culture),
            Cols("Name", "Type", "Value", "Comment"),
            Rows("firewall address6", e => [e.Key, T.Value(e, "type") ?? "", J(e, "ip6") is { Length: > 0 } ip6 ? ip6 : J(e, "fqdn"), J(e, "comment")]));

        var groups = new ExportTable(ToolText.Get("Export.Sheet.AddressGroups", culture),
            Cols("Name", "Members", "Comment"),
            Rows("firewall addrgrp", e => [e.Key, J(e, "member"), J(e, "comment")]));

        var services = new ExportTable(ToolText.Get("Export.Sheet.Services", culture),
            Cols("Name", "Protocol", "Tcp", "Udp", "Sctp", "Icmp", "Category", "Comment"),
            Rows("firewall service custom", e =>
            [
                e.Key, T.Value(e, "protocol") ?? "TCP/UDP/UDP-Lite/SCTP", J(e, "tcp-portrange"), J(e, "udp-portrange"), J(e, "sctp-portrange"),
                string.Join('/', new[] { T.Value(e, "icmptype"), T.Value(e, "icmpcode") }.Where(s => s is not null)), J(e, "category"), J(e, "comment"),
            ]));

        var serviceGroups = new ExportTable(ToolText.Get("Export.Sheet.ServiceGroups", culture),
            Cols("Name", "Members", "Comment"),
            Rows("firewall service group", e => [e.Key, J(e, "member"), J(e, "comment")]));

        var vips = new ExportTable(ToolText.Get("Export.Sheet.Vips", culture),
            Cols("Name", "ExtIntf", "ExtIp", "MappedIp", "PortForward", "Protocol", "ExtPort", "MappedPort", "Comment"),
            Rows("firewall vip", e =>
            [
                e.Key, J(e, "extintf"), J(e, "extip"), J(e, "mappedip"), T.Value(e, "portforward") ?? "disable",
                T.Value(e, "portforward") == "enable" ? T.Value(e, "protocol") ?? "tcp" : "", J(e, "extport"), J(e, "mappedport"), J(e, "comment"),
            ]));

        var schedules = new ExportTable(ToolText.Get("Export.Sheet.Schedules", culture),
            Cols("Name", "Type", "Days", "Start", "End"),
            [
                .. Rows("firewall schedule recurring", e => [e.Key, "recurring", J(e, "day"), J(e, "start"), J(e, "end")]),
                .. Rows("firewall schedule onetime", e => [e.Key, "onetime", "", J(e, "start"), J(e, "end")]),
            ]);

        return [policies, addresses, address6, groups, services, serviceGroups, vips, schedules];
    }

    private static string Cidr(IReadOnlyList<string>? subnet)
    {
        if (subnet is null) return "";
        var text = string.Join(' ', subnet);
        return IpAddressRules.TryParseSubnet(text, out var ip, out var prefix) ? $"{ip}/{prefix}" : text;
    }
}
