using System.Collections.ObjectModel;
using Shiftwall.Core.Config;
using Shiftwall.Core.Data;
using Shiftwall.Features.Migration;
using Shiftwall.Presentation.Localization;
using Shiftwall.Presentation.Mvvm;

namespace Shiftwall.Presentation.ViewModels;

/// <summary>
/// Chooses the migration target: a factory-default backup (preferred, its data wins over the database) or a model
/// from the database with a FortiOS version.
/// </summary>
public sealed class TargetSelectorViewModel : ObservableObject
{
    private ModelDatabase _db;
    private TargetSource _mode = TargetSource.DefaultBackup;
    private string? _backupPath;
    private ConfigInspection? _backup;
    private FortiGateModel? _model;
    private string? _version;

    public TargetSelectorViewModel(ModelDatabase db)
    {
        _db = db;
        Models = [.. db.Models.OrderBy(m => m.Id, StringComparer.Ordinal)];
    }

    public event EventHandler? Changed;

    public ObservableCollection<FortiGateModel> Models { get; }

    public ObservableCollection<string> Versions { get; } = [];

    public ObservableCollection<MessageItem> Diagnostics { get; } = [];

    public TargetSource Mode
    {
        get => _mode;
        set
        {
            if (Set(ref _mode, value))
            {
                OnPropertyChanged(nameof(IsBackupMode));
                OnPropertyChanged(nameof(IsDatabaseMode));
                Raise();
            }
        }
    }

    public bool IsBackupMode { get => _mode == TargetSource.DefaultBackup; set { if (value) Mode = TargetSource.DefaultBackup; } }

    public bool IsDatabaseMode { get => _mode == TargetSource.Database; set { if (value) Mode = TargetSource.Database; } }

    public string? BackupPath { get => _backupPath; private set => Set(ref _backupPath, value); }

    public ConfigInspection? Backup => _backup;

    /// <summary>Model from the database: required in database mode, optional with a backup (adds disk information).</summary>
    public FortiGateModel? SelectedModel
    {
        get => _model;
        set
        {
            if (!Set(ref _model, value)) return;
            Versions.Clear();
            if (value is not null)
            {
                foreach (var r in _db.ReleasesSupporting(value.Id).Reverse()) Versions.Add(r.Version);
            }

            SelectedVersion = Versions.FirstOrDefault();
            OnPropertyChanged(nameof(ModelWarnings));
            Raise();
        }
    }

    public string? SelectedVersion
    {
        get => _version;
        set
        {
            if (Set(ref _version, value)) Raise();
        }
    }

    /// <summary>Unverified data of the selected model, shown as warnings.</summary>
    public IReadOnlyList<string> ModelWarnings
    {
        get
        {
            if (_model is null) return [];
            var list = new List<string>();
            if (!_model.Interfaces.IsVerified) list.Add(Loc.F("Target.Unverified.Interfaces", _model.Interfaces.Note ?? ""));
            if (!_model.HasDisk.IsVerified) list.Add(Loc.F("Target.Unverified.Disk", _model.HasDisk.Note ?? ""));
            if (!_model.DefaultSwitches.IsVerified) list.Add(Loc.T("Target.Unverified.Switch"));
            return list;
        }
    }

    public string Summary
    {
        get
        {
            if (IsBackupMode && _backup?.Header is { } h)
            {
                return Loc.F("Target.Summary.Backup", h.PlatformCode ?? "?", h.Version ?? "?", h.Build ?? "?", InterfaceInventory.Physical(_backup.Document!).Count);
            }

            if (IsDatabaseMode && _model is not null)
            {
                return Loc.F("Target.Summary.Database", _model.DisplayName, _version ?? "?", _model.Interfaces.Value?.Count ?? 0);
            }

            return Loc.T("Target.Summary.None");
        }
    }

    public bool IsReady => IsBackupMode
        ? _backup is { Kind: ConfigFileKind.FortiOsText, HasErrors: false }
        : _model is not null && _version is not null;

    public void UpdateDatabase(ModelDatabase db)
    {
        _db = db;
        var id = _model?.Id;
        Models.Clear();
        foreach (var m in db.Models.OrderBy(m => m.Id, StringComparer.Ordinal)) Models.Add(m);
        SelectedModel = id is null ? null : db.FindModel(id);
    }

    public async Task LoadBackupAsync(string path)
    {
        var bytes = await InputFile.ReadAllBytesAsync(path);
        var inspection = await Task.Run(() => ConfigInspector.Inspect(bytes));
        _backup = inspection;
        BackupPath = path;
        Diagnostics.Clear();
        foreach (var d in inspection.Diagnostics.Where(d => d.Severity != DiagnosticSeverity.Info)) Diagnostics.Add(MessageItem.From(d));
        Mode = TargetSource.DefaultBackup;
        Raise();
    }

    public TargetProfile? BuildProfile()
    {
        if (!IsReady) return null;
        return IsBackupMode ? TargetProfile.FromDefaultBackup(_backup!, _model) : TargetProfile.FromDatabase(_model!, _version);
    }

    /// <summary>Label used in output file names and reports.</summary>
    public string Label => IsDatabaseMode ? _model?.Id ?? "target" : _model?.Id ?? _backup?.Header?.PlatformCode ?? "target";

    private void Raise()
    {
        OnPropertyChanged(nameof(Summary));
        OnPropertyChanged(nameof(IsReady));
        OnPropertyChanged(nameof(Label));
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
