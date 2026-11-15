using System.Text.Json;
using System.Text.Json.Serialization;

namespace Shiftwall.Core.Data;

/// <summary>
/// Source-generated JSON contract for the database files (fast startup, trimming-safe).
/// Property names are snake_case in the files.
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower,
    WriteIndented = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    ReadCommentHandling = JsonCommentHandling.Skip,
    AllowTrailingCommas = false,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    RespectNullableAnnotations = true,
    RespectRequiredConstructorParameters = true)]
[JsonSerializable(typeof(ModelsDocument))]
[JsonSerializable(typeof(ReleasesDocument))]
[JsonSerializable(typeof(UpgradePathsDocument))]
public sealed partial class DatabaseJsonContext : JsonSerializerContext;
