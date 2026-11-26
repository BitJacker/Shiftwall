using System.Collections.ObjectModel;
using System.Globalization;
using AssemblyInformationalVersionAttribute = System.Reflection.AssemblyInformationalVersionAttribute;
using static System.Reflection.CustomAttributeExtensions;
using System.Text;
using Shiftwall.Core.Config;
using Shiftwall.Core.Data;
using Shiftwall.Features.Migration;
using Shiftwall.Features.Rebuild;
using Shiftwall.Presentation.Localization;
using Shiftwall.Presentation.Mvvm;
using Shiftwall.Presentation.Services;

namespace Shiftwall.Presentation.ViewModels;

public enum WizardStep
{
    Load = 0,
    PreChecks = 1,
    Mapping = 2,
    Transform = 3,
    Report = 4,
}

/// <summary>
/// The 5-step migration wizard. Nothing is written to disk until the user saves (steps 1-4 are a simulation).
/// </summary>
public sealed partial class MigrationWizardViewModel : ObservableObject
{
    public const string ConfigFilter = "FortiOS configuration (*.conf;*.cfg;*.txt)|*.conf;*.cfg;*.txt|All files (*.*)|*.*";
    public const string TemplateFilter = "Shiftwall mapping template (*.swmap.json)|*.swmap.json|JSON (*.json)|*.json";

    private readonly IDialogService _dialogs;
    private readonly IReportPrinter _printer;
    private readonly IShell _shell;
    private WizardStep _step;
    private string? _sourcePath;
    private SourceProfile? _source;
    private ConfigInspection? _sourceInspection;
    private MigrationAnalysis? _analysis;
    private MigrationResult? _result;
    private MigrationRequest? _request;
    private string? _savedPath;
    private bool _busy;
    private bool _showAllDiff;
    private int _analysisVersion;

    public MigrationWizardViewModel(ModelDatabase db, IDialogService dialogs, IReportPrinter printer, IShell shell)
    {
        _dialogs = dialogs;
        _printer = printer;
        _shell = shell;
        Target = new TargetSelectorViewModel(db);
        Target.Changed += (_, _) => OnInputsChanged();

        BrowseSourceCommand = new AsyncRelayCommand(async () =>
        {
            if (_dialogs.OpenFile(Loc.T("Wizard.Load.PickSource"), ConfigFilter) is { } p) await LoadSourceAsync(p);
        });
        BrowseTargetCommand = new AsyncRelayCommand(async () =>
        {
            if (_dialogs.OpenFile(Loc.T("Wizard.Load.PickTarget"), ConfigFilter) is { } p) await Target.LoadBackupAsync(p);
        });
        NextCommand = new AsyncRelayCommand(GoNextAsync, () => CanGoNext);
        BackCommand = new RelayCommand(() => Step = (WizardStep)Math.Max(0, (int)Step - 1), () => Step > WizardStep.Load && !IsBusy);
        AutoMapCommand = new RelayCommand(AutoMap, () => _source is not null);
        ClearMappingCommand = new RelayCommand(() => { _suppressRowEvents = true; foreach (var s in SourcePorts) s.Target = null; _suppressRowEvents = false; Reanalyze(); }, () => _source is not null);
        UnmapCommand = new RelayCommand(p => { if (p is SourcePortItem s) s.Target = null; });
        SaveTemplateCommand = new RelayCommand(SaveTemplate, () => _source is not null);
        LoadTemplateCommand = new RelayCommand(LoadTemplate, () => _source is not null);
        SaveConfigCommand = new RelayCommand(SaveConfig, () => _result?.IsValid == true);
        ExportHtmlCommand = new RelayCommand(ExportHtml, () => _result is not null);
        PrintPdfCommand = new RelayCommand(PrintPdf, () => _result is not null);
        OpenFolderCommand = new RelayCommand(() => { if (_savedPath is not null) _shell.OpenFolder(Path.GetDirectoryName(_savedPath)!); }, () => _savedPath is not null);
        NewCommand = new RelayCommand(Reset);
        SaveGuiCommand = new RelayCommand(() => SaveScripts(forGui: true), () => CanSaveRebuild);
        SaveTerminalCommand = new RelayCommand(() => SaveScripts(forGui: false), () => CanSaveRebuild);
        SaveSecretsCommand = new RelayCommand(SaveSecrets, () => _rebuild is { Secrets.Count: > 0 });
        ExportRebuildHtmlCommand = new RelayCommand(ExportRebuildHtml, () => _rebuild is not null);
        HidePasswordsCommand = new RelayCommand(HidePasswords, () => PasswordSummary is not null);
    }

    public RelayCommand SaveGuiCommand { get; }

    public RelayCommand SaveTerminalCommand { get; }

    public RelayCommand SaveSecretsCommand { get; }

    public RelayCommand ExportRebuildHtmlCommand { get; }

    public RelayCommand HidePasswordsCommand { get; }

    public TargetSelectorViewModel Target { get; }

    public AsyncRelayCommand BrowseSourceCommand { get; }

    public AsyncRelayCommand BrowseTargetCommand { get; }

    public AsyncRelayCommand NextCommand { get; }

    public RelayCommand BackCommand { get; }

    public RelayCommand AutoMapCommand { get; }

    public RelayCommand ClearMappingCommand { get; }

    public RelayCommand UnmapCommand { get; }

    public RelayCommand SaveTemplateCommand { get; }

    public RelayCommand LoadTemplateCommand { get; }

    public RelayCommand SaveConfigCommand { get; }

    public RelayCommand ExportHtmlCommand { get; }

    public RelayCommand PrintPdfCommand { get; }

    public RelayCommand OpenFolderCommand { get; }

    public RelayCommand NewCommand { get; }

    public ObservableCollection<MessageItem> SourceDiagnostics { get; } = [];

    public ObservableCollection<MessageItem> PreChecks { get; } = [];

    public ObservableCollection<MessageItem> MappingFindings { get; } = [];

    public ObservableCollection<SourcePortItem> SourcePorts { get; } = [];

    public ObservableCollection<TargetPortItem> TargetPorts { get; } = [];

    public ObservableCollection<MessageItem> Changes { get; } = [];

    public ObservableCollection<MessageItem> Validation { get; } = [];

    public ObservableCollection<MessageItem> Checklist { get; } = [];

    public ObservableCollection<SideBySideRow> DiffRows { get; } = [];

    public WizardStep Step
    {
        get => _step;
        set
        {
            if (!Set(ref _step, value)) return;
            OnPropertyChanged(nameof(StepIndex));
            RaiseCommands();
        }
    }

    public int StepIndex => (int)_step;

    public bool IsBusy
    {
        get => _busy;
        private set
        {
            if (Set(ref _busy, value)) RaiseCommands();
        }
    }

    public string? SourcePath { get => _sourcePath; private set => Set(ref _sourcePath, value); }

    public string SourceSummary => _sourceInspection switch
    {
        null => Loc.T("Source.Summary.None"),
        { Kind: not ConfigFileKind.FortiOsText } i => Loc.T("Source.Summary.Kind." + i.Kind),
        { Header: { } h } i => Loc.F("Source.Summary", h.PlatformCode ?? "?", h.Version ?? "?", h.Build ?? "?",
            i.Document!.LineCount, InterfaceInventory.Physical(i.Document).Count),
        _ => "",
    };

    public bool IsSourceReady => _sourceInspection is { Kind: ConfigFileKind.FortiOsText, HasErrors: false };

    public bool CanGoNext => !IsBusy && Step switch
    {
        WizardStep.Load => IsSourceReady && Target.IsReady,
        WizardStep.PreChecks when IsRebuild => _source is not null && RebuildSupported && !_source.Document.IsMultiVdom,
        WizardStep.PreChecks => _analysis is not null && !_analysis.PreChecks.Any(f => f.Severity == FindingSeverity.Error),
        WizardStep.Mapping when IsRebuild => _source is not null && SecretsValid,
        WizardStep.Mapping => _analysis is { IsBlocked: false },
        WizardStep.Transform when IsRebuild => CanSaveRebuild,
        WizardStep.Transform => _result?.IsValid == true,
        _ => false,
    };

    public bool HasBlockingMapping => _analysis?.MappingFindings.Any(f => f.Severity == FindingSeverity.Error) == true;

    public bool ShowAllDiff
    {
        get => _showAllDiff;
        set
        {
            if (Set(ref _showAllDiff, value)) _ = RefreshDiffAsync();
        }
    }

    public string? SavedPath { get => _savedPath; private set => Set(ref _savedPath, value); }

    public MigrationResult? Result => _result;

    // ------------------------------------------------------------------ loading

    public async Task LoadSourceAsync(string path)
    {
        IsBusy = true;
        try
        {
            var bytes = await InputFile.ReadAllBytesAsync(path);
            var inspection = await Task.Run(() => ConfigInspector.Inspect(bytes));
            _sourceInspection = inspection;
            _source = inspection.Kind == ConfigFileKind.FortiOsText && inspection.Document is not null ? SourceProfile.From(inspection) : null;
            SourcePath = path;
            SourceDiagnostics.Clear();
            foreach (var d in inspection.Diagnostics) SourceDiagnostics.Add(MessageItem.From(d));
            OnPropertyChanged(nameof(SourceSummary));
            OnPropertyChanged(nameof(IsSourceReady));
            OnInputsChanged();
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Handles files dropped on the wizard: first drop is the source, the second the target backup.</summary>
    public Task DropFileAsync(string path, bool asTarget) => asTarget ? Target.LoadBackupAsync(path) : LoadSourceAsync(path);

    // ------------------------------------------------------------------ mapping

    /// <summary>Maps <paramref name="source"/> to <paramref name="target"/> (drag &amp; drop). A target can be used once.</summary>
    public void Map(string source, string target)
    {
        var row = SourcePorts.FirstOrDefault(s => s.Name == source);
        if (row is null || TargetPorts.All(t => t.Name != target)) return;
        row.Target = target; // OnSourcePortChanged frees the target elsewhere and re-analyzes
    }

    private bool _suppressRowEvents;

    /// <summary>A target picked in a row's combo box: the same target cannot stay assigned to another row.</summary>
    private void OnSourcePortChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (_suppressRowEvents || e.PropertyName != nameof(SourcePortItem.Target) || sender is not SourcePortItem row) return;
        _suppressRowEvents = true;
        try
        {
            if (row.Target is { } t)
            {
                foreach (var other in SourcePorts.Where(s => !ReferenceEquals(s, row) && s.Target == t)) other.Target = null;
            }
        }
        finally
        {
            _suppressRowEvents = false;
        }

        Reanalyze();
    }

    private void AutoMap()
    {
        var suggested = InterfaceMapping.Suggest(SourcePorts.Select(s => (s.Name, s.Uses)), TargetPorts.Select(t => t.Name), lanFallback: !IsRebuild);
        _suppressRowEvents = true;
        foreach (var s in SourcePorts) s.Target = suggested[s.Name];
        _suppressRowEvents = false;
        Reanalyze();
    }

    private InterfaceMapping CurrentMapping()
    {
        var m = new InterfaceMapping();
        foreach (var s in SourcePorts) m[s.Name] = s.Target;
        return m;
    }

    private void SaveTemplate()
    {
        var suggested = Path.Combine(Path.GetDirectoryName(_sourcePath ?? ".") ?? ".", $"{_source?.PlatformCode ?? "source"}-to-{Target.Label}.swmap.json");
        if (_dialogs.SaveFile(Loc.T("Wizard.Mapping.SaveTemplate"), TemplateFilter, suggested) is not { } path) return;
        var json = CurrentMapping().ToTemplate(Path.GetFileNameWithoutExtension(path), _source?.PlatformCode, Target.Label).ToJson();
        SafeOutput.Write(path, Encoding.UTF8.GetBytes(json), _sourcePath is null ? [] : [_sourcePath], allowOverwrite: true);
    }

    private void LoadTemplate()
    {
        if (_dialogs.OpenFile(Loc.T("Wizard.Mapping.LoadTemplate"), TemplateFilter) is not { } path) return;
        MappingTemplate template;
        try
        {
            template = MappingTemplate.FromJson(InputFile.ReadAllText(path));
        }
        catch (Exception ex) when (ex is InvalidDataException or System.Text.Json.JsonException)
        {
            _dialogs.ShowError(Loc.T("Error.TemplateInvalid"));
            return;
        }

        var targets = TargetPorts.Select(t => t.Name).ToHashSet(StringComparer.Ordinal);
        _suppressRowEvents = true;
        foreach (var s in SourcePorts)
        {
            s.Target = template.Map.TryGetValue(s.Name, out var t) && targets.Contains(t) ? t : null;
        }

        _suppressRowEvents = false;
        Reanalyze();
    }

    // ------------------------------------------------------------------ analysis and run

    private void OnInputsChanged()
    {
        _result = null;
        _analysis = null;
        _rebuild = null;
        ScriptPreview = "";
        PasswordSummary = null;
        RaiseModeProperties();
        RebuildPorts();
        Reanalyze();
    }

    private void RebuildPorts()
    {
        var previous = SourcePorts.ToDictionary(s => s.Name, s => (s.Target, s.Action, s.CreateKind, Ports: s.CheckedPorts), StringComparer.Ordinal);
        SourcePorts.Clear();
        TargetPorts.Clear();
        var target = Target.BuildProfile();
        if (_source is null || target is null) return;

        var probe = MigrationEngine.Analyze(new MigrationRequest(_source, target, new InterfaceMapping()));
        var names = TargetInterfaceNames(target);
        foreach (var t in names) TargetPorts.Add(new TargetPortItem(t));
        var targetNames = names.ToHashSet(StringComparer.Ordinal);
        var rows = IsRebuild ? [.. RebuildSourceRows()] : _source.PhysicalInterfaces.Select(p => (p.Name, (string?)null)).ToList();
        var suggested = InterfaceMapping.Suggest(rows.Select(r => (r.Name, probe.UsageCount.GetValueOrDefault(r.Name))), names, lanFallback: !IsRebuild);
        var keepPrevious = previous.Count > 0 && previous.Values.All(v => v.Target is null || targetNames.Contains(v.Target) || v.Action != SwitchAction.Map);
        var ports = IsRebuild && CurrentRebuildTarget() is { } rt ? SwitchPlanner.PhysicalPorts(rt) : [];
        foreach (var (name, info) in rows)
        {
            var row = new SourcePortItem(name, probe.UsageCount.GetValueOrDefault(name), info);
            var prev = keepPrevious ? previous.GetValueOrDefault(name) : default;
            var had = keepPrevious && previous.ContainsKey(name);
            row.Target = had ? prev.Target : suggested[name];
            if (had)
            {
                row.Action = row.IsSwitch ? prev.Action : SwitchAction.Map;
                row.CreateKind = prev.CreateKind;
            }

            FillPortChoices(row, ports, had ? prev.Ports : null);
            HookRow(row);
            SourcePorts.Add(row);
        }

        SuggestManagement();
    }

    /// <summary>Target port names offered in each row's combo box (empty entry = not mapped).</summary>
    public IEnumerable<string> TargetChoices => [.. TargetPorts.Select(t => t.Name)];

    /// <summary>Recomputes pre-checks and mapping findings in the background (large configurations stay responsive).</summary>
    public async Task ReanalyzeAsync()
    {
        var target = Target.BuildProfile();
        if (_source is null || target is null)
        {
            _analysis = null;
            Publish();
            return;
        }

        var version = ++_analysisVersion;
        var request = new MigrationRequest(_source, target, CurrentMapping());
        var analysis = await Task.Run(() => MigrationEngine.Analyze(request));
        if (version != _analysisVersion) return; // a newer analysis is running
        _request = request;
        _analysis = analysis;
        _result = null;
        Publish();
    }

    private void Reanalyze() => _ = ReanalyzeAsync();

    private void Publish()
    {
        PreChecks.Clear();
        MappingFindings.Clear();
        foreach (var m in ModeMessages) PreChecks.Add(m);
        if (IsRebuild)
        {
            if (_source?.Document.IsMultiVdom == true) PreChecks.Add(MessageItem.From(MigrationFinding.Error(RebuildCodes.MultiVdomNotSupported)));
            if (!Target.IsBackupMode) PreChecks.Add(MessageItem.From(MigrationFinding.Warning(RebuildCodes.NoDefaultBackup)));
            foreach (var s in SourcePorts)
            {
                s.IsBlocking = false;
                if (s.IsUsed && !s.IsMapped) MappingFindings.Add(MessageItem.From(MigrationFinding.Warning(RebuildCodes.InterfaceNotMapped, s.Name)));
            }

            PublishSwitchLayout();
            foreach (var t in TargetPorts) t.AssignedFrom = SourcePorts.FirstOrDefault(s => s.Target == t.Name)?.Name;
        }
        else if (_analysis is not null)
        {
            foreach (var f in _analysis.PreChecks) PreChecks.Add(MessageItem.From(f));
            foreach (var f in _analysis.MappingFindings) MappingFindings.Add(MessageItem.From(f));
            var blocking = _analysis.MappingFindings.Where(f => f.Code == MigrationCodes.UnmappedUsed).Select(f => f.Arguments[0]).ToHashSet(StringComparer.Ordinal);
            foreach (var s in SourcePorts) s.IsBlocking = blocking.Contains(s.Name);
            foreach (var t in TargetPorts) t.AssignedFrom = SourcePorts.FirstOrDefault(s => s.Target == t.Name)?.Name;
        }

        OnPropertyChanged(nameof(HasBlockingMapping));
        OnPropertyChanged(nameof(TargetChoices));
        RaiseCommands();
    }

    private async Task GoNextAsync()
    {
        if (!CanGoNext) return;
        if (Step == WizardStep.Mapping)
        {
            if (IsRebuild)
            {
                await RunRebuildAsync();
                if (_rebuild is null) return;
            }
            else
            {
                await RunMigrationAsync();
                if (_result is null) return;
            }
        }

        Step = (WizardStep)((int)Step + 1);
    }

    /// <summary>Runs the transformation in memory (simulation). Nothing is written.</summary>
    public async Task RunMigrationAsync()
    {
        if (_request is null) await ReanalyzeAsync();
        if (_request is null) return;
        IsBusy = true;
        try
        {
            var request = _request with { Mapping = CurrentMapping() };
            _result = await Task.Run(() => MigrationEngine.Run(request));
            _request = request;
            Changes.Clear();
            Validation.Clear();
            Checklist.Clear();
            foreach (var f in _result.Changes) Changes.Add(MessageItem.From(f));
            foreach (var f in _result.Validation) Validation.Add(MessageItem.From(f));
            foreach (var f in _result.Checklist) Checklist.Add(MessageItem.From(f));
            SavedPath = null;
            await RefreshDiffAsync();
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task RefreshDiffAsync()
    {
        DiffRows.Clear();
        if (_result?.OutputText is not { } output) return;
        var before = _result.SourceText;
        var context = _showAllDiff ? -1 : 3;
        var rows = await Task.Run(() => ConfigDiff.SideBySideRows(before, output, context));
        foreach (var r in rows) DiffRows.Add(r);
    }

    // ------------------------------------------------------------------ output

    private ReportContext ReportContext(string? outputPath) => new(
        Path.GetFileName(_sourcePath ?? ""),
        outputPath is null ? Target.Label : Path.GetFileName(outputPath),
        AppVersion,
        DateTimeOffset.Now,
        Loc.Instance.Culture);

    public static string AppVersion =>
        typeof(MigrationWizardViewModel).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0] ?? "0.0.0";

    private void SaveConfig()
    {
        if (_result?.OutputBytes is null || _sourcePath is null) return;
        var suggested = SafeOutput.SuggestName(_sourcePath, Target.Label);
        if (_dialogs.SaveFile(Loc.T("Wizard.Report.SaveConfig"), ConfigFilter, suggested) is not { } path) return;
        try
        {
            SafeOutput.Write(path, _result.OutputBytes, [_sourcePath, Target.BackupPath ?? _sourcePath], allowOverwrite: true);
            SavedPath = path;
            _dialogs.ShowInfo(Loc.F("Wizard.Report.Saved", Path.GetFileName(path)));
        }
        catch (UnsafeOutputException)
        {
            _dialogs.ShowError(Loc.T("Error.WouldOverwriteOriginal"));
        }
    }

    private void ExportHtml()
    {
        if (_result is null || _request is null) return;
        var suggested = Path.ChangeExtension(SafeOutput.SuggestName(_sourcePath ?? "migration.conf", Target.Label), ".report.html");
        if (_dialogs.SaveFile(Loc.T("Wizard.Report.ExportHtml"), "HTML (*.html)|*.html", suggested) is not { } path) return;
        var html = HtmlReport.Build(_request, _result, ReportContext(_savedPath));
        try
        {
            SafeOutput.Write(path, Encoding.UTF8.GetBytes(html), [_sourcePath ?? path], allowOverwrite: true);
        }
        catch (UnsafeOutputException)
        {
            _dialogs.ShowError(Loc.T("Error.WouldOverwriteOriginal"));
        }
    }

    private void PrintPdf()
    {
        if (_result is null || _request is null) return;
        _printer.Print(_request, _result, ReportContext(_savedPath) with { IncludeDiff = false });
    }

    private void Reset()
    {
        _sourcePath = null;
        _sourceInspection = null;
        _source = null;
        _analysis = null;
        _result = null;
        _request = null;
        SavedPath = null;
        SourceDiagnostics.Clear();
        DiffRows.Clear();
        Changes.Clear();
        Validation.Clear();
        Checklist.Clear();
        ResetRebuild();
        OnPropertyChanged(nameof(SourcePath));
        OnPropertyChanged(nameof(SourceSummary));
        OnPropertyChanged(nameof(IsSourceReady));
        OnInputsChanged();
        Step = WizardStep.Load;
    }

    private void RaiseCommands()
    {
        OnPropertyChanged(nameof(CanGoNext));
        NextCommand.RaiseCanExecuteChanged();
        BackCommand.RaiseCanExecuteChanged();
        AutoMapCommand.RaiseCanExecuteChanged();
        ClearMappingCommand.RaiseCanExecuteChanged();
        SaveTemplateCommand.RaiseCanExecuteChanged();
        LoadTemplateCommand.RaiseCanExecuteChanged();
        SaveConfigCommand.RaiseCanExecuteChanged();
        ExportHtmlCommand.RaiseCanExecuteChanged();
        PrintPdfCommand.RaiseCanExecuteChanged();
        OpenFolderCommand.RaiseCanExecuteChanged();
        SaveGuiCommand.RaiseCanExecuteChanged();
        SaveTerminalCommand.RaiseCanExecuteChanged();
        SaveSecretsCommand.RaiseCanExecuteChanged();
        ExportRebuildHtmlCommand.RaiseCanExecuteChanged();
        HidePasswordsCommand.RaiseCanExecuteChanged();
    }

    internal static CultureInfo Culture => Loc.Instance.Culture;
}
