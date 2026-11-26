using System.Collections.ObjectModel;
using Shiftwall.Core.Config;
using Shiftwall.Features.Migration;
using Shiftwall.Presentation.Localization;
using Shiftwall.Presentation.Mvvm;

namespace Shiftwall.Presentation.ViewModels;

public enum ItemSeverity
{
    Info,
    Warning,
    Error,
}

/// <summary>A message row (diagnostic or finding) whose text follows the UI language.</summary>
public sealed class MessageItem : ObservableObject
{
    private readonly Func<string> _text;

    public MessageItem(ItemSeverity severity, string code, Func<string> text)
    {
        Severity = severity;
        Code = code;
        _text = text;
        Loc.Instance.PropertyChanged += (_, _) => OnPropertyChanged(nameof(Text));
    }

    public ItemSeverity Severity { get; }

    public string Code { get; }

    public string Text => _text();

    public static MessageItem From(ConfigDiagnostic d) => new(
        d.Severity switch { DiagnosticSeverity.Error => ItemSeverity.Error, DiagnosticSeverity.Warning => ItemSeverity.Warning, _ => ItemSeverity.Info },
        d.Code,
        () => DiagnosticFormatter.Format(d, Loc.Instance.Culture));

    public static MessageItem From(MigrationFinding f) => new(
        f.Severity switch { FindingSeverity.Error => ItemSeverity.Error, FindingSeverity.Warning => ItemSeverity.Warning, _ => ItemSeverity.Info },
        f.Code,
        () => f.Format(Loc.Instance.Culture));
}

/// <summary>What the rebuild does with a logical switch of the source (section 6b).</summary>
public enum SwitchAction
{
    /// <summary>On an interface or switch that already exists on the target.</summary>
    Map,

    /// <summary>Created again on the target with the chosen ports.</summary>
    Recreate,

    /// <summary>Each chosen port becomes an interface of its own; the address goes on the first one.</summary>
    Split,
}

/// <summary>A target port offered with a checkbox, with what it is now and what the plan does with it.</summary>
public sealed class PortChoiceItem(string port) : ObservableObject
{
    private bool _checked;
    private string _status = "";

    public string Port { get; } = port;

    public bool IsChecked { get => _checked; set => Set(ref _checked, value); }

    public string Status { get => _status; set => Set(ref _status, value); }
}

/// <summary>A source interface in the mapping step: a physical port, or a logical switch (rebuild mode).</summary>
public sealed class SourcePortItem(string name, int uses, string? switchInfo = null) : ObservableObject
{
    private string? _target;
    private bool _blocking;
    private SwitchAction _action;
    private Features.Rebuild.NewInterfaceKind _createKind;
    private bool _needsCreate;
    private string _vlanParent = "";
    private string _vlanId = "";

    public string Name { get; } = name;

    public int Uses { get; } = uses;

    public bool IsUsed => Uses > 0;

    /// <summary>Kind and members of a source logical switch, null for a plain port.</summary>
    public string? SwitchInfo { get; } = switchInfo;

    public bool IsSwitch => SwitchInfo is not null;

    public string? Target
    {
        get => _target;
        set
        {
            if (Set(ref _target, string.IsNullOrWhiteSpace(value) ? null : value.Trim())) OnPropertyChanged(nameof(IsMapped));
        }
    }

    public bool IsMapped => _target is not null;

    /// <summary>Used but not mapped: blocks the next step.</summary>
    public bool IsBlocking { get => _blocking; set => Set(ref _blocking, value); }

    /// <summary>Addressing on the target (rebuild mode only).</summary>
    public AddressingItem Addressing { get; } = new();

    public SwitchAction Action
    {
        get => _action;
        set
        {
            if (Set(ref _action, value)) RaisePanel();
        }
    }

    /// <summary>What a name that does not exist on the target becomes.</summary>
    public Features.Rebuild.NewInterfaceKind CreateKind
    {
        get => _createKind;
        set
        {
            if (Set(ref _createKind, value)) RaisePanel();
        }
    }

    /// <summary>The target typed by hand does not exist on the target: the row offers to create it.</summary>
    public bool NeedsCreate
    {
        get => _needsCreate;
        set
        {
            if (Set(ref _needsCreate, value)) RaisePanel();
        }
    }

    public string VlanParent { get => _vlanParent; set => Set(ref _vlanParent, value ?? ""); }

    public string VlanId { get => _vlanId; set => Set(ref _vlanId, value ?? ""); }

    public bool IsSplit => IsSwitch && _action == SwitchAction.Split;

    public bool IsRecreate => IsSwitch && _action == SwitchAction.Recreate;

    /// <summary>The target combo is driven by the chosen ports when the switch is split.</summary>
    public bool CanEditTarget => !IsSplit;

    public bool ShowsPanel => IsRecreate || IsSplit || NeedsCreate;

    /// <summary>Kind choice limited to switches (recreated switch).</summary>
    public bool ShowsSwitchKind => IsRecreate;

    /// <summary>Kind choice with VLAN (name typed in the manual field).</summary>
    public bool ShowsAnyKind => !IsRecreate && !IsSplit && NeedsCreate;

    public bool ShowsVlan => ShowsAnyKind && _createKind == Features.Rebuild.NewInterfaceKind.Vlan;

    public bool ShowsPorts => IsSplit || ((IsRecreate || ShowsAnyKind) && _createKind != Features.Rebuild.NewInterfaceKind.Vlan);

    public ObservableCollection<PortChoiceItem> PortChoices { get; } = [];

    public IReadOnlyList<string> CheckedPorts => [.. PortChoices.Where(p => p.IsChecked).Select(p => p.Port)];

    private void RaisePanel()
    {
        foreach (var n in new[] { nameof(IsSplit), nameof(IsRecreate), nameof(CanEditTarget), nameof(ShowsPanel), nameof(ShowsSwitchKind), nameof(ShowsAnyKind), nameof(ShowsVlan), nameof(ShowsPorts) })
        {
            OnPropertyChanged(n);
        }
    }
}

/// <summary>A target interface in the mapping step, with its state (free, in a switch, used by the factory configuration).</summary>
public sealed class TargetPortItem(string name) : ObservableObject
{
    private string? _assignedFrom;
    private string _status = "";

    public string Name { get; } = name;

    public string? AssignedFrom
    {
        get => _assignedFrom;
        set
        {
            if (Set(ref _assignedFrom, value)) OnPropertyChanged(nameof(IsFree));
        }
    }

    public bool IsFree => _assignedFrom is null;

    public string Status { get => _status; set => Set(ref _status, value); }
}

/// <summary>A row of the batch result table.</summary>
public sealed class BatchRow(BatchItemResult result, string summary) : ObservableObject
{
    public string File { get; } = Path.GetFileName(result.SourcePath);

    public bool Succeeded { get; } = result.Succeeded;

    public string Summary { get; } = summary;

    public string? OutputPath { get; } = result.OutputPath;

    public string? ReportPath { get; } = result.ReportPath;
}

/// <summary>A choice shown in a combo box with a translated label.</summary>
public sealed class Choice<T>(T value, string key) : ObservableObject
{
    public T Value { get; } = value;

    public string Text => Loc.T(key);
}

/// <summary>Addressing of a source interface in the rebuild mode (kept, static, DHCP).</summary>
public sealed class AddressingItem : ObservableObject
{
    private Features.Rebuild.AddressingMode _mode;
    private string _address = "";
    private string _gateway = "";
    private string _nameServers = "";

    public Features.Rebuild.AddressingMode Mode
    {
        get => _mode;
        set
        {
            if (Set(ref _mode, value)) OnPropertyChanged(nameof(IsStatic));
        }
    }

    public bool IsStatic => _mode == Features.Rebuild.AddressingMode.Static;

    public string Address { get => _address; set => Set(ref _address, value ?? ""); }

    public string Gateway { get => _gateway; set => Set(ref _gateway, value ?? ""); }

    public string NameServers { get => _nameServers; set => Set(ref _nameServers, value ?? ""); }
}

/// <summary>A password category of the rebuild: typed twice, never shown, never logged.</summary>
public sealed class SecretRow(Features.Rebuild.SecretCategory category) : ObservableObject
{
    private string _password = "";
    private string _confirm = "";

    public Features.Rebuild.SecretCategory Category { get; } = category;

    public string Label => Loc.T("Rebuild.Secret." + Category);

    public bool CanGenerate => Features.Rebuild.SecretPlan.CanGenerate(Category);

    public bool HasPassword => _password.Length > 0;

    public void SetPassword(string value)
    {
        _password = value ?? "";
        Raise();
    }

    public void SetConfirm(string value)
    {
        _confirm = value ?? "";
        Raise();
    }

    internal string Password => _password;

    /// <summary>Empty, or the reasons why the typed password cannot be used.</summary>
    public string Problem
    {
        get
        {
            if (_password.Length == 0) return "";
            if (_password != _confirm) return Loc.T("Rebuild.Secret.Mismatch");
            var problems = Features.Rebuild.CliPassword.Check(_password);
            return problems.Count == 0 ? "" : string.Join("; ", problems.Select(p => Loc.T("Rebuild.Secret.Problem." + p)));
        }
    }

    public bool IsValid => _password.Length > 0 && Problem.Length == 0;

    private void Raise()
    {
        OnPropertyChanged(nameof(Problem));
        OnPropertyChanged(nameof(IsValid));
        OnPropertyChanged(nameof(HasPassword));
    }
}

/// <summary>A row of the rebuild counts table ("policy: 48 → 48").</summary>
public sealed record CountRow(string Path, int Source, int Rebuilt, int Converted, int Manual);

/// <summary>A row of the manual work list.</summary>
public sealed record ManualRow(string Path, string Object, string Reason, string Original);
