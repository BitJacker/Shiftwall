using Shiftwall.Core.Config;
using Shiftwall.Features.Rebuild;

namespace Shiftwall.Features.Tests.Rebuild;

internal static class RebuildFixtures
{
    public static string Dir => Path.Combine(AppContext.BaseDirectory, "Rebuild", "Fixtures");

    public static ConfigDocument Load(string file) => ConfigInspector.Inspect(File.ReadAllBytes(Path.Combine(Dir, file))).Document!;

    /// <summary>FortiGate 40F target from the synthetic stand-in backup (interfaces and switch as a factory 40F would have them).</summary>
    public static RebuildTarget Target40F(bool withBackup = true)
    {
        var backup = Load("40f-8.0-target-synthetic.conf");
        return new RebuildTarget(
            "FG-40F",
            "8.0.2",
            [.. InterfaceInventory.Declared(backup).Select(i => i.Name)],
            InterfaceInventory.VirtualSwitches(backup),
            withBackup ? backup : null,
            LowMemory: true);
    }

    /// <summary>The main case: 110C on 5.2 → 40F on 8.0, internal → lan, wan1 → wan, wan2 → lan3 (taken out of the switch).</summary>
    public static RebuildRequest Office110C(SecretPlan? secrets = null) => new(
        Load("110c-5.2-office.conf"),
        "5.02",
        Target40F(),
        [new InterfacePlan("wan1", "wan"), new InterfacePlan("internal", "lan"), new InterfacePlan("wan2", "lan3")],
        secrets ?? new SecretPlan { Generate = true },
        new RebuildOptions(ManagementInterface: "lan"));

    public static string Golden(string file) => Path.Combine(AppContext.BaseDirectory, "Golden", "Rebuild", file);
}
