using System.Text.Json;
using System.Text.Json.Serialization;

namespace Shiftwall.Features.Migration;

/// <summary>
/// Source physical interface → target physical interface. A source interface absent from the map (or mapped to
/// null) is "not mapped": allowed only if nothing uses it, in which case its definition is removed.
/// </summary>
public sealed class InterfaceMapping
{
    private readonly Dictionary<string, string?> _map = new(StringComparer.Ordinal);

    public IReadOnlyDictionary<string, string?> Entries => _map;

    public string? this[string source]
    {
        get => _map.GetValueOrDefault(source);
        set => _map[source] = string.IsNullOrEmpty(value) ? null : value;
    }

    public bool IsMapped(string source) => _map.TryGetValue(source, out var t) && t is not null;

    /// <summary>Maps every source interface whose name also exists on the target to itself.</summary>
    public static InterfaceMapping Auto(IEnumerable<string> sourceInterfaces, IEnumerable<string> targetInterfaces)
    {
        var target = targetInterfaces.ToHashSet(StringComparer.Ordinal);
        var m = new InterfaceMapping();
        foreach (var s in sourceInterfaces)
        {
            m[s] = target.Contains(s) ? s : null;
        }

        return m;
    }

    /// <summary>
    /// Proposes a mapping for the user to review: same names first, then ports of the same role in natural order
    /// (wan1, wan2 → wan1, wan2; lan1…lan5 or port1… → internal1… / port1… / lan1…; x1 → x1 or sfp1; dmz → dmz;
    /// a switch-mode "internal" → the first LAN port left, reported by the analysis).
    /// When the target has fewer ports of a role, ports in use get priority. A port with no counterpart of its
    /// role stays unmapped: WAN and DMZ ports fall back to the remaining LAN ports, nothing else is guessed.
    /// </summary>
    /// <param name="source">Source physical interfaces with their usage count (references in the configuration).</param>
    /// <param name="targetInterfaces">Target physical interfaces.</param>
    public static InterfaceMapping Suggest(IEnumerable<(string Name, int Usage)> source, IEnumerable<string> targetInterfaces)
    {
        var src = source.ToList();
        var free = targetInterfaces.Distinct(StringComparer.Ordinal).ToList();
        var m = new InterfaceMapping();
        foreach (var (name, _) in src) m[name] = null;

        foreach (var (name, _) in src.Where(s => free.Contains(s.Name, StringComparer.Ordinal)))
        {
            m[name] = name;
            free.Remove(name);
        }

        void Assign(PortRole role, Func<PortRole, bool> pool)
        {
            var pending = src.Where(s => !m.IsMapped(s.Name) && RoleOf(s.Name) == role).ToList();
            var targets = free.Where(t => pool(RoleOf(t))).OrderBy(t => t, NaturalComparer.Instance).ToList();
            if (pending.Count == 0 || targets.Count == 0) return;
            var ordered = pending.Count <= targets.Count
                ? pending.OrderBy(s => s.Name, NaturalComparer.Instance)
                : pending.OrderByDescending(s => s.Usage > 0).ThenBy(s => s.Name, NaturalComparer.Instance);
            foreach (var (s, t) in ordered.Zip(targets))
            {
                m[s.Name] = t;
                free.Remove(t);
            }
        }

        Assign(PortRole.Wan, r => r == PortRole.Wan);
        Assign(PortRole.Dmz, r => r == PortRole.Dmz);
        Assign(PortRole.Fiber, r => r == PortRole.Fiber);
        Assign(PortRole.Lan, r => r == PortRole.Lan);
        Assign(PortRole.SwitchModeLan, r => r == PortRole.Lan);
        Assign(PortRole.Wan, r => r == PortRole.Lan);
        Assign(PortRole.Dmz, r => r == PortRole.Lan);
        return m;
    }

    internal enum PortRole { Other, Wan, Dmz, Lan, Fiber, SwitchModeLan }

    internal static PortRole RoleOf(string name)
    {
        var prefix = name.TrimEnd('0', '1', '2', '3', '4', '5', '6', '7', '8', '9');
        var hasNumber = prefix.Length < name.Length;
        return prefix switch
        {
            "wan" => PortRole.Wan,
            "dmz" => PortRole.Dmz,
            "lan" or "internal" or "port" when hasNumber => PortRole.Lan,
            // A physical "internal"/"lan" without a number: the built-in switch of older models in switch mode
            // (community.fortinet.com Technical Tip "Switch mode vs interface mode", FortiOS 5.2 and earlier).
            "lan" or "internal" => PortRole.SwitchModeLan,
            "x" or "sfp" when hasNumber => PortRole.Fiber,
            _ => PortRole.Other,
        };
    }

    /// <summary>Orders port2 before port10.</summary>
    private sealed class NaturalComparer : IComparer<string>
    {
        public static readonly NaturalComparer Instance = new();

        public int Compare(string? x, string? y)
        {
            var (px, nx) = Split(x ?? "");
            var (py, ny) = Split(y ?? "");
            var c = string.CompareOrdinal(px, py);
            return c != 0 ? c : nx.CompareTo(ny);
        }

        private static (string Prefix, long Number) Split(string s)
        {
            var prefix = s.TrimEnd('0', '1', '2', '3', '4', '5', '6', '7', '8', '9');
            return (prefix, long.TryParse(s.AsSpan(prefix.Length), out var n) ? n : -1);
        }
    }

    /// <summary>Target interfaces that two or more source interfaces map to.</summary>
    public IReadOnlyList<string> DuplicateTargets() =>
        [.. _map.Values.Where(v => v is not null).GroupBy(v => v!, StringComparer.Ordinal).Where(g => g.Count() > 1).Select(g => g.Key)];

    public MappingTemplate ToTemplate(string? name, string? sourcePlatform, string? targetModel) =>
        new(1, name, sourcePlatform, targetModel, _map.Where(kv => kv.Value is not null).ToDictionary(kv => kv.Key, kv => kv.Value!, StringComparer.Ordinal));

    /// <summary>Applies a saved template on top of the given source interfaces (unknown entries are ignored).</summary>
    public static InterfaceMapping FromTemplate(MappingTemplate template, IEnumerable<string> sourceInterfaces)
    {
        ArgumentNullException.ThrowIfNull(template);
        var m = new InterfaceMapping();
        foreach (var s in sourceInterfaces)
        {
            m[s] = template.Map.GetValueOrDefault(s);
        }

        return m;
    }
}

/// <summary>A reusable mapping (e.g. every branch office with the same old and new model). Contains only interface names.</summary>
public sealed record MappingTemplate(int SchemaVersion, string? Name, string? SourcePlatform, string? TargetModel, IReadOnlyDictionary<string, string> Map)
{
    public string ToJson() => JsonSerializer.Serialize(this, MappingTemplateJson.Default.MappingTemplate);

    public static MappingTemplate FromJson(string json)
    {
        var t = JsonSerializer.Deserialize(json, MappingTemplateJson.Default.MappingTemplate)
                ?? throw new InvalidDataException("Empty mapping template.");
        if (t.SchemaVersion != 1) throw new InvalidDataException($"Unsupported mapping template version {t.SchemaVersion}.");
        return t;
    }
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower, WriteIndented = true)]
[JsonSerializable(typeof(MappingTemplate))]
internal sealed partial class MappingTemplateJson : JsonSerializerContext;
