using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Shiftwall.Core.Data;

namespace Shiftwall.UpdateDb;

/// <summary>Reads and writes the files in data/ with stable formatting (UTF-8 without BOM, LF, trailing newline).</summary>
internal sealed class JsonStore(string dataDir)
{
    private static readonly DatabaseJsonContext WriteContext = new(new JsonSerializerOptions(DatabaseJsonContext.Default.Options)
    {
        // Files are data, never embedded in HTML: keep "—", "é" etc. readable in diffs.
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    });

    public string DataDir { get; } = dataDir;

    public ModelsDocument ReadModels() => Read("models.json", DatabaseJsonContext.Default.ModelsDocument);

    public ReleasesDocument ReadReleases() => Read("fortios-releases.json", DatabaseJsonContext.Default.ReleasesDocument);

    public UpgradePathsDocument ReadUpgradePaths() => Read("upgrade-paths.json", DatabaseJsonContext.Default.UpgradePathsDocument);

    public void Write(ModelsDocument doc) => Write("models.json", doc, WriteContext.ModelsDocument);

    public void Write(ReleasesDocument doc) => Write("fortios-releases.json", doc, WriteContext.ReleasesDocument);

    public void Write(UpgradePathsDocument doc) => Write("upgrade-paths.json", doc, WriteContext.UpgradePathsDocument);

    private T Read<T>(string name, JsonTypeInfo<T> info) =>
        JsonSerializer.Deserialize(File.ReadAllText(Path.Combine(DataDir, name)), info)
        ?? throw new InvalidDataException($"{name} is empty.");

    private void Write<T>(string name, T doc, JsonTypeInfo<T> info)
    {
        var json = JsonSerializer.Serialize(doc, info).Replace("\r\n", "\n", StringComparison.Ordinal) + "\n";
        File.WriteAllText(Path.Combine(DataDir, name), json, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }
}
