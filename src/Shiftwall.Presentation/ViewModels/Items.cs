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

/// <summary>A source physical port in the mapping step.</summary>
public sealed class SourcePortItem(string name, int uses) : ObservableObject
{
    private string? _target;
    private bool _blocking;

    public string Name { get; } = name;

    public int Uses { get; } = uses;

    public bool IsUsed => Uses > 0;

    public string? Target
    {
        get => _target;
        set
        {
            if (Set(ref _target, string.IsNullOrEmpty(value) ? null : value)) OnPropertyChanged(nameof(IsMapped));
        }
    }

    public bool IsMapped => _target is not null;

    /// <summary>Used but not mapped: blocks the next step.</summary>
    public bool IsBlocking { get => _blocking; set => Set(ref _blocking, value); }

    /// <summary>Addressing on the target (rebuild mode only).</summary>
    public AddressingItem Addressing { get; } = new();
}

/// <summary>A target physical port in the mapping step.</summary>
public sealed class TargetPortItem(string name) : ObservableObject
{
    private string? _assignedFrom;

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
