using System.Collections.ObjectModel;
using System.Data;
using System.Security.Cryptography;
using Shiftwall.Features.Toolbox.Anonymize;
using Shiftwall.Features.Toolbox.Export;
using Shiftwall.Features.Toolbox.Import;
using Shiftwall.Features.Toolbox.Vpn;
using Shiftwall.Presentation.Localization;
using Shiftwall.Presentation.Mvvm;
using Shiftwall.Presentation.Services;

namespace Shiftwall.Presentation.ViewModels.Toolbox;

/// <summary>Tool 4: exports policies and objects to Excel or CSV for documentation and audits.</summary>
public sealed class ExportViewModel : ObservableObject
{
    private int _version;
    private readonly IDialogService _dialogs;
    private IReadOnlyList<ExportTable> _tables = [];
    private ExportTable? _selected;

    public ExportViewModel(IDialogService dialogs)
    {
        _dialogs = dialogs;
        Config = new ConfigSlotViewModel(dialogs, "Export.Pick");
        Config.Changed += (_, _) => ToolOutput.Run(RunAsync);
        SaveXlsxCommand = new RelayCommand(SaveXlsx, () => _tables.Count > 0);
        SaveCsvCommand = new RelayCommand(SaveCsv, () => _selected is not null);
        Loc.Instance.PropertyChanged += (_, _) => ToolOutput.Run(RunAsync);
    }

    public ConfigSlotViewModel Config { get; }

    public ObservableCollection<ExportTable> Tables { get; } = [];

    public RelayCommand SaveXlsxCommand { get; }

    public RelayCommand SaveCsvCommand { get; }

    public ExportTable? SelectedTable
    {
        get => _selected;
        set
        {
            if (!Set(ref _selected, value)) return;
            OnPropertyChanged(nameof(Preview));
            SaveCsvCommand.RaiseCanExecuteChanged();
        }
    }

    /// <summary>The selected table as a <see cref="DataView"/> for an auto-generated grid.</summary>
    public DataView? Preview
    {
        get
        {
            if (_selected is null) return null;
            var dt = new DataTable();
            foreach (var h in _selected.Headers)
            {
                var name = h;
                for (var n = 2; dt.Columns.Contains(name); n++) name = $"{h} {n}";
                dt.Columns.Add(name, typeof(string));
            }

            foreach (var r in _selected.Rows) dt.Rows.Add([.. r.Cast<object>()]);
            return dt.DefaultView;
        }
    }

    public string Summary => _tables.Count == 0 ? Loc.T("Export.LoadFirst") : string.Join(" · ", _tables.Select(t => $"{t.Name}: {t.Rows.Count}"));

    public async Task RunAsync()
    {
        var keep = _selected is null ? 0 : Math.Max(0, Tables.IndexOf(_selected));
        var version = ++_version;
        IReadOnlyList<ExportTable> tables = [];
        if (Config.IsLoaded)
        {
            var doc = Config.Document!;
            var culture = Loc.Instance.Culture;
            tables = await Task.Run(() => ConfigExport.Build(doc, culture));
        }

        if (version != _version) return;
        _tables = tables;

        Tables.Clear();
        foreach (var t in _tables) Tables.Add(t);
        SelectedTable = Tables.Count > 0 ? Tables[Math.Min(keep, Tables.Count - 1)] : null;
        OnPropertyChanged(nameof(Preview));
        OnPropertyChanged(nameof(Summary));
        SaveXlsxCommand.RaiseCanExecuteChanged();
    }

    private void SaveXlsx() => ToolOutput.Save(_dialogs, "Export.SaveXlsx", "Excel (*.xlsx)|*.xlsx", ToolOutput.Suggest(Config.Path, "export", ".xlsx"),
        XlsxWriter.Write(_tables), [Config.Path]);

    private void SaveCsv()
    {
        if (_selected is null) return;
        ToolOutput.Save(_dialogs, "Export.SaveCsv", "CSV (*.csv)|*.csv", ToolOutput.Suggest(Config.Path, _selected.Name.ToLowerInvariant().Replace(' ', '-'), ".csv"),
            CsvWriter.ToBytes(CsvWriter.Write(_selected)), [Config.Path]);
    }
}

/// <summary>Tool 5: CSV → CLI script for bulk object creation.</summary>
public sealed class ImportViewModel : ObservableObject
{
    private readonly IDialogService _dialogs;
    private readonly IShell _shell;
    private ImportKind _kind = ImportKind.Address;
    private string? _csvPath;
    private string _csv = "";
    private ImportResult? _result;

    public ImportViewModel(IDialogService dialogs, IShell shell)
    {
        _dialogs = dialogs;
        _shell = shell;
        BrowseCommand = new AsyncRelayCommand(async () =>
        {
            if (_dialogs.OpenFile(Loc.T("Import.Pick"), "CSV (*.csv;*.txt)|*.csv;*.txt|All files (*.*)|*.*") is { } p) await LoadCsvAsync(p);
        });
        SaveTemplateCommand = new RelayCommand(() => ToolOutput.Save(_dialogs, "Import.SaveTemplate", "CSV (*.csv)|*.csv",
            $"shiftwall-{_kind.ToString().ToLowerInvariant()}-template.csv", CsvWriter.ToBytes(CsvImport.Template(_kind)), []));
        CopyCommand = new RelayCommand(() => _shell.CopyText(Script), () => Script.Length > 0);
        SaveCommand = new RelayCommand(() => ToolOutput.Save(_dialogs, "Tool.SaveScript", ToolOutput.ScriptFilter, ToolOutput.Suggest(_csvPath, "script", ".txt"), ToolOutput.Utf8(Script), [_csvPath]), () => Script.Length > 0);
        Loc.Instance.PropertyChanged += (_, _) => Convert();
    }

    public IReadOnlyList<ImportKind> Kinds { get; } = Enum.GetValues<ImportKind>();

    public ImportKind Kind
    {
        get => _kind;
        set
        {
            if (!Set(ref _kind, value)) return;
            OnPropertyChanged(nameof(Columns));
            Convert();
        }
    }

    public string Columns => string.Join(", ", CsvImport.Columns(_kind));

    public string? CsvPath { get => _csvPath; private set => Set(ref _csvPath, value); }

    public AsyncRelayCommand BrowseCommand { get; }

    public RelayCommand SaveTemplateCommand { get; }

    public RelayCommand CopyCommand { get; }

    public RelayCommand SaveCommand { get; }

    public ObservableCollection<MessageItem> Errors { get; } = [];

    public string Script => _result is { Ok: true } r ? ToolOutput.ScriptHeaderLines("Nav.Import", _csvPath) + r.Script : "";

    public string Summary => _result is null ? Loc.T("Import.LoadFirst") : _result.Ok ? Loc.F("Import.Ok", _result.Objects) : Loc.F("Import.Failed", _result.Errors.Count);

    public async Task LoadCsvAsync(string path)
    {
        _csv = await Shiftwall.Core.Config.InputFile.ReadAllTextAsync(path);
        CsvPath = path;
        Convert();
    }

    /// <summary>For tests and paste: converts <paramref name="csv"/> directly.</summary>
    public void SetCsv(string csv)
    {
        _csv = csv;
        Convert();
    }

    private void Convert()
    {
        _result = _csv.Length == 0 ? null : CsvImport.Convert(_kind, _csv);
        Errors.Clear();
        foreach (var e in _result?.Errors ?? [])
        {
            Errors.Add(new MessageItem(ItemSeverity.Error, e.Code, () => e.Format(Loc.Instance.Culture)));
        }

        OnPropertyChanged(nameof(Script));
        OnPropertyChanged(nameof(Summary));
        CopyCommand.RaiseCanExecuteChanged();
        SaveCommand.RaiseCanExecuteChanged();
    }
}

/// <summary>A row of the IPsec table.</summary>
public sealed record VpnRow(string? Vdom, string Tunnel, string Phase, string Name, string Endpoints, string Proposal, string DhGroups, string Lifetime, string Extra);

/// <summary>A row of the two-site check.</summary>
public sealed record VpnCheckRow(string Tunnels, string Parameter, string Local, string Remote, VpnCheckResult Result, ItemSeverity Severity, string ResultText);

/// <summary>Tool 6: IPsec phase 1/phase 2 summary and consistency check between two sites.</summary>
public sealed class VpnViewModel : ObservableObject
{
    public VpnViewModel(IDialogService dialogs)
    {
        Local = new ConfigSlotViewModel(dialogs, "Vpn.PickLocal");
        Remote = new ConfigSlotViewModel(dialogs, "Vpn.PickRemote");
        Local.Changed += (_, _) => Refresh();
        Remote.Changed += (_, _) => Refresh();
        Loc.Instance.PropertyChanged += (_, _) => Refresh();
    }

    public ConfigSlotViewModel Local { get; }

    public ConfigSlotViewModel Remote { get; }

    public ObservableCollection<VpnRow> Rows { get; } = [];

    public ObservableCollection<VpnCheckRow> Checks { get; } = [];

    public string Summary { get; private set; } = "";

    public void Refresh()
    {
        Rows.Clear();
        Checks.Clear();
        var notSet = Loc.T("Vpn.Default");
        string D(string? v) => v ?? notSet;
        if (Local.IsLoaded)
        {
            foreach (var t in IpsecSummary.Tunnels(Local.Document!))
            {
                Rows.Add(new VpnRow(t.Vdom, t.Name, "1", t.Name, $"{t.Interface} → {t.RemoteGateway ?? t.Type ?? "?"}", D(t.Proposal), D(t.DhGroups), D(t.KeyLife), $"IKEv{t.IkeVersion ?? "?"}"));
                foreach (var p in t.Phase2)
                {
                    Rows.Add(new VpnRow(t.Vdom, t.Name, "2", p.Name, $"{p.Source} → {p.Destination}", D(p.Proposal), D(p.DhGroups), D(p.KeyLifeSeconds), $"PFS {D(p.Pfs)}"));
                }
            }
        }

        if (Local.IsLoaded && Remote.IsLoaded)
        {
            var pairs = IpsecSummary.Pair(Local.Document!, Remote.Document!);
            foreach (var (l, r) in pairs)
            {
                var cmp = IpsecSummary.Compare(l, r);
                foreach (var c in cmp.Checks)
                {
                    var sev = c.Result switch { VpnCheckResult.Mismatch => ItemSeverity.Error, VpnCheckResult.Unknown => ItemSeverity.Warning, _ => ItemSeverity.Info };
                    Checks.Add(new VpnCheckRow($"{l.Name} ↔ {r.Name}", c.Parameter, c.Local ?? notSet, c.Remote ?? notSet, c.Result, sev, Loc.T("Vpn.Result." + c.Result)));
                }
            }

            Summary = pairs.Count == 0 ? Loc.T("Vpn.NoPairs") : Loc.F("Vpn.PairSummary", pairs.Count, Checks.Count(c => c.Result == VpnCheckResult.Mismatch), Checks.Count(c => c.Result == VpnCheckResult.Unknown));
        }
        else
        {
            Summary = Local.IsLoaded ? Loc.F("Vpn.TunnelCount", Rows.Count(r => r.Phase == "1")) : Loc.T("Vpn.LoadFirst");
        }

        OnPropertyChanged(nameof(Summary));
    }
}

/// <summary>Tool 7: anonymizes a configuration; the map is saved encrypted with a password and can restore the original.</summary>
public sealed class AnonymizeViewModel : ObservableObject
{
    private int _version;
    private readonly IDialogService _dialogs;
    private AnonymizeResult? _result;
    private bool _privateIps;
    private bool _hostnames = true;
    private bool _users = true;
    private bool _comments = true;
    private bool _emails = true;
    private string _password = "";
    private string _passwordConfirm = "";
    private string? _mapPath;
    private bool _mapSaved;

    public AnonymizeViewModel(IDialogService dialogs)
    {
        _dialogs = dialogs;
        Config = new ConfigSlotViewModel(dialogs, "Anonymize.Pick");
        Config.Changed += (_, _) => ToolOutput.Run(RunAsync);
        SaveOutputCommand = new RelayCommand(SaveOutput, () => _result is not null && _mapSaved);
        SaveMapCommand = new RelayCommand(SaveMap, () => _result is not null && PasswordOk);
        RestoreSource = new ConfigSlotViewModel(dialogs, "Anonymize.PickAnonymized");
        BrowseMapCommand = new RelayCommand(() => { if (_dialogs.OpenFile(Loc.T("Anonymize.PickMap"), MapFilter) is { } p) MapPath = p; });
        RestoreCommand = new RelayCommand(Restore, () => RestoreSource.IsLoaded && _mapPath is not null && _password.Length > 0);
        RestoreSource.Changed += (_, _) => RestoreCommand.RaiseCanExecuteChanged();
        Loc.Instance.PropertyChanged += (_, _) => OnPropertyChanged(nameof(Stats));
    }

    public const string MapFilter = "Shiftwall map (*.swmap)|*.swmap";

    public ConfigSlotViewModel Config { get; }

    public ConfigSlotViewModel RestoreSource { get; }

    public RelayCommand SaveOutputCommand { get; }

    public RelayCommand SaveMapCommand { get; }

    public RelayCommand BrowseMapCommand { get; }

    public RelayCommand RestoreCommand { get; }

    public bool PrivateIps { get => _privateIps; set { if (Set(ref _privateIps, value)) ToolOutput.Run(RunAsync); } }

    public bool Hostnames { get => _hostnames; set { if (Set(ref _hostnames, value)) ToolOutput.Run(RunAsync); } }

    public bool Users { get => _users; set { if (Set(ref _users, value)) ToolOutput.Run(RunAsync); } }

    public bool Comments { get => _comments; set { if (Set(ref _comments, value)) ToolOutput.Run(RunAsync); } }

    public bool Emails { get => _emails; set { if (Set(ref _emails, value)) ToolOutput.Run(RunAsync); } }

    public string? MapPath { get => _mapPath; private set { if (Set(ref _mapPath, value)) RestoreCommand.RaiseCanExecuteChanged(); } }

    /// <summary>First lines of the anonymized text, for review.</summary>
    public string Preview => _result is null ? "" : string.Join('\n', _result.Text.Split('\n').Take(400));

    public string Stats => _result is null ? Loc.T("Anonymize.LoadFirst") : Loc.F("Anonymize.Stats",
        _result.Count(AnonCategory.PublicIp) + _result.Count(AnonCategory.PrivateIp), _result.Count(AnonCategory.Hostname), _result.Count(AnonCategory.User),
        _result.Count(AnonCategory.Email), _result.Count(AnonCategory.Comment), _result.Count(AnonCategory.Secret));

    public bool MapSaved { get => _mapSaved; private set { if (Set(ref _mapSaved, value)) SaveOutputCommand.RaiseCanExecuteChanged(); } }

    public bool PasswordOk => _password.Length >= MapProtector.MinPasswordLength && _password == _passwordConfirm;

    public string PasswordHint => _password.Length == 0 ? "" : _password.Length < MapProtector.MinPasswordLength
        ? Loc.F("Anonymize.PasswordShort", MapProtector.MinPasswordLength)
        : _password != _passwordConfirm && _passwordConfirm.Length > 0 ? Loc.T("Anonymize.PasswordMismatch") : "";

    /// <summary>Set from the password boxes (code-behind). Kept in memory only.</summary>
    public void SetPassword(string password, string confirm)
    {
        _password = password;
        _passwordConfirm = confirm;
        OnPropertyChanged(nameof(PasswordOk));
        OnPropertyChanged(nameof(PasswordHint));
        SaveMapCommand.RaiseCanExecuteChanged();
        RestoreCommand.RaiseCanExecuteChanged();
    }

    public async Task RunAsync()
    {
        MapSaved = false;
        var version = ++_version;
        AnonymizeResult? result = null;
        if (Config.IsLoaded)
        {
            var doc = Config.ParseFresh();
            var options = new AnonymizeOptions(_privateIps, _hostnames, _users, _comments, _emails);
            result = await Task.Run(() => Anonymizer.Anonymize(doc, options));
        }

        if (version != _version) return;
        _result = result;

        OnPropertyChanged(nameof(Preview));
        OnPropertyChanged(nameof(Stats));
        SaveMapCommand.RaiseCanExecuteChanged();
        SaveOutputCommand.RaiseCanExecuteChanged();
    }

    private void SaveMap()
    {
        if (_result is null || !PasswordOk) return;
        var file = MapProtector.Encrypt(_result.Map.ToJsonBytes(), _password);
        if (ToolOutput.Save(_dialogs, "Anonymize.SaveMap", MapFilter, ToolOutput.Suggest(Config.Path, "map", ".swmap"), file, [Config.Path])) MapSaved = true;
    }

    private void SaveOutput()
    {
        if (_result is null) return;
        ToolOutput.Save(_dialogs, "Anonymize.SaveOutput", MigrationWizardViewModel.ConfigFilter, ToolOutput.Suggest(Config.Path, "anonymized", ".conf"),
            ToolOutput.Utf8(_result.Text), [Config.Path]);
    }

    private void Restore()
    {
        if (!RestoreSource.IsLoaded || _mapPath is null) return;
        AnonymizationMap map;
        try
        {
            map = AnonymizationMap.FromJsonBytes(MapProtector.Decrypt(Shiftwall.Core.Config.InputFile.ReadAllBytes(_mapPath, Shiftwall.Core.Config.InputFile.MaxSmallFileBytes), _password));
        }
        catch (Exception ex) when (ex is CryptographicException or InvalidDataException or System.Text.Json.JsonException)
        {
            _dialogs.ShowError(Loc.T("Anonymize.MapFailed"));
            return;
        }

        var restored = Anonymizer.Restore(RestoreSource.ParseFresh(), map);
        ToolOutput.Save(_dialogs, "Anonymize.SaveRestored", MigrationWizardViewModel.ConfigFilter, ToolOutput.Suggest(RestoreSource.Path, "restored", ".conf"),
            ToolOutput.Utf8(restored), [RestoreSource.Path, _mapPath]);
    }
}
