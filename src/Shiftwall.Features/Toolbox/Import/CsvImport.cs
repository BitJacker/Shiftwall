using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Shiftwall.Core.Config;

namespace Shiftwall.Features.Toolbox.Import;

public enum ImportKind
{
    Address,
    AddressGroup,
    Service,
    ServiceGroup,
    Vip,
}

/// <summary>A problem in an input row. <see cref="Row"/> is 1-based and counts the header line.</summary>
public sealed record ImportError(int Row, string Code, IReadOnlyList<string> Arguments)
{
    public string Format(CultureInfo? c = null) => ToolText.Format("Import." + Code, [Row.ToString(CultureInfo.InvariantCulture), .. Arguments], c);
}

public sealed record ImportResult(ImportKind Kind, string Script, int Objects, IReadOnlyList<ImportError> Errors)
{
    public bool Ok => Errors.Count == 0;
}

/// <summary>
/// Turns a CSV file into a FortiOS CLI script that creates objects in bulk. Nothing is sent to a device: the user
/// reviews and pastes the script.
/// </summary>
/// <remarks>
/// Field names and limits from the FortiOS 7.6.8 CLI Reference (config firewall address / addrgrp / service custom /
/// service group / vip; names up to 79 characters). Port range syntax "dst[-dst][:src[-src]]", at most 16 ranges:
/// FortiOS 6.0 CLI Reference, config firewall service custom.
/// </remarks>
public static partial class CsvImport
{
    public const int MaxNameLength = 79;
    public const int MaxPortRanges = 16;

    /// <summary>Columns of each kind (first row of the CSV). Required columns first.</summary>
    public static IReadOnlyList<string> Columns(ImportKind kind) => kind switch
    {
        ImportKind.Address => ["name", "type", "value", "interface", "comment"],
        ImportKind.AddressGroup => ["name", "members", "comment"],
        ImportKind.Service => ["name", "protocol", "ports", "icmptype", "icmpcode", "protocol-number", "category", "comment"],
        ImportKind.ServiceGroup => ["name", "members", "comment"],
        ImportKind.Vip => ["name", "extintf", "extip", "mappedip", "protocol", "extport", "mappedport", "comment"],
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    private static readonly IReadOnlyDictionary<ImportKind, string[]> Required = new Dictionary<ImportKind, string[]>
    {
        [ImportKind.Address] = ["name", "value"],
        [ImportKind.AddressGroup] = ["name", "members"],
        [ImportKind.Service] = ["name", "protocol"],
        [ImportKind.ServiceGroup] = ["name", "members"],
        [ImportKind.Vip] = ["name", "extintf", "extip", "mappedip"],
    };

    /// <summary>A template CSV with the header and one example row (RFC 5737 documentation addresses).</summary>
    public static string Template(ImportKind kind) => kind switch
    {
        ImportKind.Address => "name,type,value,interface,comment\r\nsrv-web,ipmask,192.0.2.10/32,,Web server\r\nnet-lab,ipmask,198.51.100.0/24,,\r\npool-a,iprange,192.0.2.20-192.0.2.29,,\r\nupdates,fqdn,update.example.com,,\r\n",
        ImportKind.AddressGroup => "name,members,comment\r\ngrp-servers,srv-web|pool-a,Servers\r\n",
        ImportKind.Service => "name,protocol,ports,icmptype,icmpcode,protocol-number,category,comment\r\napp-8443,tcp,8443,,,,,\r\ndns-alt,tcp/udp,5353,,,,,\r\nping-echo,icmp,,8,0,,,\r\ngre,ip,,,,47,,\r\n",
        ImportKind.ServiceGroup => "name,members,comment\r\ngrp-app,app-8443|dns-alt,\r\n",
        ImportKind.Vip => "name,extintf,extip,mappedip,protocol,extport,mappedport,comment\r\nvip-web,wan1,203.0.113.10,192.0.2.10,tcp,443,8443,Web\r\n",
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    public static ImportResult Convert(ImportKind kind, string csv)
    {
        var errors = new List<ImportError>();
        var rows = CsvReader.Parse(csv, out var separator);
        if (rows.Count == 0)
        {
            errors.Add(new ImportError(1, "Empty", []));
            return new ImportResult(kind, "", 0, errors);
        }

        var header = rows[0].Select(h => h.Trim().ToLowerInvariant()).ToList();
        foreach (var req in Required[kind])
        {
            if (!header.Contains(req)) errors.Add(new ImportError(1, "MissingColumn", [req]));
        }

        if (errors.Count > 0) return new ImportResult(kind, "", 0, errors);

        var memberSeparators = separator == ';' ? new[] { '|' } : new[] { '|', ';' };
        var sb = new StringBuilder();
        var names = new HashSet<string>(StringComparer.Ordinal);
        var count = 0;
        sb.Append("config ").Append(Table(kind)).Append('\n');
        for (var r = 1; r < rows.Count; r++)
        {
            var row = rows[r];
            if (row.All(string.IsNullOrWhiteSpace)) continue;
            string Get(string col)
            {
                var i = header.IndexOf(col);
                return i >= 0 && i < row.Count ? row[i].Trim() : "";
            }

            var line = r + 1;
            var before = errors.Count;
            var name = Get("name");
            if (name.Length == 0) errors.Add(new ImportError(line, "NameMissing", []));
            else if (name.Length > MaxNameLength) errors.Add(new ImportError(line, "NameTooLong", [name, MaxNameLength.ToString(CultureInfo.InvariantCulture)]));
            else if (!names.Add(name)) errors.Add(new ImportError(line, "NameDuplicate", [name]));

            var body = new List<string>();
            switch (kind)
            {
                case ImportKind.Address: Address(Get, line, body, errors); break;
                case ImportKind.AddressGroup:
                case ImportKind.ServiceGroup: Group(Get("members"), memberSeparators, line, body, errors); break;
                case ImportKind.Service: Service(Get, line, body, errors); break;
                case ImportKind.Vip: Vip(Get, line, body, errors); break;
            }

            var comment = Get("comment");
            if (comment.Length > 0) body.Add($"set comment {ConfigToken.Render(comment)}");
            if (errors.Count != before) continue;

            sb.Append("    edit ").Append(ConfigToken.Render(name)).Append('\n');
            foreach (var b in body) sb.Append("        ").Append(b).Append('\n');
            sb.Append("    next\n");
            count++;
        }

        sb.Append("end\n");
        return new ImportResult(kind, errors.Count == 0 ? sb.ToString() : "", count, errors);
    }

    private static string Table(ImportKind kind) => kind switch
    {
        ImportKind.Address => "firewall address",
        ImportKind.AddressGroup => "firewall addrgrp",
        ImportKind.Service => "firewall service custom",
        ImportKind.ServiceGroup => "firewall service group",
        ImportKind.Vip => "firewall vip",
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    private static void Address(Func<string, string> get, int line, List<string> body, List<ImportError> errors)
    {
        var type = get("type").ToLowerInvariant();
        if (type.Length == 0) type = "ipmask";
        var value = get("value");
        switch (type)
        {
            case "ipmask":
                if (!IpAddressRules.TryParseSubnet(value, out var ip, out var prefix)) { errors.Add(new ImportError(line, "BadSubnet", [value])); return; }
                if (!IpAddressRules.Network(ip, prefix).Equals(ip)) { errors.Add(new ImportError(line, "HostBits", [value])); return; }
                body.Add($"set subnet {ip} {IpAddressRules.Netmask(prefix)}");
                break;
            case "iprange":
                var parts = value.Split('-', StringSplitOptions.TrimEntries);
                if (parts.Length != 2 || !IpAddressRules.TryParseIPv4(parts[0], out var a) || !IpAddressRules.TryParseIPv4(parts[1], out var b) || Compare(a, b) > 0)
                {
                    errors.Add(new ImportError(line, "BadRange", [value]));
                    return;
                }

                body.Add("set type iprange");
                body.Add($"set start-ip {a}");
                body.Add($"set end-ip {b}");
                break;
            case "fqdn":
                if (!Fqdn().IsMatch(value)) { errors.Add(new ImportError(line, "BadFqdn", [value])); return; }
                body.Add("set type fqdn");
                body.Add($"set fqdn {ConfigToken.Render(value)}");
                break;
            default:
                errors.Add(new ImportError(line, "BadAddressType", [type]));
                return;
        }

        var intf = get("interface");
        if (intf.Length > 0) body.Add($"set associated-interface {ConfigToken.Render(intf)}");
    }

    private static int Compare(System.Net.IPAddress a, System.Net.IPAddress b)
    {
        var x = a.GetAddressBytes();
        var y = b.GetAddressBytes();
        for (var i = 0; i < 4; i++)
        {
            if (x[i] != y[i]) return x[i].CompareTo(y[i]);
        }

        return 0;
    }

    private static void Group(string members, char[] separators, int line, List<string> body, List<ImportError> errors)
    {
        var list = members.Split(separators, StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (list.Length == 0) { errors.Add(new ImportError(line, "NoMembers", [])); return; }
        var tooLong = list.FirstOrDefault(m => m.Length > MaxNameLength);
        if (tooLong is not null) { errors.Add(new ImportError(line, "NameTooLong", [tooLong, MaxNameLength.ToString(CultureInfo.InvariantCulture)])); return; }
        body.Add("set member " + string.Join(' ', list.Select(m => ConfigToken.Render(m))));
    }

    private static void Service(Func<string, string> get, int line, List<string> body, List<ImportError> errors)
    {
        var protocol = get("protocol").ToLowerInvariant();
        var ports = get("ports");
        switch (protocol)
        {
            case "tcp" or "udp" or "sctp" or "tcp/udp":
                var list = ports.Split([' ', ','], StringSplitOptions.RemoveEmptyEntries);
                if (list.Length == 0) { errors.Add(new ImportError(line, "NoPorts", [])); return; }
                var bad = list.FirstOrDefault(p => !PortRange(p));
                if (bad is not null) { errors.Add(new ImportError(line, "BadPort", [bad])); return; }
                var total = list.Length * (protocol == "tcp/udp" ? 2 : 1);
                if (total > MaxPortRanges) { errors.Add(new ImportError(line, "TooManyPorts", [MaxPortRanges.ToString(CultureInfo.InvariantCulture)])); return; }
                body.Add("set protocol TCP/UDP/UDP-Lite/SCTP");
                var joined = string.Join(' ', list);
                if (protocol is "tcp" or "tcp/udp") body.Add($"set tcp-portrange {joined}");
                if (protocol is "udp" or "tcp/udp") body.Add($"set udp-portrange {joined}");
                if (protocol == "sctp") body.Add($"set sctp-portrange {joined}");
                break;
            case "icmp":
                body.Add("set protocol ICMP");
                if (!OptionalByte(get("icmptype"), "icmptype", line, body, errors)) return;
                OptionalByte(get("icmpcode"), "icmpcode", line, body, errors);
                break;
            case "ip":
                var n = get("protocol-number");
                if (!int.TryParse(n, NumberStyles.None, CultureInfo.InvariantCulture, out var num) || num > 255)
                {
                    errors.Add(new ImportError(line, "BadProtocolNumber", [n]));
                    return;
                }

                body.Add("set protocol IP");
                body.Add($"set protocol-number {num}");
                break;
            default:
                errors.Add(new ImportError(line, "BadProtocol", [protocol]));
                return;
        }

        var category = get("category");
        if (category.Length > 0) body.Add($"set category {ConfigToken.Render(category)}");
    }

    private static bool OptionalByte(string v, string field, int line, List<string> body, List<ImportError> errors)
    {
        if (v.Length == 0) return true;
        if (!int.TryParse(v, NumberStyles.None, CultureInfo.InvariantCulture, out var n) || n > 255)
        {
            errors.Add(new ImportError(line, "BadNumber", [field, v]));
            return false;
        }

        body.Add($"set {field} {n}");
        return true;
    }

    private static void Vip(Func<string, string> get, int line, List<string> body, List<ImportError> errors)
    {
        var extintf = get("extintf");
        var extip = get("extip");
        var mapped = get("mappedip");
        if (!IpOrRange(extip)) { errors.Add(new ImportError(line, "BadIp", ["extip", extip])); return; }
        if (!IpOrRange(mapped)) { errors.Add(new ImportError(line, "BadIp", ["mappedip", mapped])); return; }
        body.Add($"set extintf {ConfigToken.Render(extintf)}");
        body.Add($"set extip {extip}");
        body.Add($"set mappedip {ConfigToken.Render(mapped)}");

        var extport = get("extport");
        var mappedport = get("mappedport");
        if (extport.Length == 0 && mappedport.Length == 0) return;
        var protocol = get("protocol").ToLowerInvariant();
        if (protocol.Length == 0) protocol = "tcp";
        if (protocol is not ("tcp" or "udp" or "sctp")) { errors.Add(new ImportError(line, "BadProtocol", [protocol])); return; }
        if (!SimplePorts(extport)) { errors.Add(new ImportError(line, "BadPort", [extport])); return; }
        if (mappedport.Length > 0 && !SimplePorts(mappedport)) { errors.Add(new ImportError(line, "BadPort", [mappedport])); return; }
        body.Add("set portforward enable");
        body.Add($"set protocol {protocol}");
        body.Add($"set extport {extport}");
        if (mappedport.Length > 0) body.Add($"set mappedport {mappedport}");
    }

    private static bool IpOrRange(string v)
    {
        var parts = v.Split('-', StringSplitOptions.TrimEntries);
        return parts.Length is 1 or 2 && parts.All(p => IpAddressRules.TryParseIPv4(p, out _));
    }

    private static bool Port(string p) => int.TryParse(p, NumberStyles.None, CultureInfo.InvariantCulture, out var n) && n is >= 1 and <= 65535 && p.Length <= 5;

    private static bool SimplePorts(string v)
    {
        var parts = v.Split('-');
        return parts.Length is 1 or 2 && parts.All(Port) && (parts.Length == 1 || int.Parse(parts[0], CultureInfo.InvariantCulture) <= int.Parse(parts[1], CultureInfo.InvariantCulture));
    }

    /// <summary>"dst", "dst-dst", optionally followed by ":src" or ":src-src".</summary>
    private static bool PortRange(string v)
    {
        var halves = v.Split(':');
        return halves.Length is 1 or 2 && halves.All(SimplePorts);
    }

    [GeneratedRegex(@"^(?=.{1,253}$)(\*\.)?([A-Za-z0-9]([A-Za-z0-9-]{0,61}[A-Za-z0-9])?\.)+[A-Za-z]{2,63}$")]
    private static partial Regex Fqdn();
}

/// <summary>RFC 4180 CSV reader; the separator (comma, semicolon or tab) is detected from the first line.</summary>
public static class CsvReader
{
    public static List<List<string>> Parse(string text, out char separator)
    {
        if (text.Length > 0 && text[0] == '﻿') text = text[1..];
        var firstLine = text.Split('\n', 2)[0];
        separator = new[] { ',', ';', '\t' }.OrderByDescending(c => firstLine.Count(ch => ch == c)).First();
        var rows = new List<List<string>>();
        var row = new List<string>();
        var cell = new StringBuilder();
        var quoted = false;
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (quoted)
            {
                if (c == '"')
                {
                    if (i + 1 < text.Length && text[i + 1] == '"') { cell.Append('"'); i++; }
                    else quoted = false;
                }
                else cell.Append(c);
                continue;
            }

            if (c == '"' && cell.Length == 0) quoted = true;
            else if (c == separator) { row.Add(cell.ToString()); cell.Clear(); }
            else if (c == '\n')
            {
                row.Add(cell.ToString().TrimEnd('\r'));
                cell.Clear();
                rows.Add(row);
                row = [];
            }
            else cell.Append(c);
        }

        if (cell.Length > 0 || row.Count > 0)
        {
            row.Add(cell.ToString().TrimEnd('\r'));
            rows.Add(row);
        }

        return rows;
    }
}
