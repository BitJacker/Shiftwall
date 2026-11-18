using System.Globalization;
using System.Text;
using Shiftwall.Core.Config;

namespace Shiftwall.Features.Toolbox.Snippets;

public sealed record VlanInput(string Name, string ParentInterface, int VlanId, string IpCidr, string Vdom = "root", string AllowAccess = "ping", string? Alias = null, string Role = "lan");

public sealed record PolicyInput(string Name, string SrcIntf, string DstIntf, string SrcAddr, string DstAddr, string Service, string Action, bool Nat, string Schedule = "always", string LogTraffic = "all", string? Comment = null);

public sealed record VipInput(string Name, string ExtIntf, string ExtIp, string MappedIp, string? Protocol = null, string? ExtPort = null, string? MappedPort = null, string? Comment = null);

public sealed record StaticRouteInput(string DestinationCidr, string Gateway, string Device, int Distance = 10, string? Comment = null);

public sealed record LocalUserInput(string Name, bool Enabled = true);

/// <summary>A snippet or the errors that prevented it. Error codes are ToolboxMessages keys "Snippet.{Code}".</summary>
public sealed record SnippetResult(string Script, IReadOnlyList<(string Code, string[] Args)> Errors)
{
    public bool Ok => Errors.Count == 0;

    public IEnumerable<string> FormatErrors(CultureInfo? c = null) => Errors.Select(e => ToolText.Format("Snippet." + e.Code, e.Args, c));
}

/// <summary>
/// Generates FortiOS CLI for common operations. Field names: FortiOS 7.6.8 CLI Reference (system interface,
/// firewall policy, firewall vip, router static, user local). <c>edit 0</c> lets FortiOS pick the next free ID
/// ("Technical Tip: Using the 'edit 0' option…", community.fortinet.com/t5/FortiGate/ta-p/190761).
/// </summary>
public static class SnippetGenerator
{
    private static string Q(string v) => ConfigToken.Render(v);

    private static string Csv(string v) => v.IndexOfAny([',', '"', '\n', '\r']) >= 0 ? "\"" + v.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"" : v;

    public static SnippetResult Vlan(VlanInput i)
    {
        var errors = new List<(string, string[])>();
        Name(i.Name, 15, errors);
        if (string.IsNullOrWhiteSpace(i.ParentInterface)) errors.Add(("Required", ["interface"]));
        if (i.VlanId is < 1 or > 4094) errors.Add(("VlanId", []));
        if (i.Alias is { Length: > 25 }) errors.Add(("NameTooLong", ["25"]));
        if (!IpAddressRules.TryParseSubnet(i.IpCidr, out var ip, out var prefix) || prefix is 0 or 32) errors.Add(("BadIp", [i.IpCidr]));
        if (errors.Count > 0) return new SnippetResult("", errors);

        var sb = new StringBuilder("config system interface\n");
        sb.Append("    edit ").Append(Q(i.Name)).Append('\n')
          .Append("        set vdom ").Append(Q(i.Vdom)).Append('\n')
          .Append("        set ip ").Append(ip).Append(' ').Append(IpAddressRules.Netmask(prefix)).Append('\n');
        if (!string.IsNullOrWhiteSpace(i.AllowAccess)) sb.Append("        set allowaccess ").Append(i.AllowAccess.Trim()).Append('\n');
        if (!string.IsNullOrWhiteSpace(i.Alias)) sb.Append("        set alias ").Append(Q(i.Alias)).Append('\n');
        sb.Append("        set role ").Append(i.Role).Append('\n')
          .Append("        set interface ").Append(Q(i.ParentInterface)).Append('\n')
          .Append("        set vlanid ").Append(i.VlanId.ToString(CultureInfo.InvariantCulture)).Append('\n')
          .Append("    next\nend\n");
        return new SnippetResult(sb.ToString(), errors);
    }

    public static SnippetResult Policy(PolicyInput i)
    {
        var errors = new List<(string, string[])>();
        Name(i.Name, 35, errors);
        foreach (var (field, v) in new[] { ("srcintf", i.SrcIntf), ("dstintf", i.DstIntf), ("srcaddr", i.SrcAddr), ("dstaddr", i.DstAddr), ("service", i.Service) })
        {
            if (string.IsNullOrWhiteSpace(v)) errors.Add(("Required", [field]));
        }

        if (i.Action is not ("accept" or "deny")) errors.Add(("Required", ["action"]));
        if (errors.Count > 0) return new SnippetResult("", errors);

        static string List(string v) => string.Join(' ', v.Split([',', ';', '|'], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).Select(Q));
        var sb = new StringBuilder("config firewall policy\n    edit 0\n");
        sb.Append("        set name ").Append(Q(i.Name)).Append('\n')
          .Append("        set srcintf ").Append(List(i.SrcIntf)).Append('\n')
          .Append("        set dstintf ").Append(List(i.DstIntf)).Append('\n')
          .Append("        set action ").Append(i.Action).Append('\n')
          .Append("        set srcaddr ").Append(List(i.SrcAddr)).Append('\n')
          .Append("        set dstaddr ").Append(List(i.DstAddr)).Append('\n')
          .Append("        set schedule ").Append(Q(i.Schedule)).Append('\n')
          .Append("        set service ").Append(List(i.Service)).Append('\n')
          .Append("        set logtraffic ").Append(i.LogTraffic).Append('\n');
        if (i.Action == "accept" && i.Nat) sb.Append("        set nat enable\n");
        if (!string.IsNullOrWhiteSpace(i.Comment)) sb.Append("        set comments ").Append(Q(i.Comment)).Append('\n');
        sb.Append("    next\nend\n");
        return new SnippetResult(sb.ToString(), errors);
    }

    public static SnippetResult Vip(VipInput i)
    {
        var import = Import.CsvImport.Convert(Import.ImportKind.Vip,
            "name,extintf,extip,mappedip,protocol,extport,mappedport,comment\n" +
            string.Join(',', new[] { i.Name, i.ExtIntf, i.ExtIp, i.MappedIp, i.Protocol ?? "", i.ExtPort ?? "", i.MappedPort ?? "", i.Comment ?? "" }.Select(Csv)));
        return import.Ok
            ? new SnippetResult(import.Script, [])
            : new SnippetResult("", [.. import.Errors.Select(e => ("Import", new[] { e.Format() }))]);
    }

    public static SnippetResult StaticRoute(StaticRouteInput i)
    {
        var errors = new List<(string, string[])>();
        if (!IpAddressRules.TryParseSubnet(i.DestinationCidr, out var dst, out var prefix)) errors.Add(("BadIp", [i.DestinationCidr]));
        else if (!IpAddressRules.Network(dst, prefix).Equals(dst)) errors.Add(("HostBits", [i.DestinationCidr]));
        if (!IpAddressRules.TryParseIPv4(i.Gateway, out var gw)) errors.Add(("BadIp", [i.Gateway]));
        if (string.IsNullOrWhiteSpace(i.Device)) errors.Add(("Required", ["device"]));
        if (i.Distance is < 1 or > 255) errors.Add(("Distance", []));
        if (errors.Count > 0) return new SnippetResult("", errors);

        var sb = new StringBuilder("config router static\n    edit 0\n");
        sb.Append("        set dst ").Append(dst).Append(' ').Append(IpAddressRules.Netmask(prefix)).Append('\n')
          .Append("        set gateway ").Append(gw).Append('\n')
          .Append("        set device ").Append(Q(i.Device)).Append('\n')
          .Append("        set distance ").Append(i.Distance.ToString(CultureInfo.InvariantCulture)).Append('\n');
        if (!string.IsNullOrWhiteSpace(i.Comment)) sb.Append("        set comment ").Append(Q(i.Comment)).Append('\n');
        sb.Append("    next\nend\n");
        return new SnippetResult(sb.ToString(), errors);
    }

    /// <summary>The password is never asked: the script carries a placeholder the user replaces on the device.</summary>
    public static SnippetResult LocalUser(LocalUserInput i)
    {
        var errors = new List<(string, string[])>();
        Name(i.Name, 64, errors);
        if (errors.Count > 0) return new SnippetResult("", errors);
        var sb = new StringBuilder("config user local\n");
        sb.Append("    edit ").Append(Q(i.Name)).Append('\n')
          .Append("        set type password\n")
          .Append("        set passwd <PASSWORD>\n");
        if (!i.Enabled) sb.Append("        set status disable\n");
        sb.Append("    next\nend\n");
        return new SnippetResult(sb.ToString(), errors);
    }

    private static void Name(string name, int max, List<(string, string[])> errors)
    {
        if (string.IsNullOrWhiteSpace(name)) errors.Add(("Required", ["name"]));
        else if (name.Length > max) errors.Add(("NameTooLong", [max.ToString(CultureInfo.InvariantCulture)]));
    }
}
