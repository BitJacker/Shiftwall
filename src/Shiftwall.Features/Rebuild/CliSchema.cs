using System.Globalization;
using System.Text.Json;

namespace Shiftwall.Features.Rebuild;

/// <summary>A parameter of a CLI table, as documented in the official CLI Reference of a FortiOS version.</summary>
public sealed record SchemaParam(string Type, IReadOnlyList<string>? Options, string? Default, long? Min, long? Max, int? MaxLength, bool Multi);

/// <summary>A <c>config</c> path (or sub-table) of the CLI Reference.</summary>
public sealed class SchemaNode
{
    public bool IsTable { get; init; }

    public string? Url { get; init; }

    public IReadOnlyDictionary<string, SchemaParam> Params { get; init; } = new Dictionary<string, SchemaParam>();

    public IReadOnlyDictionary<string, SchemaNode> Sub { get; init; } = new Dictionary<string, SchemaNode>();
}

/// <summary>Why a value does not match the schema; null means valid.</summary>
public enum SchemaProblem
{
    UnknownPath,
    UnknownParam,
    InvalidOption,
    OutOfRange,
    NotANumber,
    TooLong,
    TooManyValues,
}

/// <summary>
/// The CLI schema of one FortiOS version, built by tools/cli-schema from the official CLI Reference
/// (data/schemas/&lt;version&gt;.json). Only names, types, sizes, defaults and option names: no documentation text.
/// </summary>
public sealed class CliSchema
{
    private CliSchema(string version, string title, string url, string verifiedOn, Dictionary<string, SchemaNode> paths)
    {
        Version = version;
        SourceTitle = title;
        SourceUrl = url;
        VerifiedOn = verifiedOn;
        Paths = paths;
    }

    public string Version { get; }

    public string SourceTitle { get; }

    public string SourceUrl { get; }

    public string VerifiedOn { get; }

    public IReadOnlyDictionary<string, SchemaNode> Paths { get; }

    private static readonly Dictionary<string, CliSchema> Cache = [];

    /// <summary>Versions bundled with the application.</summary>
    public static IReadOnlyList<string> Bundled { get; } = ["5.2", "8.0"];

    /// <summary>Loads a bundled schema ("5.2", "8.0"...).</summary>
    public static CliSchema Load(string version)
    {
        lock (Cache)
        {
            if (Cache.TryGetValue(version, out var s)) return s;
            using var stream = typeof(CliSchema).Assembly.GetManifestResourceStream($"Shiftwall.Schemas.{version}.json")
                ?? throw new ArgumentException($"No bundled CLI schema for FortiOS {version}.", nameof(version));
            s = Parse(stream);
            Cache[version] = s;
            return s;
        }
    }

    public static CliSchema Parse(Stream json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var src = root.GetProperty("source");
        var paths = new Dictionary<string, SchemaNode>(StringComparer.Ordinal);
        foreach (var p in root.GetProperty("paths").EnumerateObject()) paths[p.Name] = Node(p.Value);
        return new CliSchema(
            root.GetProperty("version").GetString()!,
            src.GetProperty("title").GetString()!,
            src.GetProperty("url").GetString()!,
            src.GetProperty("verified_on").GetString()!,
            paths);
    }

    private static SchemaNode Node(JsonElement e)
    {
        var ps = new Dictionary<string, SchemaParam>(StringComparer.Ordinal);
        foreach (var p in e.GetProperty("params").EnumerateObject())
        {
            var v = p.Value;
            ps[p.Name] = new SchemaParam(
                v.TryGetProperty("t", out var t) ? t.GetString() ?? "string" : "string",
                v.TryGetProperty("o", out var o) ? [.. o.EnumerateArray().Select(x => x.GetString()!)] : null,
                v.TryGetProperty("d", out var d) ? d.GetString() : null,
                v.TryGetProperty("min", out var mn) ? mn.GetInt64() : null,
                v.TryGetProperty("max", out var mx) ? mx.GetInt64() : null,
                v.TryGetProperty("len", out var ln) ? ln.GetInt32() : null,
                v.TryGetProperty("multi", out var mu) && mu.GetBoolean());
        }

        var sub = new Dictionary<string, SchemaNode>(StringComparer.Ordinal);
        if (e.TryGetProperty("sub", out var s))
        {
            foreach (var c in s.EnumerateObject()) sub[c.Name] = Node(c.Value);
        }

        return new SchemaNode
        {
            IsTable = e.TryGetProperty("edit", out var ed) && ed.GetBoolean(),
            Url = e.TryGetProperty("url", out var u) ? u.GetString() : null,
            Params = ps,
            Sub = sub,
        };
    }

    /// <summary>Finds a node by path: a top-level path optionally followed by sub-table names ("system interface", "ipv6").</summary>
    public SchemaNode? Find(string path, IReadOnlyList<string>? subPath = null)
    {
        if (!Paths.TryGetValue(path, out var node)) return null;
        foreach (var s in subPath ?? [])
        {
            if (!node.Sub.TryGetValue(s, out node!)) return null;
        }

        return node;
    }

    /// <summary>Checks one <c>set</c> statement against a node. Returns null when it is valid.</summary>
    public static SchemaProblem? Check(SchemaNode node, string param, IReadOnlyList<string> values)
    {
        ArgumentNullException.ThrowIfNull(node);
        if (!node.Params.TryGetValue(param, out var p)) return SchemaProblem.UnknownParam;
        return Check(p, values);
    }

    public static SchemaProblem? Check(SchemaParam p, IReadOnlyList<string> values)
    {
        ArgumentNullException.ThrowIfNull(p);
        ArgumentNullException.ThrowIfNull(values);
        switch (p.Type)
        {
            case "option":
                if (!p.Multi && values.Count > 1) return SchemaProblem.TooManyValues;
                if (p.Options is { Count: > 0 } && values.Any(v => !p.Options.Contains(v, StringComparer.Ordinal))) return SchemaProblem.InvalidOption;
                return null;
            case "integer":
                if (values.Count != 1) return SchemaProblem.TooManyValues;
                if (!long.TryParse(values[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var n)) return SchemaProblem.NotANumber;
                if ((p.Min is { } min && n < min) || (p.Max is { } max && n > max)) return SchemaProblem.OutOfRange;
                return null;
            case "string" or "var-string" or "password" or "password-2" or "password-3" or "password_aes256":
                if (!p.Multi && values.Count > 1) return SchemaProblem.TooManyValues;
                if (p.MaxLength is { } len && values.Any(v => v.Length > len)) return SchemaProblem.TooLong;
                return null;
            default:
                return null; // addresses, masks, ranges, "user" types: format checked by FortiOS itself
        }
    }
}
