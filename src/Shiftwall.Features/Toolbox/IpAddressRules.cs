using System.Net;

namespace Shiftwall.Features.Toolbox;

/// <summary>Address classification used by the audit, the anonymizer and the LAN feature.</summary>
/// <remarks>
/// Ranges: RFC 1918 (private IPv4), RFC 4193 (unique local IPv6 fc00::/7), RFC 6598 (shared 100.64.0.0/10),
/// RFC 3927 (link-local 169.254.0.0/16), RFC 5737 (documentation), RFC 2544 (benchmarking 198.18.0.0/15),
/// RFC 1122 (loopback, "this network"), RFC 1112 (240.0.0.0/4 reserved), RFC 5771 (multicast).
/// </remarks>
public static class IpAddressRules
{
    /// <summary>IPv4 address (4 bytes). Checked by length so no System.Net.Sockets type is referenced.</summary>
    public static bool IsV4(IPAddress ip) => ip.GetAddressBytes().Length == 4;

    /// <summary>IPv6 address (16 bytes).</summary>
    public static bool IsV6(IPAddress ip) => ip.GetAddressBytes().Length == 16;

    /// <summary>True for RFC 1918 IPv4 and RFC 4193 IPv6 unique local addresses only.</summary>
    public static bool IsPrivate(IPAddress ip)
    {
        if (ip.IsIPv4MappedToIPv6) ip = ip.MapToIPv4();
        if (IsV4(ip))
        {
            var b = ip.GetAddressBytes();
            return b[0] == 10 || (b[0] == 172 && b[1] >= 16 && b[1] <= 31) || (b[0] == 192 && b[1] == 168);
        }

        if (IsV6(ip))
        {
            var b = ip.GetAddressBytes();
            return (b[0] & 0xFE) == 0xFC;
        }

        return false;
    }

    /// <summary>True when the IPv4 address is globally routable (not private, shared, loopback, link-local, documentation, benchmarking, multicast or reserved).</summary>
    public static bool IsPublicIPv4(IPAddress ip)
    {
        if (!IsV4(ip)) return false;
        var b = ip.GetAddressBytes();
        if (IsPrivate(ip)) return false;
        if (b[0] == 0 || b[0] == 127) return false;                       // "this network", loopback
        if (b[0] == 100 && b[1] >= 64 && b[1] <= 127) return false;       // shared address space
        if (b[0] == 169 && b[1] == 254) return false;                     // link-local
        if (b[0] == 192 && b[1] == 0 && b[2] == 2) return false;          // TEST-NET-1
        if (b[0] == 198 && b[1] == 51 && b[2] == 100) return false;       // TEST-NET-2
        if (b[0] == 203 && b[1] == 0 && b[2] == 113) return false;        // TEST-NET-3
        if (b[0] == 198 && (b[1] == 18 || b[1] == 19)) return false;      // benchmarking
        if (b[0] >= 224) return false;                                    // multicast, reserved, broadcast
        return true;
    }

    /// <summary>
    /// Like <see cref="IsPublicIPv4"/> but documentation ranges (RFC 5737) count as public: synthetic examples use
    /// them in place of real public addresses, so the audit treats them as such.
    /// </summary>
    public static bool IsInternetFacingIPv4(IPAddress ip)
    {
        if (!IsV4(ip)) return false;
        var b = ip.GetAddressBytes();
        var documentation = (b[0] == 192 && b[1] == 0 && b[2] == 2) || (b[0] == 198 && b[1] == 51 && b[2] == 100) || (b[0] == 203 && b[1] == 0 && b[2] == 113);
        return documentation || IsPublicIPv4(ip);
    }

    /// <summary>True for a contiguous IPv4 netmask such as 255.255.255.0 (including 0.0.0.0 and 255.255.255.255).</summary>
    public static bool IsNetmask(IPAddress ip)
    {
        if (!IsV4(ip)) return false;
        var b = ip.GetAddressBytes();
        var v = ((uint)b[0] << 24) | ((uint)b[1] << 16) | ((uint)b[2] << 8) | b[3];
        var inverted = ~v;
        return (inverted & (inverted + 1)) == 0;
    }

    /// <summary>Parses a dotted-quad IPv4 address strictly (four decimal octets, no leading zeros beyond "0").</summary>
    public static bool TryParseIPv4(string s, out IPAddress ip)
    {
        ip = IPAddress.None;
        var parts = s.Split('.');
        if (parts.Length != 4) return false;
        var bytes = new byte[4];
        for (var i = 0; i < 4; i++)
        {
            var p = parts[i];
            if (p.Length is 0 or > 3 || !p.All(char.IsAsciiDigit) || (p.Length > 1 && p[0] == '0')) return false;
            var n = int.Parse(p, System.Globalization.CultureInfo.InvariantCulture);
            if (n > 255) return false;
            bytes[i] = (byte)n;
        }

        ip = new IPAddress(bytes);
        return true;
    }

    /// <summary>Prefix length of a contiguous netmask.</summary>
    public static int PrefixLength(IPAddress mask)
    {
        var b = mask.GetAddressBytes();
        var bits = 0;
        foreach (var x in b) bits += System.Numerics.BitOperations.PopCount(x);
        return bits;
    }

    /// <summary>Parses "a.b.c.d/nn" or "a.b.c.d m.m.m.m" into address and prefix.</summary>
    public static bool TryParseSubnet(string text, out IPAddress address, out int prefix)
    {
        address = IPAddress.None;
        prefix = 0;
        text = text.Trim();
        string ipPart;
        string? maskPart;
        var slash = text.IndexOf('/');
        if (slash >= 0)
        {
            ipPart = text[..slash];
            maskPart = text[(slash + 1)..];
        }
        else
        {
            var sp = text.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
            ipPart = sp[0];
            maskPart = sp.Length > 1 ? sp[1] : null;
            if (sp.Length > 2) return false;
        }

        if (!TryParseIPv4(ipPart, out address)) return false;
        if (maskPart is null)
        {
            prefix = 32;
            return true;
        }

        if (maskPart.All(char.IsAsciiDigit) && maskPart.Length is > 0 and <= 2)
        {
            prefix = int.Parse(maskPart, System.Globalization.CultureInfo.InvariantCulture);
            return prefix <= 32;
        }

        if (!TryParseIPv4(maskPart, out var mask) || !IsNetmask(mask)) return false;
        prefix = PrefixLength(mask);
        return true;
    }

    /// <summary>Dotted netmask for a prefix length.</summary>
    public static string Netmask(int prefix)
    {
        var v = prefix == 0 ? 0u : uint.MaxValue << (32 - prefix);
        return $"{v >> 24}.{(v >> 16) & 255}.{(v >> 8) & 255}.{v & 255}";
    }

    /// <summary>Network address (host bits cleared).</summary>
    public static IPAddress Network(IPAddress address, int prefix)
    {
        var b = address.GetAddressBytes();
        var v = ((uint)b[0] << 24) | ((uint)b[1] << 16) | ((uint)b[2] << 8) | b[3];
        var mask = prefix == 0 ? 0u : uint.MaxValue << (32 - prefix);
        v &= mask;
        return new IPAddress([(byte)(v >> 24), (byte)(v >> 16), (byte)(v >> 8), (byte)v]);
    }
}
