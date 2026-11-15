using System.Collections.ObjectModel;
using System.Globalization;
using System.Net;
using Shiftwall.Core.Config;
using Shiftwall.Core.Data;
using Shiftwall.Features.Toolbox.Snippets;
using Shiftwall.Lan;
using Shiftwall.Presentation.Localization;
using Shiftwall.Presentation.Mvvm;
using Shiftwall.Presentation.Services;

namespace Shiftwall.Presentation.ViewModels.Toolbox;

public enum SnippetKind
{
    Vlan,
    Policy,
    Vip,
    StaticRoute,
    LocalUser,
}

/// <summary>Tool 8: generator of CLI snippets for common operations. Fields are plain strings bound to the form.</summary>
public sealed class SnippetsViewModel : ObservableObject
{
    private readonly IDialogService _dialogs;
    private readonly IShell _shell;
    private SnippetKind _kind = SnippetKind.Vlan;
    private string _script = "";

    public SnippetsViewModel(IDialogService dialogs, IShell shell)
    {
        _dialogs = dialogs;
        _shell = shell;
        GenerateCommand = new RelayCommand(Generate);
        CopyCommand = new RelayCommand(() => _shell.CopyText(Script), () => Script.Length > 0);
        SaveCommand = new RelayCommand(() => ToolOutput.Save(_dialogs, "Tool.SaveScript", ToolOutput.ScriptFilter, $"shiftwall-{_kind.ToString().ToLowerInvariant()}.txt", ToolOutput.Utf8(Script), []), () => Script.Length > 0);
    }

    public IReadOnlyList<SnippetKind> Kinds { get; } = Enum.GetValues<SnippetKind>();

    public SnippetKind Kind
    {
        get => _kind;
        set
        {
            if (!Set(ref _kind, value)) return;
            Errors.Clear();
            Script = "";
            foreach (var k in Kinds) OnPropertyChanged("Is" + k);
        }
    }

    public bool IsVlan => _kind == SnippetKind.Vlan;

    public bool IsPolicy => _kind == SnippetKind.Policy;

    public bool IsVip => _kind == SnippetKind.Vip;

    public bool IsStaticRoute => _kind == SnippetKind.StaticRoute;

    public bool IsLocalUser => _kind == SnippetKind.LocalUser;

    // Shared / per-kind fields (strings: validated by the generator).
    public string Name { get; set; } = "";

    public string Interface { get; set; } = "";

    public string VlanId { get; set; } = "";

    public string IpCidr { get; set; } = "";

    public string Vdom { get; set; } = "root";

    public string AllowAccess { get; set; } = "ping";

    public string Alias { get; set; } = "";

    public string SrcIntf { get; set; } = "";

    public string DstIntf { get; set; } = "";

    public string SrcAddr { get; set; } = "all";

    public string DstAddr { get; set; } = "all";

    public string Service { get; set; } = "";

    public string Action { get; set; } = "accept";

    public IReadOnlyList<string> Actions { get; } = ["accept", "deny"];

    public bool Nat { get; set; }

    public string Schedule { get; set; } = "always";

    public string LogTraffic { get; set; } = "all";

    public IReadOnlyList<string> LogOptions { get; } = ["all", "utm", "disable"];

    public string Comment { get; set; } = "";

    public string ExtIntf { get; set; } = "";

    public string ExtIp { get; set; } = "";

    public string MappedIp { get; set; } = "";

    public string Protocol { get; set; } = "tcp";

    public string ExtPort { get; set; } = "";

    public string MappedPort { get; set; } = "";

    public string Gateway { get; set; } = "";

    public string Distance { get; set; } = "10";

    public bool UserEnabled { get; set; } = true;

    public RelayCommand GenerateCommand { get; }

    public RelayCommand CopyCommand { get; }

    public RelayCommand SaveCommand { get; }

    public ObservableCollection<string> Errors { get; } = [];

    public string Script
    {
        get => _script;
        private set
        {
            if (!Set(ref _script, value)) return;
            CopyCommand.RaiseCanExecuteChanged();
            SaveCommand.RaiseCanExecuteChanged();
        }
    }

    public void Generate()
    {
        static int Int(string s, int fallback) => int.TryParse(s, NumberStyles.None, CultureInfo.InvariantCulture, out var n) ? n : fallback;
        static string? Opt(string s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
        var r = _kind switch
        {
            SnippetKind.Vlan => SnippetGenerator.Vlan(new VlanInput(Name.Trim(), Interface.Trim(), Int(VlanId, 0), IpCidr, Vdom.Trim(), AllowAccess, Opt(Alias))),
            SnippetKind.Policy => SnippetGenerator.Policy(new PolicyInput(Name.Trim(), SrcIntf, DstIntf, SrcAddr, DstAddr, Service, Action, Nat, Schedule.Trim(), LogTraffic, Opt(Comment))),
            SnippetKind.Vip => SnippetGenerator.Vip(new VipInput(Name.Trim(), ExtIntf.Trim(), ExtIp.Trim(), MappedIp.Trim(), Opt(Protocol), Opt(ExtPort), Opt(MappedPort), Opt(Comment))),
            SnippetKind.StaticRoute => SnippetGenerator.StaticRoute(new StaticRouteInput(IpCidr, Gateway.Trim(), Interface.Trim(), Int(Distance, 0), Opt(Comment))),
            _ => SnippetGenerator.LocalUser(new LocalUserInput(Name.Trim(), UserEnabled)),
        };
        Errors.Clear();
        foreach (var e in r.FormatErrors(Loc.Instance.Culture)) Errors.Add(e);
        Script = r.Ok ? ToolOutput.ScriptHeaderLines("Nav.Snippets", null) + r.Script : "";
    }
}

/// <summary>A FortiOS release row of the upgrade page.</summary>
public sealed record ReleaseRow(string Version, string Build, string Date, bool SupportsModel, string? SourceUrl);

/// <summary>
/// Tool 9: upgrade planning. Fortinet publishes upgrade paths only in the interactive Upgrade Path Tool, so no path
/// is computed: the page shows the releases that support the model (from the release notes) and opens the tool.
/// </summary>
public sealed class UpgradeViewModel : ObservableObject
{
    private readonly IShell _shell;
    private ModelDatabase _db;
    private FortiGateModel? _model;
    private string? _current;

    public UpgradeViewModel(ModelDatabase db, IShell shell)
    {
        _db = db;
        _shell = shell;
        Models = [.. db.Models.OrderBy(m => m.Id, StringComparer.Ordinal)];
        OpenToolCommand = new RelayCommand(() => _shell.OpenUrl(_db.UpgradePaths.OfficialToolUrl));
        OpenUrlCommand = new RelayCommand(p => { if (p is string u) _shell.OpenUrl(u); });
        Loc.Instance.PropertyChanged += (_, _) => OnPropertyChanged(nameof(PathStatus));
    }

    public ObservableCollection<FortiGateModel> Models { get; }

    public ObservableCollection<string> CurrentVersions { get; } = [];

    public ObservableCollection<ReleaseRow> Newer { get; } = [];

    public RelayCommand OpenToolCommand { get; }

    public RelayCommand OpenUrlCommand { get; }

    public string ToolUrl => _db.UpgradePaths.OfficialToolUrl;

    public FortiGateModel? SelectedModel
    {
        get => _model;
        set
        {
            if (!Set(ref _model, value)) return;
            CurrentVersions.Clear();
            if (value is not null)
            {
                foreach (var r in _db.ReleasesSupporting(value.Id).OrderByDescending(r => FortiOsVersion.Parse(r.Version))) CurrentVersions.Add(r.Version);
            }

            CurrentVersion = CurrentVersions.LastOrDefault();
            Refresh();
        }
    }

    public string? CurrentVersion
    {
        get => _current;
        set
        {
            if (Set(ref _current, value)) Refresh();
        }
    }

    /// <summary>Why no path is shown (official source) or the paths stored in the database.</summary>
    public string PathStatus => _db.UpgradePaths.Paths.Count == 0 ? Loc.T("Upgrade.NoPaths") : Loc.T("Upgrade.PathsAvailable");

    public void UpdateDatabase(ModelDatabase db)
    {
        _db = db;
        var id = _model?.Id;
        Models.Clear();
        foreach (var m in db.Models.OrderBy(m => m.Id, StringComparer.Ordinal)) Models.Add(m);
        SelectedModel = id is null ? null : db.FindModel(id);
    }

    private void Refresh()
    {
        Newer.Clear();
        if (_model is null || _current is null || !FortiOsVersion.TryParse(_current, out var from)) return;
        foreach (var r in _db.Releases.Where(r => FortiOsVersion.TryParse(r.Version, out var v) && v > from).OrderBy(r => FortiOsVersion.Parse(r.Version)))
        {
            var supports = r.SupportedModels.Value?.Contains(_model.Id, StringComparer.OrdinalIgnoreCase) == true;
            Newer.Add(new ReleaseRow(r.Version, r.Build.Value ?? "?", r.ReleaseDate.Value?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "?", supports,
                r.SupportedModels.Sources.FirstOrDefault()?.Url));
        }
    }
}

/// <summary>
/// Tool 10: downloads a backup from a FortiGate on the local network (REST API). Disabled by default; the token
/// and passwords are kept only in memory and cleared after each attempt.
/// </summary>
public sealed class LanViewModel : ObservableObject
{
    private readonly IDialogService _dialogs;
    private readonly SettingsStore _store;
    private readonly AppSettings _settings;
    private readonly Func<LanBackupRequest, string?, CancellationToken, Task<LanBackupResult>> _download;
    private string _host = "";
    private string _port = "443";
    private string _vdom = "";
    private bool _fortiOs801;
    private string _token = "";
    private string _backupPassword = "";
    private string _status = "";
    private bool _busy;
    private CancellationTokenSource? _cts;

    public LanViewModel(IDialogService dialogs, SettingsStore store, AppSettings settings, Func<LanBackupRequest, string?, CancellationToken, Task<LanBackupResult>>? download = null)
    {
        _dialogs = dialogs;
        _store = store;
        _settings = settings;
        var client = new LanBackupClient();
        _download = download ?? client.DownloadAsync;
        EnableCommand = new RelayCommand(Enable, () => !Enabled);
        DisableCommand = new RelayCommand(Disable, () => Enabled);
        DownloadCommand = new AsyncRelayCommand(DownloadAsync, () => Enabled && !IsBusy && CanDownload);
        CancelCommand = new RelayCommand(() => _cts?.Cancel(), () => IsBusy);
        ForgetCertificatesCommand = new RelayCommand(() => { _settings.LanConfirmedCertificates.Clear(); _store.Save(_settings); OnPropertyChanged(nameof(ConfirmedCount)); });
    }

    /// <summary>Raised with the saved file path after a successful download.</summary>
    public event EventHandler<string>? Downloaded;

    public bool Enabled => _settings.LanFeatureEnabled;

    public RelayCommand EnableCommand { get; }

    public RelayCommand DisableCommand { get; }

    public AsyncRelayCommand DownloadCommand { get; }

    public RelayCommand CancelCommand { get; }

    public RelayCommand ForgetCertificatesCommand { get; }

    public int ConfirmedCount => _settings.LanConfirmedCertificates.Count;

    public string Host { get => _host; set { if (Set(ref _host, value)) RaiseCan(); } }

    public string Port { get => _port; set { if (Set(ref _port, value)) RaiseCan(); } }

    public string Vdom { get => _vdom; set => Set(ref _vdom, value); }

    /// <summary>The device runs FortiOS 8.0.1 or later: a backup password is required (secrets encrypted with it).</summary>
    public bool FortiOs801 { get => _fortiOs801; set { if (Set(ref _fortiOs801, value)) RaiseCan(); } }

    public string Status { get => _status; private set => Set(ref _status, value); }

    public bool IsBusy { get => _busy; private set { if (Set(ref _busy, value)) { RaiseCan(); CancelCommand.RaiseCanExecuteChanged(); } } }

    public string? HostError => _host.Length == 0 || LanBackupClient.TryParseTarget(_host, out _) ? null : Loc.T("Lan.HostInvalid");

    private bool CanDownload => LanBackupClient.TryParseTarget(_host, out _) && TryPort(out _) && _token.Length > 0 && (!_fortiOs801 || _backupPassword.Length >= 8);

    /// <summary>Set from the password boxes (code-behind). Never stored.</summary>
    public void SetSecrets(string token, string backupPassword)
    {
        _token = token;
        _backupPassword = backupPassword;
        RaiseCan();
    }

    private bool TryPort(out int port) => int.TryParse(_port, NumberStyles.None, CultureInfo.InvariantCulture, out port) && port is >= 1 and <= 65535;

    private void RaiseCan()
    {
        OnPropertyChanged(nameof(HostError));
        DownloadCommand.RaiseCanExecuteChanged();
    }

    private void Enable()
    {
        if (!_dialogs.Confirm(Loc.T("Lan.EnableConfirm"))) return;
        _settings.LanFeatureEnabled = true;
        _store.Save(_settings);
        RaiseEnabled();
    }

    private void Disable()
    {
        _settings.LanFeatureEnabled = false;
        _store.Save(_settings);
        _token = "";
        _backupPassword = "";
        RaiseEnabled();
    }

    private void RaiseEnabled()
    {
        OnPropertyChanged(nameof(Enabled));
        EnableCommand.RaiseCanExecuteChanged();
        DisableCommand.RaiseCanExecuteChanged();
        RaiseCan();
    }

    public async Task DownloadAsync()
    {
        if (!Enabled || !LanBackupClient.TryParseTarget(_host, out var address) || !TryPort(out var port)) return;
        var key = $"{address}:{port}";
        var request = new LanBackupRequest(address, port, _token, _vdom.Trim().Length > 0 ? _vdom.Trim() : null,
            _fortiOs801 ? BackupProtection.SecretsWithPassword : BackupProtection.Standard, _fortiOs801 ? _backupPassword : null);
        IsBusy = true;
        Status = Loc.F("Lan.Connecting", key);
        _cts = new CancellationTokenSource();
        try
        {
            _settings.LanConfirmedCertificates.TryGetValue(key, out var confirmed);
            var result = await _download(request, confirmed, _cts.Token);
            if (result.Outcome == LanOutcome.CertificateNotConfirmed && result.CertificateSha256 is { } fp)
            {
                if (!_dialogs.Confirm(Loc.F("Lan.CertificateConfirm", key, result.CertificateSubject ?? "?", fp)))
                {
                    Status = Loc.T("Lan.CertificateRefused");
                    return;
                }

                _settings.LanConfirmedCertificates[key] = fp;
                _store.Save(_settings);
                OnPropertyChanged(nameof(ConfirmedCount));
                result = await _download(request, fp, _cts.Token);
            }

            Status = Loc.T("Lan.Outcome." + result.Outcome) + (result.HttpStatus is { } s and not 200 ? $" (HTTP {s})" : "");
            if (result.Outcome == LanOutcome.Success && result.Content is { } bytes)
            {
                var inspection = ConfigInspector.Inspect(bytes);
                var suggested = $"{inspection.Header?.PlatformCode ?? "fortigate"}_{address.ToString().Replace(':', '-')}_{DateTime.Now:yyyyMMdd-HHmm}.conf";
                if (_dialogs.SaveFile(Loc.T("Lan.Save"), MigrationWizardViewModel.ConfigFilter, suggested) is { } path)
                {
                    Shiftwall.Features.Migration.SafeOutput.Write(path, bytes, [], allowOverwrite: true);
                    Status = Loc.F("Lan.Saved", System.IO.Path.GetFileName(path));
                    Downloaded?.Invoke(this, path);
                }
            }
        }
        catch (OperationCanceledException)
        {
            Status = Loc.T("Lan.Cancelled");
        }
        finally
        {
            // Secrets are not kept after the attempt.
            _token = "";
            _backupPassword = "";
            _cts.Dispose();
            _cts = null;
            IsBusy = false;
            SecretsCleared?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>The view clears its password boxes when this is raised.</summary>
    public event EventHandler? SecretsCleared;
}
