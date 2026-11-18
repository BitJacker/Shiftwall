using System.Collections.ObjectModel;
using System.Globalization;
using Shiftwall.Features.Toolbox;
using Shiftwall.Features.Toolbox.Audit;
using Shiftwall.Features.Toolbox.Export;
using Shiftwall.Presentation.Localization;
using Shiftwall.Presentation.Mvvm;
using Shiftwall.Presentation.Services;

namespace Shiftwall.Presentation.ViewModels.Toolbox;

/// <summary>A row of the compare result.</summary>
public sealed record CompareRow(string Location, SectionChangeKind Kind, string KindText, string Details);

/// <summary>Tool 1: compares two backups by section and entry.</summary>
public sealed class CompareViewModel : ObservableObject
{
    private int _version;
    private readonly IDialogService _dialogs;
    private SectionDiffResult? _result;
    private string _filter = "";
    private string _summary = "";

    public CompareViewModel(IDialogService dialogs)
    {
        _dialogs = dialogs;
        Left = new ConfigSlotViewModel(dialogs, "Compare.Left");
        Right = new ConfigSlotViewModel(dialogs, "Compare.Right");
        Left.Changed += (_, _) => ToolOutput.Run(RunAsync);
        Right.Changed += (_, _) => ToolOutput.Run(RunAsync);
        ExportCsvCommand = new RelayCommand(ExportCsv, () => _result is { Identical: false });
        Loc.Instance.PropertyChanged += (_, _) => Refresh();
    }

    public ConfigSlotViewModel Left { get; }

    public ConfigSlotViewModel Right { get; }

    public ObservableCollection<CompareRow> Rows { get; } = [];

    public RelayCommand ExportCsvCommand { get; }

    public string Summary { get => _summary; private set => Set(ref _summary, value); }

    public string Filter
    {
        get => _filter;
        set
        {
            if (Set(ref _filter, value)) Refresh();
        }
    }

    public async Task RunAsync()
    {
        if (!Left.IsLoaded || !Right.IsLoaded)
        {
            _version++;
            _result = null;
            Refresh();
            return;
        }

        var a = Left.Document!;
        var b = Right.Document!;
        var version = ++_version;
        var result = await Task.Run(() => SectionDiff.Compare(a, b));
        if (version != _version) return;
        _result = result;
        Refresh();
    }

    private void Refresh()
    {
        Rows.Clear();
        ExportCsvCommand.RaiseCanExecuteChanged();
        if (_result is null)
        {
            Summary = Loc.T("Compare.LoadBoth");
            return;
        }

        if (_result.Identical)
        {
            Summary = Loc.T("Compare.Identical");
            return;
        }

        Summary = Loc.F("Compare.Summary",
            _result.Changes.Count(c => c.Kind == SectionChangeKind.Added),
            _result.Changes.Count(c => c.Kind == SectionChangeKind.Removed),
            _result.Changes.Count(c => c.Kind == SectionChangeKind.Changed),
            _result.Sections.Count);
        foreach (var c in _result.Changes)
        {
            var row = ToRow(c);
            if (_filter.Length == 0 || row.Location.Contains(_filter, StringComparison.OrdinalIgnoreCase) || row.Details.Contains(_filter, StringComparison.OrdinalIgnoreCase))
            {
                Rows.Add(row);
            }
        }
    }

    private static CompareRow ToRow(SectionChange c)
    {
        var details = string.Join("\n", c.Fields.Select(f => c.Kind switch
        {
            SectionChangeKind.Added => $"+ {f.Field}: {f.After}",
            SectionChangeKind.Removed => $"− {f.Field}: {f.Before}",
            _ => $"{f.Field}: {f.Before ?? Loc.T("Compare.NotSet")} → {f.After ?? Loc.T("Compare.NotSet")}",
        }));
        return new CompareRow(c.Location, c.Kind, Loc.T("Compare.Kind." + c.Kind), details);
    }

    private void ExportCsv()
    {
        if (_result is null) return;
        var table = new ExportTable("compare", [Loc.T("Compare.Col.Location"), Loc.T("Compare.Col.Change"), Loc.T("Compare.Col.Field"), Loc.T("Compare.Col.Before"), Loc.T("Compare.Col.After")],
            [.. _result.Changes.SelectMany(c => c.Fields.Count == 0
                ? [(IReadOnlyList<string>)[c.Location, Loc.T("Compare.Kind." + c.Kind), "", "", ""]]
                : c.Fields.Select(f => (IReadOnlyList<string>)[c.Location, Loc.T("Compare.Kind." + c.Kind), f.Field, f.Before ?? "", f.After ?? ""]))]);
        ToolOutput.Save(_dialogs, "Compare.ExportCsv", "CSV (*.csv)|*.csv", ToolOutput.Suggest(Left.Path, "compare", ".csv"),
            CsvWriter.ToBytes(CsvWriter.Write(table)), [Left.Path, Right.Path]);
    }
}

/// <summary>An audit rule with its outcome, for display.</summary>
public sealed class AuditRuleItem(AuditRule rule, IReadOnlyList<AuditFinding> findings, CultureInfo culture)
{
    public string Id { get; } = rule.Id;

    public AuditSeverity Severity { get; } = rule.Severity;

    public string SeverityText { get; } = Shiftwall.Features.Toolbox.ToolText.Get("Severity." + rule.Severity, culture);

    public ItemSeverity ItemSeverity { get; } = findings.Count == 0 ? ItemSeverity.Info : rule.Severity == AuditSeverity.Low ? ItemSeverity.Warning : ItemSeverity.Error;

    public bool Passed { get; } = findings.Count == 0;

    public string Title { get; } = rule.Title(culture);

    public string Why { get; } = rule.Why(culture);

    public string Fix { get; } = rule.Fix(culture);

    public IReadOnlyList<string> Occurrences { get; } = [.. findings.Select(f => $"{f.Location}: {f.Detail(culture)}")];

    public IReadOnlyList<AuditSource> Sources { get; } = rule.Sources;
}

/// <summary>Tool 2: security and best-practice audit.</summary>
public sealed class AuditViewModel : ObservableObject
{
    private int _version;
    private readonly IDialogService _dialogs;
    private AuditReport? _report;
    private bool _showPassed;

    public AuditViewModel(IDialogService dialogs, IShell shell)
    {
        _dialogs = dialogs;
        Config = new ConfigSlotViewModel(dialogs, "Audit.Pick");
        Config.Changed += (_, _) => ToolOutput.Run(RunAsync);
        OpenSourceCommand = new RelayCommand(p => { if (p is string url) shell.OpenUrl(url); });
        ExportCsvCommand = new RelayCommand(ExportCsv, () => _report is not null);
        Loc.Instance.PropertyChanged += (_, _) => Refresh();
    }

    public ConfigSlotViewModel Config { get; }

    public ObservableCollection<AuditRuleItem> Rules { get; } = [];

    public ObservableCollection<string> Notes { get; } = [];

    public RelayCommand OpenSourceCommand { get; }

    public RelayCommand ExportCsvCommand { get; }

    public bool HasReport => _report is not null;

    public int Score => _report?.Score ?? 0;

    public string ScoreText => _report is null ? "" : Loc.F("Audit.Score", _report.Score);

    public string SummaryText => _report is null ? Loc.T("Audit.LoadFirst") : Loc.F("Audit.Summary",
        _report.FailedRules.Count(r => r.Severity == AuditSeverity.High),
        _report.FailedRules.Count(r => r.Severity == AuditSeverity.Medium),
        _report.FailedRules.Count(r => r.Severity == AuditSeverity.Low),
        _report.PassedRules.Count());

    public bool ShowPassed
    {
        get => _showPassed;
        set
        {
            if (Set(ref _showPassed, value)) Refresh();
        }
    }

    public async Task RunAsync()
    {
        if (!Config.IsLoaded)
        {
            _version++;
            _report = null;
            Refresh();
            return;
        }

        var doc = Config.Document!;
        var version = ++_version;
        var report = await Task.Run(() => AuditEngine.Run(doc));
        if (version != _version) return;
        _report = report;
        Refresh();
    }

    private void Refresh()
    {
        Rules.Clear();
        Notes.Clear();
        var c = Loc.Instance.Culture;
        if (_report is not null)
        {
            var items = _report.Rules
                .Select(r => new AuditRuleItem(r, [.. _report.Findings.Where(f => f.RuleId == r.Id)], c))
                .Where(i => _showPassed || !i.Passed)
                .OrderBy(i => i.Passed)
                .ThenByDescending(i => i.Severity);
            foreach (var i in items) Rules.Add(i);
            foreach (var n in _report.Notes) Notes.Add(Shiftwall.Features.Toolbox.ToolText.Get(n, c));
        }

        OnPropertyChanged(nameof(HasReport));
        OnPropertyChanged(nameof(Score));
        OnPropertyChanged(nameof(ScoreText));
        OnPropertyChanged(nameof(SummaryText));
        ExportCsvCommand.RaiseCanExecuteChanged();
    }

    private void ExportCsv()
    {
        if (_report is null) return;
        var c = Loc.Instance.Culture;
        var rows = new List<IReadOnlyList<string>>();
        foreach (var r in _report.Rules)
        {
            var findings = _report.Findings.Where(f => f.RuleId == r.Id).ToList();
            var sources = string.Join(" | ", r.Sources.Select(s => $"{s.Title} <{s.Url}>"));
            if (findings.Count == 0)
            {
                rows.Add([r.Id, Shiftwall.Features.Toolbox.ToolText.Get("Severity." + r.Severity, c), r.Title(c), Loc.T("Audit.Passed"), "", r.Fix(c), sources]);
            }

            foreach (var f in findings)
            {
                rows.Add([r.Id, Shiftwall.Features.Toolbox.ToolText.Get("Severity." + r.Severity, c), r.Title(c), f.Location, f.Detail(c), r.Fix(c), sources]);
            }
        }

        var table = new ExportTable("audit", [Loc.T("Audit.Col.Rule"), Loc.T("Audit.Col.Severity"), Loc.T("Audit.Col.Title"), Loc.T("Audit.Col.Where"), Loc.T("Audit.Col.Detail"), Loc.T("Audit.Col.Fix"), Loc.T("Audit.Col.Sources")], rows);
        ToolOutput.Save(_dialogs, "Audit.ExportCsv", "CSV (*.csv)|*.csv", ToolOutput.Suggest(Config.Path, "audit", ".csv"),
            CsvWriter.ToBytes(CsvWriter.Write(table)), [Config.Path]);
    }
}

/// <summary>A selectable unused object.</summary>
public sealed class UnusedItem(UnusedObject o, Action changed) : ObservableObject
{
    private bool _selected = true;

    public UnusedObject Object { get; } = o;

    public string? Vdom => Object.Vdom;

    public string Table => Object.Table;

    public string Name => Object.Name;

    public bool ViaGroup => Object.OnlyUsedByUnusedGroups;

    public bool IsSelected
    {
        get => _selected;
        set
        {
            if (Set(ref _selected, value)) changed();
        }
    }
}

/// <summary>Tool 3: finds unused objects and generates the CLI script that removes them.</summary>
public sealed class CleanupViewModel : ObservableObject
{
    private int _version;
    private readonly IDialogService _dialogs;
    private readonly IShell _shell;
    private string _script = "";
    private bool _suppress;

    public CleanupViewModel(IDialogService dialogs, IShell shell)
    {
        _dialogs = dialogs;
        _shell = shell;
        Config = new ConfigSlotViewModel(dialogs, "Cleanup.Pick");
        Reference = new ConfigSlotViewModel(dialogs, "Cleanup.PickReference");
        Config.Changed += (_, _) => ToolOutput.Run(RunAsync);
        Reference.Changed += (_, _) => ToolOutput.Run(RunAsync);
        SelectAllCommand = new RelayCommand(() => SetAll(true), () => Items.Count > 0);
        SelectNoneCommand = new RelayCommand(() => SetAll(false), () => Items.Count > 0);
        CopyCommand = new RelayCommand(() => _shell.CopyText(Script), () => Script.Length > 0);
        SaveCommand = new RelayCommand(() => ToolOutput.Save(_dialogs, "Tool.SaveScript", ToolOutput.ScriptFilter, ToolOutput.Suggest(Config.Path, "cleanup", ".txt"), ToolOutput.Utf8(Script), [Config.Path, Reference.Path]), () => Script.Length > 0);
    }

    public ConfigSlotViewModel Config { get; }

    /// <summary>Optional factory-default backup of the same model: its objects are never proposed for removal.</summary>
    public ConfigSlotViewModel Reference { get; }

    public ObservableCollection<UnusedItem> Items { get; } = [];

    public RelayCommand SelectAllCommand { get; }

    public RelayCommand SelectNoneCommand { get; }

    public RelayCommand CopyCommand { get; }

    public RelayCommand SaveCommand { get; }

    public string Script { get => _script; private set { if (Set(ref _script, value)) { CopyCommand.RaiseCanExecuteChanged(); SaveCommand.RaiseCanExecuteChanged(); } } }

    public string Summary => !Config.IsLoaded ? Loc.T("Cleanup.LoadFirst") : Items.Count == 0 ? Loc.T("Cleanup.None") : Loc.F("Cleanup.Summary", Items.Count, Items.Count(i => i.IsSelected));

    public async Task RunAsync()
    {
        var version = ++_version;
        IReadOnlyList<UnusedObject> found = [];
        if (Config.IsLoaded)
        {
            var doc = Config.Document!;
            var exclude = Reference.IsLoaded ? UnusedObjects.ObjectNames(Reference.Document!) : null;
            found = await Task.Run(() => UnusedObjects.Find(doc, exclude));
        }

        if (version != _version) return;
        Items.Clear();
        _suppress = true;
        foreach (var o in found) Items.Add(new UnusedItem(o, Regenerate));
        _suppress = false;

        SelectAllCommand.RaiseCanExecuteChanged();
        SelectNoneCommand.RaiseCanExecuteChanged();
        Regenerate();
    }

    private void SetAll(bool value)
    {
        _suppress = true;
        foreach (var i in Items) i.IsSelected = value;
        _suppress = false;
        Regenerate();
    }

    private void Regenerate()
    {
        if (_suppress) return;
        var selected = Items.Where(i => i.IsSelected).Select(i => i.Object).ToList();
        Script = selected.Count == 0 || Config.Document is null ? "" : UnusedObjects.Script(selected, Config.Document.IsMultiVdom, ToolOutput.ScriptHeader("Nav.Cleanup", Config.Path));
        OnPropertyChanged(nameof(Summary));
    }
}
