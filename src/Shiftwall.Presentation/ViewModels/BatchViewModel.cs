using System.Collections.ObjectModel;
using Shiftwall.Core.Data;
using Shiftwall.Features.Migration;
using Shiftwall.Presentation.Localization;
using Shiftwall.Presentation.Mvvm;
using Shiftwall.Presentation.Services;

namespace Shiftwall.Presentation.ViewModels;

/// <summary>Batch conversion: several sites, same target and same mapping template.</summary>
public sealed class BatchViewModel : ObservableObject
{
    private readonly IDialogService _dialogs;
    private readonly IShell _shell;
    private string? _templatePath;
    private MappingTemplate? _template;
    private string? _outputFolder;
    private bool _simulate = true;
    private bool _busy;
    private string _progress = "";

    public BatchViewModel(ModelDatabase db, IDialogService dialogs, IShell shell)
    {
        _dialogs = dialogs;
        _shell = shell;
        Target = new TargetSelectorViewModel(db);
        Target.Changed += (_, _) => Raise();

        AddFilesCommand = new RelayCommand(() =>
        {
            foreach (var f in _dialogs.OpenFiles(Loc.T("Batch.AddFiles"), MigrationWizardViewModel.ConfigFilter))
            {
                if (!Files.Contains(f, StringComparer.OrdinalIgnoreCase)) Files.Add(f);
            }

            Raise();
        });
        RemoveFileCommand = new RelayCommand(p => { if (p is string f) Files.Remove(f); Raise(); });
        ClearFilesCommand = new RelayCommand(() => { Files.Clear(); Raise(); });
        BrowseTargetCommand = new AsyncRelayCommand(async () =>
        {
            if (_dialogs.OpenFile(Loc.T("Wizard.Load.PickTarget"), MigrationWizardViewModel.ConfigFilter) is { } p) await Target.LoadBackupAsync(p);
        });
        LoadTemplateCommand = new RelayCommand(LoadTemplate);
        PickOutputCommand = new RelayCommand(() => { if (_dialogs.PickFolder(Loc.T("Batch.PickOutput")) is { } d) OutputFolder = d; });
        RunCommand = new AsyncRelayCommand(RunAsync, () => CanRun);
        OpenOutputCommand = new RelayCommand(() => { if (_outputFolder is not null) _shell.OpenFolder(_outputFolder); }, () => _outputFolder is not null);
    }

    public TargetSelectorViewModel Target { get; }

    public ObservableCollection<string> Files { get; } = [];

    public ObservableCollection<BatchRow> Results { get; } = [];

    public RelayCommand AddFilesCommand { get; }

    public RelayCommand RemoveFileCommand { get; }

    public RelayCommand ClearFilesCommand { get; }

    public AsyncRelayCommand BrowseTargetCommand { get; }

    public RelayCommand LoadTemplateCommand { get; }

    public RelayCommand PickOutputCommand { get; }

    public AsyncRelayCommand RunCommand { get; }

    public RelayCommand OpenOutputCommand { get; }

    public string? TemplatePath { get => _templatePath; private set => Set(ref _templatePath, value); }

    public string? OutputFolder
    {
        get => _outputFolder;
        set
        {
            if (Set(ref _outputFolder, value)) Raise();
        }
    }

    /// <summary>When true (default) nothing is written: only the results table is produced.</summary>
    public bool Simulate
    {
        get => _simulate;
        set
        {
            if (Set(ref _simulate, value)) Raise();
        }
    }

    public bool IsBusy { get => _busy; private set { if (Set(ref _busy, value)) Raise(); } }

    public string Progress { get => _progress; private set => Set(ref _progress, value); }

    public bool CanRun => !IsBusy && Files.Count > 0 && Target.IsReady && _template is not null && (Simulate || _outputFolder is not null);

    public void AddDroppedFiles(IEnumerable<string> paths)
    {
        foreach (var f in paths.Where(File.Exists))
        {
            if (!Files.Contains(f, StringComparer.OrdinalIgnoreCase)) Files.Add(f);
        }

        Raise();
    }

    public void SetTemplate(string path)
    {
        try
        {
            _template = MappingTemplate.FromJson(File.ReadAllText(path));
            TemplatePath = path;
        }
        catch (Exception ex) when (ex is InvalidDataException or System.Text.Json.JsonException or IOException)
        {
            _template = null;
            TemplatePath = null;
            _dialogs.ShowError(Loc.T("Error.TemplateInvalid"));
        }

        Raise();
    }

    private void LoadTemplate()
    {
        if (_dialogs.OpenFile(Loc.T("Wizard.Mapping.LoadTemplate"), MigrationWizardViewModel.TemplateFilter) is { } p) SetTemplate(p);
    }

    public async Task RunAsync()
    {
        var target = Target.BuildProfile();
        if (target is null || _template is null) return;
        IsBusy = true;
        Results.Clear();
        try
        {
            var files = Files.ToList();
            var outDir = _outputFolder ?? Path.GetTempPath(); // unused when simulating
            var ctx = new ReportContext("", "", MigrationWizardViewModel.AppVersion, DateTimeOffset.Now, Loc.Instance.Culture);
            var progress = new Progress<(int Done, int Total)>(p => Progress = Loc.F("Batch.Progress", p.Done, p.Total));
            var template = _template;
            var simulate = Simulate;
            var results = await Task.Run(() => BatchMigration.Run(files, target, template, outDir, ctx, simulate, progress));
            foreach (var r in results) Results.Add(new BatchRow(r, BatchMigration.Summary(r, Loc.Instance.Culture)));
            Progress = Loc.F("Batch.Done", results.Count(r => r.Succeeded), results.Count);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void Raise()
    {
        OnPropertyChanged(nameof(CanRun));
        RunCommand.RaiseCanExecuteChanged();
        OpenOutputCommand.RaiseCanExecuteChanged();
    }
}
