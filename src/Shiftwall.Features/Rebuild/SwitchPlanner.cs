using System.Globalization;
using System.Text.RegularExpressions;
using Shiftwall.Core.Config;
using Shiftwall.Features.Migration;

namespace Shiftwall.Features.Rebuild;

/// <summary>What a name typed in the mapping (or a recreated switch) becomes on the target.</summary>
public enum NewInterfaceKind
{
    /// <summary>A switch whose kind Shiftwall chooses: hardware when possible, otherwise software.</summary>
    Switch,
    HardwareSwitch,
    SoftwareSwitch,
    Vlan,
}

/// <summary>
/// An interface that does not exist on the target and is created by the script (section 6b): a switch with the
/// chosen target ports, or a VLAN. A name equal to an existing target switch changes the ports of that switch.
/// </summary>
public sealed record NewInterfacePlan(string Name, NewInterfaceKind Kind, IReadOnlyList<string> Ports, string? Parent = null, int? VlanId = null);

/// <summary>How a logical switch of the source groups its ports.</summary>
public enum SourceSwitchKind
{
    /// <summary>config system virtual-switch, or an interface of type hard-switch.</summary>
    Hardware,

    /// <summary>config system switch-interface (interface of type switch).</summary>
    Software,

    /// <summary>The "internal" interface of older models with internal-switch-mode switch or hub (5.2 default: switch, CLI Reference p. 524).</summary>
    InternalSwitchMode,
}

/// <summary>A logical switch of the source, with its member ports when the backup lists them.</summary>
public sealed record SourceSwitch(string Name, SourceSwitchKind Kind, IReadOnlyList<string> Members);

/// <summary>What a target port is before the plan and what the plan does with it.</summary>
public enum PortState
{
    /// <summary>Standalone and not used by the factory configuration.</summary>
    Free,

    /// <summary>Member of a hardware or software switch of the target.</summary>
    InSwitch,

    /// <summary>Referenced by the factory configuration (policy, DHCP server, address, route...).</summary>
    UsedOnTarget,
}

/// <param name="Switch">Switch the port belongs to on the target, if any.</param>
/// <param name="UsedBy">Where the factory configuration references the port ("firewall policy 1").</param>
/// <param name="ChosenBy">What the plan does with the port: source interface mapped to it, or switch it goes into.</param>
public sealed record PortStatus(string Port, PortState State, string? Switch, string? UsedBy, string? ChosenBy);

/// <summary>A switch the script creates or changes.</summary>
/// <param name="Hardware">config system virtual-switch (hardware switch) or config system switch-interface (software switch).</param>
/// <param name="Existing">The switch already exists on the target and only its ports change.</param>
/// <param name="Ports">Ports to add (existing switch) or all the ports (new switch).</param>
/// <param name="PhysicalSwitch">Value of "set physical-switch" of a new hardware switch, from the factory-default backup.</param>
public sealed record SwitchChange(string Name, bool Hardware, bool Existing, IReadOnlyList<string> Ports, IReadOnlyList<string> Removed, IReadOnlyList<string> Remaining, string? PhysicalSwitch);

/// <summary>The kind chosen for a new switch, with the reasons shown to the user.</summary>
public sealed record SwitchDecision(string Name, bool Hardware, IReadOnlyList<MigrationFinding> Findings);

/// <summary>Result of <see cref="SwitchPlanner.Plan"/>: what happens to every target port and which switches change.</summary>
public sealed class SwitchLayout
{
    public List<PortStatus> Ports { get; } = [];

    /// <summary>Existing switches that lose ports and new or changed switches, in the order the script writes them.</summary>
    public List<SwitchChange> Changes { get; } = [];

    public List<SwitchDecision> Decisions { get; } = [];

    /// <summary>Ports that go into a switch: written first with mode static and IP 0.0.0.0/0.0.0.0, as the documentation requires.</summary>
    public List<string> Prepared { get; } = [];

    public List<NewInterfacePlan> Vlans { get; } = [];

    /// <summary>Names of the interfaces created by the plan (switches and VLANs).</summary>
    public HashSet<string> Created { get; } = new(StringComparer.Ordinal);

    public List<MigrationFinding> Findings { get; } = [];

    public bool HasErrors => Findings.Any(f => f.Severity == FindingSeverity.Error);
}

/// <summary>
/// Plans the switches and ports of the target (section 6b): ports taken out of factory switches, recreated switches
/// (hardware when the model supports it with those ports, software otherwise), split switches and new VLANs.
/// Used both by the rebuild engine and by the wizard, so the port states shown are the ones the script uses.
/// </summary>
/// <remarks>
/// Constraints, all from official sources listed in docs/rebuild-sources.md:
/// a port added to a hardware or software switch must not be referenced and must have IP 0.0.0.0/0.0.0.0 (Administration
/// Guide 8.0, "Hardware switch" and "Software switch"); on the 40F its mode must be static (Hardware Acceleration 8.0.2,
/// "FortiGate 40F fast path architecture"); ports are removed from a hardware switch with config port / delete; a hardware
/// switch cannot be emptied, all its ports are freed only by deleting it after removing its references (Fortinet Community
/// Technical Tip "How to delete the default virtual hardware switch").
/// </remarks>
public static partial class SwitchPlanner
{
    public const string FastPath40F = "https://docs.fortinet.com/document/fortigate/8.0.2/hardware-acceleration/965349/fortigate-40f-fast-path-architecture";

    /// <summary>
    /// Front panel ports that share one switch chip, per model. Only models with an official source are listed: for the
    /// others a hardware switch cannot be verified and a software switch is used.
    /// </summary>
    private static readonly Dictionary<string, string[][]> SwitchChips = new(StringComparer.OrdinalIgnoreCase)
    {
        // "Five 10/100/1000BASE-T Copper (WAN, A, and 1-3) connected to the SOC4 ... config system virtual-switch to create a
        // virtual hardware switch that can include any front panel interface connected to the SOC4."
        ["FG-40F"] = [["wan", "a", "lan1", "lan2", "lan3"]],
    };

    /// <summary>Source of the chip groups of a model, or null when the model is not listed.</summary>
    public static string? ChipSource(string? model) => model is not null && SwitchChips.ContainsKey(model) ? FastPath40F : null;

    /// <summary>Parameters of the factory configuration that reference an interface.</summary>
    private static readonly HashSet<string> InterfaceParams = new(StringComparer.Ordinal)
    {
        "interface", "srcintf", "dstintf", "device", "associated-interface", "extintf", "member", "source-interface",
    };

    private static readonly HashSet<string> SwitchTables = new(StringComparer.Ordinal) { "system interface", "system virtual-switch", "system switch-interface" };

    public static SwitchLayout Plan(RebuildTarget target, IReadOnlyList<InterfacePlan> interfaces, IReadOnlyList<NewInterfacePlan> created)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(interfaces);
        ArgumentNullException.ThrowIfNull(created);
        var layout = new SwitchLayout();
        var hardware = target.Switches.ToDictionary(s => s.Name, s => s.Members.ToList(), StringComparer.Ordinal);
        var software = SoftwareSwitches(target.DefaultBackup);
        var ports = PhysicalPorts(target, hardware, software);
        var usedOnTarget = PortReferences(target.DefaultBackup, ports);
        string? SwitchOf(string port) =>
            hardware.FirstOrDefault(kv => kv.Value.Contains(port)).Key ?? software.FirstOrDefault(kv => kv.Value.Contains(port)).Key;

        // Who takes each port: a source interface mapped to it, a split, or a switch it goes into.
        var claims = new Dictionary<string, string>(StringComparer.Ordinal);
        void Claim(string port, string by)
        {
            if (claims.TryGetValue(port, out var other) && other != by)
            {
                layout.Findings.Add(MigrationFinding.Error(RebuildCodes.PortUsedTwice, port, other, by));
                return;
            }

            claims[port] = by;
        }

        foreach (var p in interfaces)
        {
            if (p.Target is { } t && ports.Contains(t)) Claim(t, p.Source);
            foreach (var extra in p.SplitPorts ?? [])
            {
                if (!ports.Contains(extra))
                {
                    layout.Findings.Add(MigrationFinding.Error(RebuildCodes.NotAPort, extra, p.Source));
                    continue;
                }

                Claim(extra, p.Source);
                layout.Findings.Add(MigrationFinding.Warning(RebuildCodes.SplitPortNoAddress, extra, p.Source, p.Target ?? "-"));
            }
        }

        // Desired members of the switches the plan creates or changes.
        var wanted = new Dictionary<string, (NewInterfacePlan Plan, List<string> Ports)>(StringComparer.Ordinal);
        foreach (var c in created)
        {
            if (!ValidName(c.Name))
            {
                layout.Findings.Add(MigrationFinding.Error(RebuildCodes.InvalidNewName, c.Name));
                continue;
            }

            if (wanted.ContainsKey(c.Name) || layout.Vlans.Any(v => v.Name == c.Name))
            {
                layout.Findings.Add(MigrationFinding.Error(RebuildCodes.DuplicateTarget, c.Name));
                continue;
            }

            if (c.Kind == NewInterfaceKind.Vlan)
            {
                PlanVlan(c, target, hardware, software, created, layout);
                continue;
            }

            var existingHw = hardware.ContainsKey(c.Name);
            var existingSw = software.ContainsKey(c.Name);
            if ((existingHw && c.Kind == NewInterfaceKind.SoftwareSwitch) || (existingSw && c.Kind == NewInterfaceKind.HardwareSwitch)
                || (!existingHw && !existingSw && target.Interfaces.Contains(c.Name, StringComparer.Ordinal)))
            {
                layout.Findings.Add(MigrationFinding.Error(RebuildCodes.NameTakenOnTarget, c.Name));
                continue;
            }

            if (c.Ports.Count == 0)
            {
                layout.Findings.Add(MigrationFinding.Error(RebuildCodes.SwitchWithoutPorts, c.Name));
                continue;
            }

            var list = new List<string>();
            foreach (var port in c.Ports.Distinct(StringComparer.Ordinal))
            {
                if (!ports.Contains(port))
                {
                    layout.Findings.Add(MigrationFinding.Error(RebuildCodes.NotAPort, port, c.Name));
                    continue;
                }

                // A port already in this existing switch stays where it is.
                var current = SwitchOf(port);
                if (current != c.Name && usedOnTarget.TryGetValue(port, out var where))
                {
                    layout.Findings.Add(MigrationFinding.Error(RebuildCodes.PortUsedOnTarget, port, where, c.Name));
                    continue;
                }

                Claim(port, c.Name);
                list.Add(port);
            }

            wanted[c.Name] = (c, list);
        }

        // Existing switches: ports taken by something else leave them; a changed switch keeps only the chosen ports.
        var removed = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var (name, members) in hardware.Select(kv => (kv.Key, kv.Value)).Concat(software.Select(kv => (kv.Key, kv.Value))))
        {
            var keep = wanted.TryGetValue(name, out var w) ? w.Ports : null;
            var gone = members.Where(m => (claims.TryGetValue(m, out var by) && by != name) || (keep is not null && !keep.Contains(m))).ToList();
            var remaining = members.Except(gone).Concat(keep?.Except(members) ?? []).ToList();
            if (gone.Count > 0 && remaining.Count == 0)
            {
                layout.Findings.Add(MigrationFinding.Error(RebuildCodes.SwitchWouldBeEmpty, name));
                continue;
            }

            if (gone.Count == 0) continue;
            removed[name] = gone;
            if (keep is null) layout.Findings.Add(MigrationFinding.Warning(RebuildCodes.PortsRemovedFromSwitch, string.Join(", ", gone), name));
        }

        // Removals first: a port can join another switch only once it has left the first one.
        foreach (var (name, gone) in removed)
        {
            var isHw = hardware.ContainsKey(name);
            var members = isHw ? hardware[name] : software[name];
            if (wanted.ContainsKey(name)) continue; // written together with the additions below
            layout.Changes.Add(new SwitchChange(name, isHw, true, [], gone, [.. members.Except(gone)], null));
        }

        var physicalSwitch = PhysicalSwitchName(target.DefaultBackup);
        foreach (var (name, (plan, list)) in wanted)
        {
            if (list.Count == 0 || layout.Findings.Any(f => f.Severity == FindingSeverity.Error && f.Code == RebuildCodes.SwitchWouldBeEmpty && f.Arguments[0] == name)) continue;
            if (hardware.TryGetValue(name, out var hwMembers) || software.TryGetValue(name, out hwMembers))
            {
                var isHw = hardware.ContainsKey(name);
                var add = list.Except(hwMembers).ToList();
                var gone = removed.GetValueOrDefault(name) ?? [];
                if (add.Count == 0 && gone.Count == 0) continue;
                layout.Prepared.AddRange(add.Where(p => !layout.Prepared.Contains(p)));
                layout.Changes.Add(new SwitchChange(name, isHw, true, add, gone, list, null));
                layout.Findings.Add(MigrationFinding.Info(RebuildCodes.SwitchPortsChanged, name, string.Join(", ", list)));
                continue;
            }

            var decision = Decide(plan, list, target.ModelId, physicalSwitch);
            layout.Decisions.Add(decision);
            layout.Findings.AddRange(decision.Findings);
            if (decision.Findings.Any(f => f.Severity == FindingSeverity.Error)) continue;
            layout.Prepared.AddRange(list.Where(p => !layout.Prepared.Contains(p)));
            layout.Changes.Add(new SwitchChange(name, decision.Hardware, false, list, [], list, decision.Hardware ? physicalSwitch : null));
            layout.Created.Add(name);
        }

        foreach (var v in layout.Vlans) layout.Created.Add(v.Name);

        foreach (var port in ports.OrderBy(p => p, StringComparer.Ordinal))
        {
            var sw = SwitchOf(port);
            var used = usedOnTarget.GetValueOrDefault(port);
            var state = sw is not null ? PortState.InSwitch : used is not null ? PortState.UsedOnTarget : PortState.Free;
            layout.Ports.Add(new PortStatus(port, state, sw, used, claims.GetValueOrDefault(port)));
        }

        return layout;
    }

    /// <summary>Hardware switch when the model has a documented switch chip holding all the ports, software switch otherwise.</summary>
    private static SwitchDecision Decide(NewInterfacePlan plan, List<string> ports, string? model, string? physicalSwitch)
    {
        var joined = string.Join(", ", ports);
        var reasons = new List<MigrationFinding>();
        if (model is null || !SwitchChips.TryGetValue(model, out var groups))
        {
            reasons.Add(MigrationFinding.Warning(RebuildCodes.SwitchChipUnknown, plan.Name, joined, model ?? "?"));
        }
        else if (!groups.Any(g => ports.All(p => g.Contains(p, StringComparer.Ordinal))))
        {
            reasons.Add(MigrationFinding.Warning(RebuildCodes.SwitchDifferentChips, plan.Name, joined, model));
        }
        else if (physicalSwitch is null)
        {
            reasons.Add(MigrationFinding.Warning(RebuildCodes.SwitchNoPhysicalSwitch, plan.Name, joined));
        }

        var possible = reasons.Count == 0;
        return plan.Kind switch
        {
            NewInterfaceKind.HardwareSwitch when !possible => new(plan.Name, true, [.. reasons, MigrationFinding.Error(RebuildCodes.HardwareSwitchNotPossible, plan.Name, joined)]),
            NewInterfaceKind.SoftwareSwitch when possible => new(plan.Name, false, [MigrationFinding.Warning(RebuildCodes.SoftwareSwitchForced, plan.Name, joined)]),
            NewInterfaceKind.SoftwareSwitch => new(plan.Name, false, [.. reasons, MigrationFinding.Warning(RebuildCodes.SoftwareSwitchUsed, plan.Name, joined)]),
            _ when possible => new(plan.Name, true, [MigrationFinding.Info(RebuildCodes.HardwareSwitchChosen, plan.Name, joined, model!)]),
            _ => new(plan.Name, false, [.. reasons, MigrationFinding.Warning(RebuildCodes.SoftwareSwitchUsed, plan.Name, joined)]),
        };
    }

    private static void PlanVlan(NewInterfacePlan c, RebuildTarget target, Dictionary<string, List<string>> hardware, Dictionary<string, List<string>> software, IReadOnlyList<NewInterfacePlan> created, SwitchLayout layout)
    {
        if (target.Interfaces.Contains(c.Name, StringComparer.Ordinal) || hardware.ContainsKey(c.Name) || software.ContainsKey(c.Name))
        {
            layout.Findings.Add(MigrationFinding.Error(RebuildCodes.NameTakenOnTarget, c.Name));
            return;
        }

        var parentOk = c.Parent is { } p && p != c.Name
            && (target.Interfaces.Contains(p, StringComparer.Ordinal) || created.Any(x => x.Name == p && x.Kind != NewInterfaceKind.Vlan));
        if (!parentOk || c.VlanId is not (>= 1 and <= 4094))
        {
            layout.Findings.Add(MigrationFinding.Error(RebuildCodes.InvalidVlan, c.Name, c.Parent ?? "-", c.VlanId?.ToString(CultureInfo.InvariantCulture) ?? "-"));
            return;
        }

        layout.Vlans.Add(c);
    }

    /// <summary>Interface names on the target are at most 15 characters (CLI Reference 8.0) and written without characters the CLI treats specially.</summary>
    public static bool ValidName(string? name) => name is { Length: > 0 and <= 15 } && NameRegex().IsMatch(name);

    [GeneratedRegex("^[A-Za-z0-9_.-]+$")]
    private static partial Regex NameRegex();

    /// <summary>Logical switches of the source configuration, each shown as an item of its own in the mapping.</summary>
    public static IReadOnlyList<SourceSwitch> SourceSwitches(ConfigDocument doc)
    {
        ArgumentNullException.ThrowIfNull(doc);
        var result = new List<SourceSwitch>();
        foreach (var e in doc.GlobalScope.Block("system virtual-switch")?.Entries ?? [])
        {
            result.Add(new SourceSwitch(e.Key, SourceSwitchKind.Hardware, [.. e.Block("port")?.Entries.Select(p => p.Key) ?? []]));
        }

        foreach (var (name, members) in SoftwareSwitches(doc)) result.Add(new SourceSwitch(name, SourceSwitchKind.Software, members));
        var declared = InterfaceInventory.Declared(doc);
        foreach (var i in declared.Where(i => i.Type == "hard-switch" && result.All(r => r.Name != i.Name)))
        {
            result.Add(new SourceSwitch(i.Name, SourceSwitchKind.Hardware, []));
        }

        var mode = doc.GlobalScope.Block("system global")?.Field("internal-switch-mode")?.Values.FirstOrDefault() ?? "switch";
        if (mode != "interface" && declared.FirstOrDefault(i => i.Name == "internal" && i.Type == "physical") is { } internalPort && result.All(r => r.Name != "internal"))
        {
            result.Add(new SourceSwitch(internalPort.Name, SourceSwitchKind.InternalSwitchMode, []));
        }

        return result;
    }

    /// <summary>Software switches of the factory-default backup (config system switch-interface).</summary>
    public static Dictionary<string, List<string>> SoftwareSwitches(ConfigDocument? backup)
    {
        var result = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var e in backup?.GlobalScope.Block("system switch-interface")?.Entries ?? [])
        {
            result[e.Key] = [.. e.Field("member")?.Values ?? []];
        }

        return result;
    }

    /// <summary>Physical ports of the target: declared "physical" in the factory backup, or the database list without the switches.</summary>
    public static HashSet<string> PhysicalPorts(RebuildTarget target, IReadOnlyDictionary<string, List<string>>? hardware = null, IReadOnlyDictionary<string, List<string>>? software = null)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (target.DefaultBackup is { } doc)
        {
            return InterfaceInventory.Declared(doc).Where(i => i.Type == "physical").Select(i => i.Name).ToHashSet(StringComparer.Ordinal);
        }

        var switchNames = target.Switches.Select(s => s.Name).Concat(software?.Keys ?? []).ToHashSet(StringComparer.Ordinal);
        var result = target.Interfaces.Where(i => !switchNames.Contains(i)).ToHashSet(StringComparer.Ordinal);
        foreach (var m in target.Switches.SelectMany(s => s.Members)) result.Add(m);
        return result;
    }

    /// <summary>"set physical-switch" of the factory hardware switches, when they all use the same one.</summary>
    public static string? PhysicalSwitchName(ConfigDocument? backup)
    {
        var names = (backup?.GlobalScope.Block("system virtual-switch")?.Entries ?? [])
            .Select(e => e.Field("physical-switch")?.Values.FirstOrDefault()).Where(n => !string.IsNullOrEmpty(n)).Distinct(StringComparer.Ordinal).ToList();
        return names.Count == 1 ? names[0] : null;
    }

    /// <summary>Where the factory configuration references each port ("firewall policy 1"), outside the interface tables.</summary>
    public static Dictionary<string, string> PortReferences(ConfigDocument? backup, IReadOnlySet<string> ports)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        if (backup is null) return result;
        foreach (var block in backup.Blocks.Where(b => !SwitchTables.Contains(b.Path)))
        {
            foreach (var (owner, field) in Fields(block))
            {
                if (!InterfaceParams.Contains(field.Name)) continue;
                foreach (var v in field.Values.Where(ports.Contains)) result.TryAdd(v, owner);
            }
        }

        return result;
    }

    private static IEnumerable<(string Owner, FieldNode Field)> Fields(ConfigBlock block)
    {
        foreach (var f in block.Fields) yield return (block.Path, f);
        foreach (var e in block.Entries)
        {
            foreach (var f in e.Fields) yield return ($"{block.Path} {e.Key}", f);
        }
    }
}
