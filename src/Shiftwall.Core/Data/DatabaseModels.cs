using System.Text.Json.Serialization;

namespace Shiftwall.Core.Data;

/// <summary>Whether a datum is backed by an official Fortinet source.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<VerificationStatus>))]
public enum VerificationStatus
{
    /// <summary>Taken from an official Fortinet source listed in <see cref="Sourced{T}.Sources"/>.</summary>
    [JsonStringEnumMemberName("verified")] Verified,

    /// <summary>No reliable official source found: the UI must show a warning and never rely on it silently.</summary>
    [JsonStringEnumMemberName("unverified")] Unverified,
}

/// <summary>An official source backing a datum, with the date it was last checked.</summary>
/// <param name="Url">Absolute https URL on an allowed official Fortinet domain.</param>
/// <param name="Title">Human-readable title of the document.</param>
/// <param name="VerifiedOn">Date the datum was last checked against the source.</param>
/// <param name="Note">Where in the document the datum is found (section, table, CLI output...).</param>
public sealed record SourceRef(string Url, string Title, DateOnly VerifiedOn, string? Note = null);

/// <summary>A value together with its provenance.</summary>
public sealed record Sourced<T>(
    T? Value,
    VerificationStatus Status,
    IReadOnlyList<SourceRef> Sources,
    string? Note = null)
{
    public bool IsVerified => Status == VerificationStatus.Verified;

    public static Sourced<T> Unverified(string note) => new(default, VerificationStatus.Unverified, [], note);
}

/// <summary>A physical (or VM) network interface as named by FortiOS.</summary>
/// <param name="Name">Exact FortiOS interface name, e.g. <c>wan1</c>, <c>port1</c>, <c>x1</c>, <c>a</c>.</param>
/// <param name="Media">Physical media: <c>rj45</c>, <c>sfp</c>, <c>sfp+</c>, <c>rj45/sfp+</c> (shared), <c>virtual</c>...</param>
/// <param name="Speed">Maximum speed as documented (e.g. <c>1G</c>, <c>10G</c>), or null when not documented.</param>
/// <param name="Note">Extra documented facts, e.g. "FortiLink".</param>
public sealed record InterfaceInfo(string Name, string Media, string? Speed = null, string? Note = null);

/// <summary>A hardware switch / virtual switch present in the factory-default configuration.</summary>
public sealed record HardwareSwitchInfo(string Name, IReadOnlyList<string> Members);

/// <summary>A FortiGate hardware model or VM platform.</summary>
public sealed record FortiGateModel(
    string Id,
    string DisplayName,
    string Family,
    string Platform,
    Sourced<IReadOnlyList<InterfaceInfo>> Interfaces,
    Sourced<bool?> HasDisk,
    Sourced<IReadOnlyList<HardwareSwitchInfo>> DefaultSwitches,
    IReadOnlyList<string>? Aliases = null,
    string? Note = null);

/// <summary>A FortiOS patch release.</summary>
/// <param name="Version">e.g. <c>7.6.8</c>.</param>
/// <param name="Build">Build number as printed in the release notes, e.g. <c>3744</c>.</param>
/// <param name="ReleaseDate">Date of the "Initial release" entry in the release notes change log.</param>
/// <param name="SupportedModels">Model ids listed under "Supported models" in the release notes.</param>
public sealed record FortiOsRelease(
    string Version,
    Sourced<string> Build,
    Sourced<DateOnly?> ReleaseDate,
    Sourced<IReadOnlyList<string>> SupportedModels);

/// <summary>Upgrade path information. Paths are only listed when an official, consultable source exists.</summary>
public sealed record UpgradePathInfo(
    string OfficialToolUrl,
    IReadOnlyList<UpgradePath> Paths,
    Sourced<bool?> PublishedInConsultableForm);

public sealed record UpgradePath(string Model, string From, string To, IReadOnlyList<string> Steps, IReadOnlyList<SourceRef> Sources);

/// <summary>Root documents of the JSON files in <c>data/</c>.</summary>
public sealed record ModelsDocument(int SchemaVersion, IReadOnlyList<FortiGateModel> Models);

public sealed record ReleasesDocument(int SchemaVersion, IReadOnlyList<FortiOsRelease> Releases);

public sealed record UpgradePathsDocument(int SchemaVersion, UpgradePathInfo UpgradePaths);
