using Shiftwall.Core.Config;
using Shiftwall.Core.Data;

namespace Shiftwall.UpdateDb;

/// <summary>
/// Takes interface names and virtual-switch membership from a real factory-default backup of a model.
/// The backup is read from the local disk only; it is never copied, stored or printed beyond those names.
/// </summary>
internal static class DefaultBackupImport
{
    public static int Run(JsonStore store, string modelId, string file, DateOnly today, bool apply)
    {
        var inspection = ConfigInspector.Inspect(File.ReadAllBytes(file));
        if (inspection.Kind != ConfigFileKind.FortiOsText || inspection.Document is null)
        {
            Console.Error.WriteLine($"Not a FortiOS text backup ({inspection.Kind}).");
            return 1;
        }

        var doc = inspection.Document;
        if (!doc.Header.HasConfigVersion || inspection.IsPartial)
        {
            Console.Error.WriteLine("The file is partial or has no #config-version header: a complete factory-default backup is required.");
            return 1;
        }

        var models = store.ReadModels();
        var model = models.Models.FirstOrDefault(m => m.Id.Equals(modelId, StringComparison.OrdinalIgnoreCase));
        if (model is null)
        {
            Console.Error.WriteLine($"Model {modelId} is not in models.json: add it first (id, display name, family, platform).");
            return 1;
        }

        var physical = InterfaceInventory.Physical(doc);
        var switches = InterfaceInventory.VirtualSwitches(doc);
        var title = $"Factory-default configuration of {model.Id}, FortiOS {doc.Header.Version ?? "?"} build {doc.Header.Build ?? "?"}";
        var source = new SourceRef(null, title, today, "Interface names only; the backup itself is not stored or published.", SourceKind.FactoryDefaultBackup);

        Console.WriteLine(title);
        Console.WriteLine($"Physical interfaces ({physical.Count}): {string.Join(", ", physical.Select(p => p.Name))}");
        foreach (var sw in switches)
        {
            Console.WriteLine($"Virtual switch {sw.Name}: {string.Join(", ", sw.Members)}");
        }

        var current = model.Interfaces.Value ?? [];
        var added = physical.Select(p => p.Name).Except(current.Select(i => i.Name)).ToList();
        var missing = current.Select(i => i.Name).Except(physical.Select(p => p.Name)).ToList();
        if (current.Count > 0)
        {
            Console.WriteLine($"Compared with models.json: +[{string.Join(",", added)}] -[{string.Join(",", missing)}]");
        }

        if (!apply)
        {
            Console.WriteLine("Dry run. Re-run with --apply to write interface names and switch membership to models.json.");
            return 0;
        }

        var interfaces = physical.Select(p =>
        {
            var old = current.FirstOrDefault(i => i.Name == p.Name);
            return old ?? new InterfaceInfo(p.Name, "unknown");
        }).ToList();

        // Keep the existing document sources (e.g. hardware-acceleration guide) and add the backup as a source.
        var updated = model with
        {
            Interfaces = new Sourced<IReadOnlyList<InterfaceInfo>>(interfaces, VerificationStatus.Verified,
                [.. model.Interfaces.Sources.Where(s => s.EffectiveKind == SourceKind.Document && !missing.Any() && !added.Any()), source],
                "Interface names from a factory-default backup (all physical ports, including HA/MGMT)."),
            DefaultSwitches = new Sourced<IReadOnlyList<HardwareSwitchInfo>>(
                [.. switches.Select(s => new HardwareSwitchInfo(s.Name, s.Members))], VerificationStatus.Verified, [source],
                "From config system virtual-switch in the factory-default backup."),
        };

        store.Write(models with { Models = [.. models.Models.Select(m => m.Id == model.Id ? updated : m)] });
        Console.WriteLine("models.json updated. New interfaces have media \"unknown\": fill it from the data sheet. Review with git diff.");
        return 0;
    }
}
