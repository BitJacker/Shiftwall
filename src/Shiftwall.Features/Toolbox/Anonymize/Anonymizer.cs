using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Shiftwall.Core.Config;

namespace Shiftwall.Features.Toolbox.Anonymize;

public enum AnonCategory
{
    PublicIp,
    PrivateIp,
    Hostname,
    User,
    Email,
    Comment,
    Secret,
}

public sealed record AnonymizeOptions(bool PrivateIps = false, bool Hostnames = true, bool Users = true, bool Comments = true, bool Emails = true);

/// <summary>One replacement. The map is kept only locally, encrypted with the user's password.</summary>
public sealed record MapEntry(AnonCategory Category, string Original, string Alias);

public sealed record AnonymizationMap(int SchemaVersion, IReadOnlyList<MapEntry> Entries)
{
    public byte[] ToJsonBytes() => JsonSerializer.SerializeToUtf8Bytes(this, AnonymizeJson.Default.AnonymizationMap);

    public static AnonymizationMap FromJsonBytes(ReadOnlySpan<byte> json) =>
        JsonSerializer.Deserialize(json, AnonymizeJson.Default.AnonymizationMap) ?? throw new InvalidDataException("Empty map.");
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower, UseStringEnumConverter = true)]
[JsonSerializable(typeof(AnonymizationMap))]
internal sealed partial class AnonymizeJson : JsonSerializerContext;

public sealed record AnonymizeResult(string Text, AnonymizationMap Map)
{
    public int Count(AnonCategory c) => Map.Entries.Count(e => e.Category == c);
}

/// <summary>
/// Masks public IP addresses (optionally private ones), hostnames/FQDNs, user names, e-mail addresses, comments,
/// encrypted values (<c>ENC</c>), plain passwords and PEM blocks, so a configuration can be shared. The same original
/// always gets the same alias, so references stay consistent. <see cref="Restore"/> reverses it with the map.
/// </summary>
/// <remarks>
/// Aliases: public IPv4 → 198.18.0.0/15 (RFC 2544 benchmarking), private IPv4 → 100.64.0.0/10 (RFC 6598),
/// IPv6 → 2001:db8::/32 (RFC 3849), names → "anon-…". Encrypted values are replaced, never decrypted. Netmasks are kept.
/// The output is meant to be read by people: it cannot be restored on a FortiGate.
/// </remarks>
public static partial class Anonymizer
{
    private static readonly HashSet<string> CommentFields = ["comment", "comments", "description", "alias", "replacemsg-group-comment"];
    private static readonly HashSet<string> HostFields = ["hostname", "fqdn", "domain", "ddns-domain", "server-name", "sni", "common-name"];
    private static readonly HashSet<string> UserFields = ["username", "user", "users", "ldap-username", "auth-user", "admin-user"];
    private static readonly HashSet<string> UserTables = ["user local", "system admin", "system api-user"];
    private static readonly HashSet<string> PlainSecretFields = ["passwd", "password", "psksecret", "psksecret-remote", "secret", "key", "api-key", "auth-pwd", "private-key", "passphrase"];

    [GeneratedRegex(@"(?<![\d.])(?:(?:25[0-5]|2[0-4]\d|1\d\d|[1-9]?\d)\.){3}(?:25[0-5]|2[0-4]\d|1\d\d|[1-9]?\d)(?![\d.])")]
    private static partial Regex Ipv4();

    [GeneratedRegex(@"[A-Za-z0-9._%+-]+@[A-Za-z0-9-]+(?:\.[A-Za-z0-9-]+)*\.[A-Za-z]{2,}")]
    private static partial Regex Email();

    [GeneratedRegex(@"^(\*\.)?([A-Za-z0-9-]{1,63}\.)+[A-Za-z]{2,63}\.?$")]
    private static partial Regex Fqdn();

    [GeneratedRegex(@"(user=)([^:\r\n]+)")]
    private static partial Regex HeaderUser();

    public static AnonymizeResult Anonymize(ConfigDocument doc, AnonymizeOptions options)
    {
        var original = doc.ToText();
        var state = new State(original, options);

        // Pass 1: names to replace wherever they occur as a whole token (user names, hostnames).
        CollectNames(doc, state);

        // Pass 2: rewrite every statement token by token.
        Rewrite(doc, (token, field, isKey, verb) => state.Anonymize(token, field, isKey), state);
        return new AnonymizeResult(doc.ToText(), new AnonymizationMap(1, state.Entries));
    }

    /// <summary>Replaces aliases with the originals recorded in <paramref name="map"/>.</summary>
    public static string Restore(ConfigDocument doc, AnonymizationMap map)
    {
        var byAlias = map.Entries.GroupBy(e => e.Alias, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        string Back(string value)
        {
            if (byAlias.TryGetValue(value, out var whole)) return whole.Original;
            var s = Ipv4().Replace(value, m => byAlias.TryGetValue(m.Value, out var e) ? e.Original : m.Value);
            s = AnonToken().Replace(s, m => byAlias.TryGetValue(m.Value, out var e) ? e.Original : m.Value);
            return s;
        }

        Rewrite(doc, (token, _, _, _) => Back(token), null, headerUser: u => byAlias.TryGetValue(u, out var e) ? e.Original : u);
        return doc.ToText();
    }

    [GeneratedRegex(@"anon-[a-z]+-\d+(?:@example\.com|\.example)?|2001:db8::[0-9a-f]+")]
    private static partial Regex AnonToken();

    private static void CollectNames(ConfigDocument doc, State state)
    {
        if (state.Options.Users && doc.Header.User is { Length: > 0 } hu) state.Alias(AnonCategory.User, hu);
        Walk(doc, [], (path, node) =>
        {
            if (node is EditEntry e && state.Options.Users && path.Count > 0 && UserTables.Contains(path[^1])) state.Alias(AnonCategory.User, e.Key);
            if (node is not FieldNode { Verb: "set" } f) return;
            var values = f.Values;
            if (state.Options.Users && UserFields.Contains(f.Name))
            {
                foreach (var v in values.Where(v => v.Length > 0)) state.Alias(AnonCategory.User, v);
            }

            if (state.Options.Hostnames && HostFields.Contains(f.Name))
            {
                foreach (var v in values.Where(v => v.Length > 0 && !IPAddress.TryParse(v, out _))) state.Alias(AnonCategory.Hostname, v);
            }
        });
    }

    private delegate string TokenMap(string value, string? field, bool isKey, string verb);

    private static void Rewrite(ConfigDocument doc, TokenMap map, State? state, Func<string, string>? headerUser = null)
    {
        var work = new List<(ConfigContainer Parent, ConfigNode Node)>();
        foreach (var n in doc.Descendants()) work.Add((n.Parent!, n));
        foreach (var (parent, node) in work)
        {
            switch (node)
            {
                case FieldNode f when f.Statement.Kind == StatementKind.Field:
                {
                    var st = f.Statement;
                    var tokens = st.Tokens;
                    var changed = st;
                    var encNext = false;
                    for (var i = 2; i < tokens.Count; i++)
                    {
                        var t = changed.Tokens[i];
                        string replacement;
                        if (encNext)
                        {
                            replacement = state is null ? map(t.Value, "ENC", false, f.Verb) : state.Secret(t.Value);
                            encNext = false;
                        }
                        else if (t.Value == "ENC" && !t.IsQuoted)
                        {
                            encNext = true;
                            continue;
                        }
                        else if (state is not null && PlainSecretFields.Contains(f.Name) && t.Value != ConfigInspector.PasswordMaskToken && t.Value.Length > 0)
                        {
                            replacement = state.Secret(t.Value);
                        }
                        else if (state is not null && t.Value.StartsWith("-----BEGIN", StringComparison.Ordinal))
                        {
                            replacement = state.Secret(t.Value);
                        }
                        else if (state is not null && CommentFields.Contains(f.Name))
                        {
                            replacement = state.Options.Comments && t.Value.Length > 0 ? state.Alias(AnonCategory.Comment, t.Value) : map(t.Value, f.Name, false, f.Verb);
                        }
                        else
                        {
                            replacement = map(t.Value, f.Name, false, f.Verb);
                        }

                        if (replacement != t.Value) changed = changed.WithTokenReplaced(i, Render(t, replacement));
                    }

                    if (!ReferenceEquals(changed, st)) parent.Replace(f, new FieldNode(changed));
                    break;
                }

                case EditEntry e when e.Statement.Tokens.Count > 1:
                {
                    var t = e.Statement.Tokens[1];
                    var replacement = map(t.Value, null, true, "edit");
                    if (replacement != t.Value) e.SetStatement(e.Statement.WithTokenReplaced(1, Render(t, replacement)));
                    break;
                }

                case TriviaNode tr when tr.Statement.Kind == StatementKind.Comment && tr.Statement.Raw.StartsWith("#config-version=", StringComparison.Ordinal):
                {
                    var raw = tr.Statement.Raw;
                    var replaced = HeaderUser().Replace(raw, m =>
                    {
                        var u = m.Groups[2].Value;
                        var alias = headerUser?.Invoke(u) ?? (state is not null && state.Options.Users ? state.Alias(AnonCategory.User, u) : u);
                        return m.Groups[1].Value + alias;
                    });
                    if (replaced != raw) parent.Replace(tr, TriviaNode.FromText(replaced));
                    break;
                }
            }
        }
    }

    private static string Render(ConfigToken original, string value)
    {
        if (original.Quote is { } q) return ConfigToken.Render(value, q);
        return value.Length == 0 || value.Any(c => char.IsWhiteSpace(c) || c is '"' or '\'' or '\\' or '#' or '<' or '>' or '(' or ')') ? ConfigToken.Render(value) : value;
    }

    private static void Walk(ConfigContainer container, List<string> path, Action<List<string>, ConfigNode> visit)
    {
        foreach (var child in container.Children)
        {
            visit(path, child);
            if (child is ConfigBlock b)
            {
                path.Add(b.Path);
                Walk(b, path, visit);
                path.RemoveAt(path.Count - 1);
            }
            else if (child is EditEntry e)
            {
                Walk(e, path, visit);
            }
        }
    }

    private sealed class State(string original, AnonymizeOptions options)
    {
        private readonly Dictionary<(AnonCategory, string), string> _aliases = [];
        private readonly Dictionary<string, MapEntry> _names = new(StringComparer.Ordinal);
        private readonly Dictionary<AnonCategory, int> _counters = [];
        private uint _publicNext = (198u << 24) | (18u << 16) | 1;
        private uint _privateNext = (100u << 24) | (64u << 16) | 1;
        private int _ipv6Next = 1;

        public AnonymizeOptions Options { get; } = options;

        public List<MapEntry> Entries { get; } = [];

        public string Alias(AnonCategory category, string value)
        {
            if (_aliases.TryGetValue((category, value), out var existing)) return existing;
            string alias;
            do
            {
                alias = NextAlias(category, value);
            }
            while (original.Contains(alias, StringComparison.Ordinal));

            _aliases[(category, value)] = alias;
            var entry = new MapEntry(category, value, alias);
            Entries.Add(entry);
            if (category is AnonCategory.User or AnonCategory.Hostname) _names.TryAdd(value, entry);
            return alias;
        }

        public string Secret(string value) => Alias(AnonCategory.Secret, value);

        private string NextAlias(AnonCategory category, string value)
        {
            var n = _counters[category] = _counters.GetValueOrDefault(category) + 1;
            switch (category)
            {
                case AnonCategory.PublicIp when value.Contains(':', StringComparison.Ordinal):
                case AnonCategory.PrivateIp when value.Contains(':', StringComparison.Ordinal):
                    return $"2001:db8::{_ipv6Next++:x}";
                case AnonCategory.PublicIp:
                    return Ip(ref _publicNext);
                case AnonCategory.PrivateIp:
                    return Ip(ref _privateNext);
                case AnonCategory.Email:
                    return $"anon-user-{n}@example.com";
                case AnonCategory.Hostname:
                    return $"anon-host-{n}.example";
                default:
                    return $"anon-{category.ToString().ToLowerInvariant()}-{n}";
            }
        }

        private static string Ip(ref uint next)
        {
            // Skip .0 and .255 host bytes for readability.
            while ((next & 0xFF) is 0 or 255) next++;
            var v = next++;
            return $"{v >> 24}.{(v >> 16) & 255}.{(v >> 8) & 255}.{v & 255}";
        }

        public string Anonymize(string value, string? field, bool isKey)
        {
            if (_names.TryGetValue(value, out var named)) return named.Alias;
            var s = value;
            if (Options.Emails) s = Email().Replace(s, m => Alias(AnonCategory.Email, m.Value));
            s = Ipv4().Replace(s, m => MaskIpv4(m.Value));
            s = MaskIpv6(s);
            if (Options.Hostnames && !isKey && field is not null && (field.Contains("server", StringComparison.Ordinal) || field.Contains("host", StringComparison.Ordinal))
                && Fqdn().IsMatch(s) && !IPAddress.TryParse(s, out _))
            {
                s = Alias(AnonCategory.Hostname, s);
            }

            return s;
        }

        private string MaskIpv4(string text)
        {
            if (!IPAddress.TryParse(text, out var ip)) return text;
            if (IpAddressRules.IsNetmask(ip)) return text;
            if (IpAddressRules.IsPrivate(ip)) return Options.PrivateIps ? Alias(AnonCategory.PrivateIp, text) : text;
            return IpAddressRules.IsInternetFacingIPv4(ip) ? Alias(AnonCategory.PublicIp, text) : text;
        }

        private string MaskIpv6(string token)
        {
            var slash = token.IndexOf('/');
            var addr = slash >= 0 ? token[..slash] : token;
            if (!addr.Contains(':', StringComparison.Ordinal) || !IPAddress.TryParse(addr, out var ip) || !IpAddressRules.IsV6(ip)) return token;
            if (IPAddress.IsLoopback(ip) || ip.IsIPv6LinkLocal || ip.Equals(IPAddress.IPv6Any) || ip.IsIPv6Multicast) return token;
            var b = ip.GetAddressBytes();
            if (b[0] == 0x20 && b[1] == 0x01 && b[2] == 0x0d && b[3] == 0xb8) return token; // already documentation space
            var category = IpAddressRules.IsPrivate(ip) ? AnonCategory.PrivateIp : AnonCategory.PublicIp;
            if (category == AnonCategory.PrivateIp && !Options.PrivateIps) return token;
            return Alias(category, addr) + (slash >= 0 ? token[slash..] : "");
        }
    }
}
