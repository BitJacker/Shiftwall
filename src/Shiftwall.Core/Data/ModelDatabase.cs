using System.Reflection;
using System.Text.Json;

namespace Shiftwall.Core.Data;

/// <summary>
/// The bundled database of FortiGate models, FortiOS releases and upgrade-path information.
/// Every datum carries its official source; see <see cref="Sourced{T}"/>.
/// </summary>
/// <remarks>
/// A real factory-default backup of the target model always takes precedence over this database:
/// callers must prefer interface names parsed from such a backup when one is provided.
/// </remarks>
public sealed class ModelDatabase
{
    public const int SupportedSchemaVersion = 1;

    private readonly Dictionary<string, FortiGateModel> _models;
    private readonly Dictionary<string, FortiOsRelease> _releases;

    private ModelDatabase(ModelsDocument models, ReleasesDocument releases, UpgradePathsDocument upgradePaths)
    {
        Models = models.Models;
        Releases = [.. releases.Releases.OrderBy(r => FortiOsVersion.Parse(r.Version))];
        UpgradePaths = upgradePaths.UpgradePaths;
        _models = models.Models.ToDictionary(m => m.Id, StringComparer.OrdinalIgnoreCase);
        _releases = releases.Releases.ToDictionary(r => r.Version, StringComparer.Ordinal);
    }

    public IReadOnlyList<FortiGateModel> Models { get; }

    /// <summary>Releases ordered by version, oldest first.</summary>
    public IReadOnlyList<FortiOsRelease> Releases { get; }

    public UpgradePathInfo UpgradePaths { get; }

    public FortiGateModel? FindModel(string id) =>
        _models.TryGetValue(id, out var m) ? m : Models.FirstOrDefault(x => x.Aliases?.Contains(id, StringComparer.OrdinalIgnoreCase) == true);

    public FortiOsRelease? FindRelease(string version) => _releases.GetValueOrDefault(version);

    /// <summary>Releases whose release notes list <paramref name="modelId"/> as supported.</summary>
    public IEnumerable<FortiOsRelease> ReleasesSupporting(string modelId) =>
        Releases.Where(r => r.SupportedModels.Value?.Contains(modelId, StringComparer.OrdinalIgnoreCase) == true);

    /// <summary>Loads the database embedded in Shiftwall.Core.</summary>
    public static ModelDatabase LoadEmbedded()
    {
        var asm = typeof(ModelDatabase).Assembly;
        return Load(
            ReadResource(asm, "Shiftwall.Data.models.json"),
            ReadResource(asm, "Shiftwall.Data.fortios-releases.json"),
            ReadResource(asm, "Shiftwall.Data.upgrade-paths.json"));
    }

    /// <summary>Loads the database from a directory containing the three JSON files (used by tools and imports).</summary>
    public static ModelDatabase LoadFromDirectory(string directory) => Load(
        File.ReadAllText(Path.Combine(directory, "models.json")),
        File.ReadAllText(Path.Combine(directory, "fortios-releases.json")),
        File.ReadAllText(Path.Combine(directory, "upgrade-paths.json")));

    /// <summary>Parses and validates the three JSON documents.</summary>
    /// <exception cref="DatabaseException">The documents are malformed or fail validation.</exception>
    public static ModelDatabase Load(string modelsJson, string releasesJson, string upgradePathsJson)
    {
        var models = Deserialize(modelsJson, DatabaseJsonContext.Default.ModelsDocument, "models.json");
        var releases = Deserialize(releasesJson, DatabaseJsonContext.Default.ReleasesDocument, "fortios-releases.json");
        var paths = Deserialize(upgradePathsJson, DatabaseJsonContext.Default.UpgradePathsDocument, "upgrade-paths.json");

        foreach (var (name, version) in new[] { ("models.json", models.SchemaVersion), ("fortios-releases.json", releases.SchemaVersion), ("upgrade-paths.json", paths.SchemaVersion) })
        {
            if (version != SupportedSchemaVersion)
            {
                throw new DatabaseException($"{name}: schema_version {version} is not supported (expected {SupportedSchemaVersion}).", []);
            }
        }

        var errors = DatabaseValidator.Validate(models, releases, paths);
        if (errors.Count > 0)
        {
            throw new DatabaseException("The database failed validation.", errors);
        }

        return new ModelDatabase(models, releases, paths);
    }

    private static T Deserialize<T>(string json, System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> typeInfo, string name)
    {
        try
        {
            return JsonSerializer.Deserialize(json, typeInfo) ?? throw new DatabaseException($"{name}: document is empty.", []);
        }
        catch (JsonException ex)
        {
            // Only the location is reported, never file content.
            throw new DatabaseException($"{name}: invalid JSON at {ex.Path} (line {ex.LineNumber + 1}).", []);
        }
    }

    private static string ReadResource(Assembly asm, string name)
    {
        using var stream = asm.GetManifestResourceStream(name)
            ?? throw new DatabaseException($"Embedded resource '{name}' is missing.", []);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}

/// <summary>The database could not be loaded. <see cref="Errors"/> lists every validation problem.</summary>
public sealed class DatabaseException(string message, IReadOnlyList<string> errors)
    : Exception(errors.Count == 0 ? message : message + Environment.NewLine + string.Join(Environment.NewLine, errors))
{
    public IReadOnlyList<string> Errors { get; } = errors;
}
