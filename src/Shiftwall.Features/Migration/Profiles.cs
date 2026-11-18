using Shiftwall.Core.Config;
using Shiftwall.Core.Data;

namespace Shiftwall.Features.Migration;

/// <summary>Where the target information comes from.</summary>
public enum TargetSource
{
    /// <summary>A real factory-default backup of the target: most reliable, provides the header.</summary>
    DefaultBackup,

    /// <summary>The bundled model database: no header, possibly unverified fields.</summary>
    Database,
}

/// <summary>What Shiftwall knows about the migration target.</summary>
public sealed record TargetProfile(
    TargetSource Source,
    string? ModelId,
    string? Version,
    string? Build,
    IReadOnlyList<string> PhysicalInterfaces,
    bool InterfacesVerified,
    IReadOnlyList<VirtualSwitch> VirtualSwitches,
    bool? HasDisk,
    bool HasDiskVerified,
    IReadOnlyList<Statement> HeaderLines,
    string? PlatformCode,
    string? InterfacesNote)
{
    /// <summary>
    /// Builds the target from a factory-default backup. If <paramref name="model"/> is given (from the database),
    /// it only adds the disk information; interface names always come from the backup.
    /// </summary>
    public static TargetProfile FromDefaultBackup(ConfigInspection backup, FortiGateModel? model = null)
    {
        ArgumentNullException.ThrowIfNull(backup);
        var doc = backup.Document ?? throw new ArgumentException("The target backup could not be parsed.", nameof(backup));
        return new TargetProfile(
            TargetSource.DefaultBackup,
            model?.Id,
            doc.Header.Version,
            doc.Header.Build,
            [.. InterfaceInventory.Physical(doc).Select(i => i.Name)],
            InterfacesVerified: true,
            InterfaceInventory.VirtualSwitches(doc),
            model?.HasDisk.Value,
            model?.HasDisk.IsVerified ?? false,
            doc.Header.Lines,
            doc.Header.PlatformCode,
            null);
    }

    /// <summary>Builds the target from the database (planning/simulation, or when no backup is available).</summary>
    public static TargetProfile FromDatabase(FortiGateModel model, string? version)
    {
        ArgumentNullException.ThrowIfNull(model);
        return new TargetProfile(
            TargetSource.Database,
            model.Id,
            version,
            null,
            [.. (model.Interfaces.Value ?? []).Select(i => i.Name)],
            model.Interfaces.IsVerified,
            [.. (model.DefaultSwitches.Value ?? []).Select(s => new VirtualSwitch(s.Name, s.Members, 0))],
            model.HasDisk.Value,
            model.HasDisk.IsVerified,
            [],
            null,
            model.Interfaces.Note);
    }
}

/// <summary>What Shiftwall knows about the source configuration.</summary>
public sealed record SourceProfile(
    ConfigInspection Inspection,
    IReadOnlyList<DeclaredInterface> PhysicalInterfaces,
    IReadOnlyList<VirtualSwitch> VirtualSwitches)
{
    public ConfigDocument Document => Inspection.Document!;

    public string? Version => Inspection.Header?.Version;

    public string? PlatformCode => Inspection.Header?.PlatformCode;

    public static SourceProfile From(ConfigInspection inspection)
    {
        ArgumentNullException.ThrowIfNull(inspection);
        if (inspection.Document is null)
        {
            throw new ArgumentException("The source configuration could not be parsed.", nameof(inspection));
        }

        return new SourceProfile(
            inspection,
            InterfaceInventory.Physical(inspection.Document),
            InterfaceInventory.VirtualSwitches(inspection.Document));
    }
}
