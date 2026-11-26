using System.Text.Json;

namespace Shiftwall.Features.Rebuild;

public sealed record RuleSource(string Key, string Title, string Url);

/// <summary>
/// One translation rule (data/rules/&lt;from&gt;-&lt;to&gt;.json). Kinds: rename, value-map, default-change,
/// remove-no-equivalent, remove-with-equivalent, skip, not-a-difference.
/// </summary>
public sealed record TranslationRule(
    string Id,
    string Kind,
    string Path,
    IReadOnlyList<string> Sub,
    string? Param,
    string? Table,
    string? To,
    IReadOnlyDictionary<string, string> Map,
    IReadOnlyList<string> Values,
    string? Value,
    string Note,
    IReadOnlyList<RuleSource> Sources)
{
    /// <summary>For default-change rules: other parameters of the object that must have one of these values.</summary>
    public IReadOnlyDictionary<string, IReadOnlyList<string>> When { get; init; } = new Dictionary<string, IReadOnlyList<string>>();

    /// <summary>Applied by dedicated engine code (e.g. interface addressing), not by the generic translation.</summary>
    public bool ByCode { get; init; }

    public bool Matches(string path, IReadOnlyList<string> sub) =>
        (Path == "*" || Path == path) && (Path == "*" || Sub.SequenceEqual(sub));
}

/// <summary>The rules that translate a configuration from one FortiOS version to another.</summary>
public sealed class RuleSet
{
    private RuleSet(string from, string to, IReadOnlyList<string> paths, IReadOnlyList<TranslationRule> rules)
    {
        From = from;
        To = to;
        Paths = paths;
        Rules = rules;
    }

    public string From { get; }

    public string To { get; }

    /// <summary>The configuration paths the rules cover (every difference on them has a rule).</summary>
    public IReadOnlyList<string> Paths { get; }

    public IReadOnlyList<TranslationRule> Rules { get; }

    private static readonly Dictionary<string, RuleSet> Cache = [];

    public static RuleSet Load(string from, string to)
    {
        var key = $"{from}-{to}";
        lock (Cache)
        {
            if (Cache.TryGetValue(key, out var r)) return r;
            using var stream = typeof(RuleSet).Assembly.GetManifestResourceStream($"Shiftwall.Rules.{key}.json")
                ?? throw new ArgumentException($"No translation rules from FortiOS {from} to {to}.", nameof(from));
            r = Parse(stream);
            Cache[key] = r;
            return r;
        }
    }

    public static bool Exists(string from, string to) =>
        typeof(RuleSet).Assembly.GetManifestResourceInfo($"Shiftwall.Rules.{from}-{to}.json") is not null;

    public static RuleSet Parse(Stream json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var sources = new Dictionary<string, RuleSource>(StringComparer.Ordinal);
        foreach (var s in root.GetProperty("sources").EnumerateObject())
        {
            sources[s.Name] = new RuleSource(s.Name, s.Value.GetProperty("title").GetString()!, s.Value.GetProperty("url").GetString()!);
        }

        var rules = new List<TranslationRule>();
        foreach (var e in root.GetProperty("rules").EnumerateArray())
        {
            string? S(string n) => e.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
            IReadOnlyList<string> L(string n) => e.TryGetProperty(n, out var v) ? [.. v.EnumerateArray().Select(x => x.GetString()!)] : [];
            var map = new Dictionary<string, string>(StringComparer.Ordinal);
            if (e.TryGetProperty("map", out var m))
            {
                foreach (var p in m.EnumerateObject()) map[p.Name] = p.Value.GetString()!;
            }

            var when = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
            if (e.TryGetProperty("when", out var w))
            {
                foreach (var p in w.EnumerateObject()) when[p.Name] = [.. p.Value.EnumerateArray().Select(x => x.GetString()!)];
            }

            rules.Add(new TranslationRule(
                S("id")!, S("kind")!, S("path")!, L("sub"), S("param"), S("table"), S("to"), map, L("values"), S("value"), S("note") ?? "",
                [.. L("sources").Select(k => sources.TryGetValue(k, out var src) ? src : throw new InvalidDataException($"Unknown source '{k}' in rule {S("id")}."))])
            {
                When = when,
                ByCode = e.TryGetProperty("by_code", out var bc) && bc.GetBoolean(),
            });
        }

        return new RuleSet(
            root.GetProperty("from").GetString()!,
            root.GetProperty("to").GetString()!,
            [.. root.GetProperty("paths").EnumerateArray().Select(x => x.GetString()!)],
            rules);
    }

    /// <summary>The rule for a parameter of a path, if any (a "values" rule only matches those values).</summary>
    public TranslationRule? ForParam(string path, IReadOnlyList<string> sub, string param, IReadOnlyList<string>? values = null) =>
        Rules.FirstOrDefault(r => r.Param == param && r.Matches(path, sub) && r.Kind != "default-change"
            && (r.Values.Count == 0 || values is null || values.Any(v => r.Values.Contains(v, StringComparer.Ordinal))));

    public TranslationRule? ForTable(string path, IReadOnlyList<string> sub, string table) =>
        Rules.FirstOrDefault(r => r.Table == table && r.Matches(path, sub));

    public IEnumerable<TranslationRule> Defaults(string path, IReadOnlyList<string> sub) =>
        Rules.Where(r => r.Kind == "default-change" && !r.ByCode && r.Matches(path, sub));
}
