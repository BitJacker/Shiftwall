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
