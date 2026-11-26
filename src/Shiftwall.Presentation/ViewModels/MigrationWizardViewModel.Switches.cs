using System.Globalization;
using Shiftwall.Features.Migration;
using Shiftwall.Features.Rebuild;
using Shiftwall.Presentation.Localization;

namespace Shiftwall.Presentation.ViewModels;

/// <summary>Section 6b of the rebuild: logical switches of the source and new interfaces typed in the mapping.</summary>
public sealed partial class MigrationWizardViewModel
{
    private SwitchLayout? _layout;

    public IReadOnlyList<Choice<SwitchAction>> SwitchActionChoices { get; } =
    [
        new(SwitchAction.Map, "Rebuild.Switch.Map"),
        new(SwitchAction.Recreate, "Rebuild.Switch.Recreate"),
        new(SwitchAction.Split, "Rebuild.Switch.Split"),
    ];

    public IReadOnlyList<Choice<NewInterfaceKind>> SwitchKindChoices { get; } =
    [
        new(NewInterfaceKind.Switch, "Rebuild.Create.Switch"),
        new(NewInterfaceKind.HardwareSwitch, "Rebuild.Create.Hardware"),
        new(NewInterfaceKind.SoftwareSwitch, "Rebuild.Create.Software"),
    ];

    public IReadOnlyList<Choice<NewInterfaceKind>> CreateKindChoices { get; } =
    [
        new(NewInterfaceKind.Switch, "Rebuild.Create.Switch"),
        new(NewInterfaceKind.HardwareSwitch, "Rebuild.Create.Hardware"),
        new(NewInterfaceKind.SoftwareSwitch, "Rebuild.Create.Software"),
        new(NewInterfaceKind.Vlan, "Rebuild.Create.Vlan"),
    ];

    /// <summary>Ports and switches of the target as planned now: the states shown are the ones the script uses.</summary>
    public SwitchLayout? SwitchLayout => _layout;

    private RebuildTarget? CurrentRebuildTarget() =>
        Target.BuildProfile() is { } t ? RebuildTargets.From(t, Target.IsBackupMode ? Target.Backup?.Document : null) : null;

    private static string SwitchInfo(SourceSwitch s) => s.Kind switch
    {
        SourceSwitchKind.Software => Loc.F("Rebuild.Switch.Software", Members(s)),
        SourceSwitchKind.InternalSwitchMode => Loc.T("Rebuild.Switch.Internal"),
        _ => Loc.F("Rebuild.Switch.Hardware", Members(s)),
    };

    private static string Members(SourceSwitch s) => s.Members.Count == 0 ? Loc.T("Rebuild.Switch.NoMembers") : string.Join(", ", s.Members);

    /// <summary>The interface plans and the interfaces to create, from the rows of the mapping.</summary>
    internal (List<InterfacePlan> Plans, List<NewInterfacePlan> Created) BuildPlans()
    {
        var plans = new List<InterfacePlan>();
        var created = new List<NewInterfacePlan>();
        foreach (var s in SourcePorts)
        {
            var ports = s.CheckedPorts;
            IReadOnlyList<string>? split = null;
            var target = s.Target;
            if (s.IsSplit)
            {
                target = ports.FirstOrDefault();
                split = [.. ports.Skip(1)];
            }

            plans.Add(new InterfacePlan(
                s.Name,
                target,
                s.Addressing.Mode,
                s.Addressing.Address,
                string.IsNullOrWhiteSpace(s.Addressing.Gateway) ? null : s.Addressing.Gateway.Trim(),
                s.Addressing.NameServers.Split([' ', ',', ';'], StringSplitOptions.RemoveEmptyEntries),
                split));

            if (target is null || s.IsSplit || created.Any(c => c.Name == target)) continue;
            if (s.IsRecreate)
            {
                created.Add(new NewInterfacePlan(target, s.CreateKind == NewInterfaceKind.Vlan ? NewInterfaceKind.Switch : s.CreateKind, ports));
            }
            else if (s.NeedsCreate)
            {
                created.Add(s.CreateKind == NewInterfaceKind.Vlan
                    ? new NewInterfacePlan(target, NewInterfaceKind.Vlan, [], s.VlanParent.Trim(), int.TryParse(s.VlanId, NumberStyles.None, CultureInfo.InvariantCulture, out var id) ? id : null)
                    : new NewInterfacePlan(target, s.CreateKind, ports));
            }
        }

        return (plans, created);
    }

    /// <summary>Runs the switch planner on the current rows and shows its findings and the state of every port.</summary>
    private void PublishSwitchLayout()
    {
        var names = TargetPorts.Select(t => t.Name).ToHashSet(StringComparer.Ordinal);
        foreach (var s in SourcePorts) s.NeedsCreate = !s.IsSplit && (s.IsRecreate || (s.Target is { } t && !names.Contains(t)));

        var target = CurrentRebuildTarget();
        if (target is null)
        {
            _layout = null;
            return;
        }

        var (plans, created) = BuildPlans();
        _layout = SwitchPlanner.Plan(target, plans, created);
        foreach (var f in _layout.Findings) MappingFindings.Add(MessageItem.From(f));

        var byPort = _layout.Ports.ToDictionary(p => p.Port, StringComparer.Ordinal);
        foreach (var row in SourcePorts)
        {
            foreach (var c in row.PortChoices) c.Status = byPort.TryGetValue(c.Port, out var p) ? PortText(p) : "";
        }

        var switches = target.Switches.ToDictionary(s => s.Name, s => s.Members, StringComparer.Ordinal);
        foreach (var t in TargetPorts)
        {
            t.Status = byPort.TryGetValue(t.Name, out var p) ? PortText(p)
                : switches.TryGetValue(t.Name, out var m) ? Loc.F("Rebuild.Port.Switch", string.Join(", ", m))
                : "";
        }

        OnPropertyChanged(nameof(SwitchLayout));
    }

    private static string PortText(PortStatus p)
    {
        var state = p.State switch
        {
            PortState.InSwitch => Loc.F("Rebuild.Port.InSwitch", p.Switch ?? ""),
            PortState.UsedOnTarget => Loc.F("Rebuild.Port.Used", p.UsedBy ?? ""),
            _ => Loc.T("Rebuild.Port.Free"),
        };
        return p.ChosenBy is { } by ? state + " · " + Loc.F("Rebuild.Port.ChosenBy", by) : state;
    }

    /// <summary>Target ports offered as checkboxes in every row (the switch panels).</summary>
    private void FillPortChoices(SourcePortItem row, IReadOnlyCollection<string> ports, IReadOnlyCollection<string>? previous)
    {
        foreach (var p in ports.OrderBy(p => p, StringComparer.Ordinal))
        {
            var c = new PortChoiceItem(p) { IsChecked = previous?.Contains(p) == true };
            c.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(PortChoiceItem.IsChecked)) OnPortChoiceChanged(row);
            };
            row.PortChoices.Add(c);
        }
    }

    private void OnPortChoiceChanged(SourcePortItem row)
    {
        if (_suppressRowEvents) return;
        if (row.IsSplit) SetTarget(row, row.CheckedPorts.FirstOrDefault());
        Reanalyze();
    }

    private void SetTarget(SourcePortItem row, string? target)
    {
        _suppressRowEvents = true;
        try
        {
            row.Target = target;
        }
        finally
        {
            _suppressRowEvents = false;
        }
    }

    /// <summary>Changes of a row other than its target: switch action, kind, VLAN fields, addressing.</summary>
    private void OnRowSettingChanged(SourcePortItem row, string? property)
    {
        if (_suppressRowEvents) return;
        switch (property)
        {
            case nameof(SourcePortItem.Action):
                var names = TargetPorts.Select(t => t.Name).ToHashSet(StringComparer.Ordinal);
                if (row.Action == SwitchAction.Recreate && (row.Target is null || names.Contains(row.Target)))
                {
                    // Same name as on the source by default (editable: the name of an existing switch changes its ports).
                    SetTarget(row, row.Name);
                }
                else if (row.Action == SwitchAction.Split)
                {
                    SetTarget(row, row.CheckedPorts.FirstOrDefault());
                }
                else if (row.Action == SwitchAction.Map && row.Target is { } current && !names.Contains(current))
                {
                    SetTarget(row, null);
                }

                Reanalyze();
                break;
            case nameof(SourcePortItem.CreateKind):
            case nameof(SourcePortItem.VlanParent):
            case nameof(SourcePortItem.VlanId):
                Reanalyze();
                break;
        }
    }

    private void HookRow(SourcePortItem row)
    {
        row.PropertyChanged += OnSourcePortChanged;
        row.PropertyChanged += (_, e) => OnRowSettingChanged(row, e.PropertyName);
    }

    /// <summary>Source rows of the rebuild mode: physical ports plus the logical switches, each with its own item.</summary>
    private IEnumerable<(string Name, string? SwitchInfo)> RebuildSourceRows()
    {
        if (_source is null) yield break;
        var switches = SwitchPlanner.SourceSwitches(_source.Document).ToDictionary(s => s.Name, StringComparer.Ordinal);
        var physical = _source.PhysicalInterfaces.Select(p => p.Name).ToList();
        foreach (var name in physical) yield return (name, switches.TryGetValue(name, out var s) ? SwitchInfo(s) : null);
        foreach (var s in switches.Values.Where(s => !physical.Contains(s.Name, StringComparer.Ordinal)))
        {
            yield return (s.Name, SwitchInfo(s));
        }
    }

    /// <summary>For tests and the screenshot run: the row of a source interface.</summary>
    public SourcePortItem? Row(string name) => SourcePorts.FirstOrDefault(s => s.Name == name);

}
