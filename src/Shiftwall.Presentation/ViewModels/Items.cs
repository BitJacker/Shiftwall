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
