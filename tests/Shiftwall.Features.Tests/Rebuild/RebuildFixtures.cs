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
        var backup = Load("40f-8.0-target-synthetic.fgtcfg");
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
        Load("110c-5.2-office.fgtcfg"),
        "5.02",
        Target40F(),
        [new InterfacePlan("wan1", "wan"), new InterfacePlan("internal", "lan"), new InterfacePlan("wan2", "lan3")],
        secrets ?? new SecretPlan { Generate = true },
        new RebuildOptions(ManagementInterface: "lan"));

    /// <summary>
    /// Variant 2: "internal" recreated on the 40F as its own switch with lan1 + lan2. The factory switch "lan" cannot be
    /// emptied, so it keeps lan3 and takes wan2 (guest internet) as a separate network.
    /// </summary>
    public static RebuildRequest Office110CRecreated(NewInterfaceKind kind = NewInterfaceKind.Switch) => Office110C() with
    {
        Interfaces = [new InterfacePlan("wan1", "wan"), new InterfacePlan("internal", "internal"), new InterfacePlan("wan2", "lan")],
        NewInterfaces = [new NewInterfacePlan("internal", kind, ["lan1", "lan2"])],
        Options = new RebuildOptions(ManagementInterface: "internal"),
    };

    /// <summary>Variant 3: "internal" split into separate ports: lan1 takes its address, lan2 stays without one; lan keeps lan3 for wan2.</summary>
    public static RebuildRequest Office110CSplit() => Office110C() with
    {
        Interfaces = [new InterfacePlan("wan1", "wan"), new InterfacePlan("internal", "lan1", SplitPorts: ["lan2"]), new InterfacePlan("wan2", "lan")],
        Options = new RebuildOptions(ManagementInterface: "lan1"),
    };

    public static string Golden(string file) => Path.Combine(AppContext.BaseDirectory, "Golden", "Rebuild", file);
}
