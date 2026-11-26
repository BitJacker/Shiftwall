using System.Collections.ObjectModel;
using System.Globalization;
using Shiftwall.Features.Migration;
using Shiftwall.Features.Toolbox.Anonymize;
using Shiftwall.Features.Toolbox.Export;
using Shiftwall.Features.Toolbox.TwoFactor;
using Shiftwall.Lan;
using Shiftwall.Presentation.Localization;
using Shiftwall.Presentation.Mvvm;
using Shiftwall.Presentation.Services;

namespace Shiftwall.Presentation.ViewModels.Toolbox;

public enum AccountFilter
{
    All,
    NoEmail,
    NoTwoFactor,
    WithFortiToken,
    AdminsOnly,
    UsersOnly,
    Ldap,
    Radius,
}

/// <summary>A value with a localized label (combo boxes).</summary>
public sealed class Choice<T>(T value, string key) : ObservableObject
{
    public T Value { get; } = value;

    public string Label => Loc.T(key);
}

/// <summary>An account row of the table (selectable for bulk edits).</summary>
public sealed class AccountRow(AccountEntry entry) : ObservableObject
{
    private bool _selected;

    public AccountEntry Entry { get; } = entry;

    public bool IsSelected { get => _selected; set => Set(ref _selected, value); }

    public string Kind => TwoFactorReport.Kind(Entry.Kind, Loc.Instance.Culture);
}

/// <summary>A row of the dry run: one account, values before and after.</summary>
public sealed record PlanRow(string Account, string Kind, string Action, string EmailBefore, string EmailAfter, string TwoFactorBefore, string TwoFactorAfter);

/// <summary>A FortiToken holder whose switch to email codes needs a confirmation.</summary>
public sealed class TokenConfirmRow(AccountKey key, string text, bool confirmed, Action changed) : ObservableObject
{
    private bool _confirmed = confirmed;

    public AccountKey Key { get; } = key;

    public string Text { get; } = text;

    public bool Confirmed { get => _confirmed; set { if (Set(ref _confirmed, value)) changed(); } }
}

/// <summary>
/// "Email 2FA": sets the email and email two-factor authentication on local users and administrators in bulk, by
/// generating CLI scripts (with rollback) from a backup. Reading the account list through the LAN is optional and
/// read-only; nothing is ever written to a FortiGate by this page.
/// </summary>
public sealed class TwoFactorViewModel : ObservableObject
{
    public const string RequestFilter = "CSV / Excel (*.csv;*.xlsx)|*.csv;*.xlsx|CSV (*.csv)|*.csv|Excel (*.xlsx)|*.xlsx";

    /// <summary>Example of the block the module requires (FortiOS CLI Reference, config system email-server).</summary>
    public const string EmailServerSnippet = """
        config system email-server
            set type custom
            set server "smtp.example.com"
            set port 465
            set security smtps
            set authenticate enable
            set username "fortigate@example.com"
            set password <SMTP_PASSWORD>
        end
        """;

    private readonly IDialogService _dialogs;
    private readonly IShell _shell;
    private readonly SettingsStore? _store;
    private readonly AppSettings? _settings;
    private readonly Func<LanAccountsRequest, string?, CancellationToken, Task<LanAccountsResult>> _readLan;
    private readonly HashSet<AccountKey> _confirmedTokens = [];
    private readonly List<ChangeRequest> _requests = [];
    private List<PlanFinding> _inputErrors = [];
    private AccountInventory? _inventory;
    private bool _fromLan;
    private TwoFactorPlan? _plan;
    private AccountFilter _filter;
    private string _search = "";
    private string _editEmail = "";
    private ChangeAction _editAction = ChangeAction.Set;
    private string _domain = "";
    private string _template = "";
    private bool _createMissing;
    private string _batchSize = "500";
    private bool _timers;
    private bool _adminConfirm1;
    private bool _adminConfirm2;
    private string _vaultPassword = "";
    private string _vaultConfirm = "";
    private string _status = "";
    private string _lanHost = "";
    private string _lanPort = "443";
    private string _lanVdoms = "";
    private string _lanToken = "";
    private bool _busy;

    public TwoFactorViewModel(IDialogService dialogs, IShell shell, SettingsStore? store = null, AppSettings? settings = null,
        Func<LanAccountsRequest, string?, CancellationToken, Task<LanAccountsResult>>? readLan = null)
    {
        _dialogs = dialogs;
        _shell = shell;
        _store = store;
        _settings = settings;
        _readLan = readLan ?? new LanAccountsClient().ReadAsync;
        Config = new ConfigSlotViewModel(dialogs, "TwoFactor.Pick");
        Config.Changed += (_, _) => LoadFromBackup();
        AddSelectedCommand = new RelayCommand(AddSelected, () => Accounts.Any(a => a.IsSelected));
        SelectAllCommand = new RelayCommand(() => { foreach (var r in Visible) r.IsSelected = true; AddSelectedCommand.RaiseCanExecuteChanged(); });
        SelectNoneCommand = new RelayCommand(() => { foreach (var r in Accounts) r.IsSelected = false; AddSelectedCommand.RaiseCanExecuteChanged(); });
        ImportCommand = new AsyncRelayCommand(ImportAsync, () => _inventory is not null);
        SaveTemplateCommand = new RelayCommand(() => ToolOutput.Save(_dialogs, "TwoFactor.SaveTemplate", "CSV (*.csv)|*.csv", "email-2fa.csv", ToolOutput.Utf8(TwoFactorInput.Template()), [Config.Path]));
        ClearRequestsCommand = new RelayCommand(() => { _requests.Clear(); _inputErrors = []; Recompute(); }, () => _requests.Count > 0);
        GenerateCommand = new RelayCommand(Generate, () => CanGenerate);
        ExportInventoryCommand = new RelayCommand(ExportInventory, () => _inventory is not null);
        CopySnippetCommand = new RelayCommand(() => _shell.CopyText(EmailServerSnippet.ReplaceLineEndings("\n") + "\n"));
        ReadLanCommand = new AsyncRelayCommand(ReadLanAsync, () => LanEnabled && !IsBusy && LanBackupClient.TryParseTarget(_lanHost, out _) && _lanToken.Length > 0);
        Loc.Instance.PropertyChanged += (_, _) => { OnPropertyChanged(nameof(Summary)); OnPropertyChanged(nameof(EmailServerStatus)); Recompute(); };
    }

    public ConfigSlotViewModel Config { get; }

    public ObservableCollection<AccountRow> Accounts { get; } = [];

    public IEnumerable<AccountRow> Visible => Accounts.Where(Matches);

    public ObservableCollection<MessageItem> Findings { get; } = [];

    public ObservableCollection<PlanRow> DryRun { get; } = [];

    public ObservableCollection<TokenConfirmRow> TokenConfirmations { get; } = [];

    public RelayCommand AddSelectedCommand { get; }

    public RelayCommand SelectAllCommand { get; }

    public RelayCommand SelectNoneCommand { get; }

    public AsyncRelayCommand ImportCommand { get; }

    public RelayCommand SaveTemplateCommand { get; }

    public RelayCommand ClearRequestsCommand { get; }

    public RelayCommand GenerateCommand { get; }

    public RelayCommand ExportInventoryCommand { get; }

    public RelayCommand CopySnippetCommand { get; }

    public AsyncRelayCommand ReadLanCommand { get; }

    public IReadOnlyList<Choice<AccountFilter>> Filters { get; } = [.. Enum.GetValues<AccountFilter>().Select(f => new Choice<AccountFilter>(f, "TwoFactor.Filter." + f))];

    public IReadOnlyList<Choice<ChangeAction>> ActionChoices { get; } = [.. Enum.GetValues<ChangeAction>().Select(a => new Choice<ChangeAction>(a, "TwoFactor.Action." + a))];

    public AccountFilter Filter { get => _filter; set { if (Set(ref _filter, value)) OnPropertyChanged(nameof(Visible)); } }

    public string Search { get => _search; set { if (Set(ref _search, value)) OnPropertyChanged(nameof(Visible)); } }

    public string EditEmail { get => _editEmail; set => Set(ref _editEmail, value); }

    public ChangeAction EditAction { get => _editAction; set => Set(ref _editAction, value); }

    public string DefaultDomain { get => _domain; set { if (Set(ref _domain, value)) Recompute(); } }

    public string EmailTemplate { get => _template; set { if (Set(ref _template, value)) Recompute(); } }

    public bool CreateMissingUsers { get => _createMissing; set { if (Set(ref _createMissing, value)) Recompute(); } }

    public string BatchSize { get => _batchSize; set { if (Set(ref _batchSize, value)) RaiseGenerate(); } }

    public bool IncludeTimers { get => _timers; set => Set(ref _timers, value); }

    public bool AdminConfirm1 { get => _adminConfirm1; set { if (Set(ref _adminConfirm1, value)) Recompute(); } }

    public bool AdminConfirm2 { get => _adminConfirm2; set { if (Set(ref _adminConfirm2, value)) Recompute(); } }

    public bool TouchesAdmins => _plan?.TouchesAdmins == true;

    public bool HasTokenConfirmations => TokenConfirmations.Count > 0;

    public bool NeedsVaultPassword => _plan?.Items.Any(i => i.NeedsPassword) == true;

    public string Status { get => _status; private set => Set(ref _status, value); }

    public bool IsBusy { get => _busy; private set { if (Set(ref _busy, value)) ReadLanCommand.RaiseCanExecuteChanged(); } }

    public bool FromLan => _fromLan;

    public string Summary => _inventory is null ? Loc.T("TwoFactor.LoadFirst") : Loc.F("TwoFactor.Summary",
        _inventory.Users.Count(), _inventory.Admins.Count(), _inventory.Accounts.Count(a => a.EmailTo is null), _inventory.Accounts.Count(a => a.HasEmailTwoFactor), _inventory.Accounts.Count(a => a.HasFortiToken));

    public bool EmailServerMissing => _inventory is not null && !_fromLan && !_inventory.EmailServer.IsUsable;

    public string EmailServerStatus => _inventory is null ? "" : _fromLan ? Loc.T("TwoFactor.EmailServer.FromLan")
        : _inventory.EmailServer.IsUsable ? Loc.F("TwoFactor.EmailServer.Ok", _inventory.EmailServer.Server!, _inventory.EmailServer.Port ?? "-", _inventory.EmailServer.Security ?? "-")
        : Loc.T("TwoFactor.EmailServer.Missing");

    public string RequestSummary => Loc.F("TwoFactor.Requests", _requests.Count, _plan?.Items.Count ?? 0, _plan?.Skipped.Count ?? 0);

    public string VaultHint => !NeedsVaultPassword ? "" : _vaultPassword.Length < MapProtector.MinPasswordLength
        ? Loc.F("Anonymize.PasswordShort", MapProtector.MinPasswordLength)
        : _vaultPassword != _vaultConfirm ? Loc.T("Anonymize.PasswordMismatch") : "";

    private bool VaultOk => !NeedsVaultPassword || (_vaultPassword.Length >= MapProtector.MinPasswordLength && _vaultPassword == _vaultConfirm);

    private bool BatchOk => int.TryParse(_batchSize, NumberStyles.None, CultureInfo.InvariantCulture, out var n) && n is >= ScriptOptions.MinBatch and <= ScriptOptions.MaxBatch;

    public bool CanGenerate => _plan is { IsBlocked: false, Items.Count: > 0 } && !_fromLan && BatchOk && VaultOk;

    // ------------------------------------------------------------------ LAN (read only)

    public bool LanEnabled => _settings?.LanFeatureEnabled == true;

    public string LanHost { get => _lanHost; set { if (Set(ref _lanHost, value)) ReadLanCommand.RaiseCanExecuteChanged(); } }

    public string LanPort { get => _lanPort; set => Set(ref _lanPort, value); }

    public string LanVdoms { get => _lanVdoms; set => Set(ref _lanVdoms, value); }

    /// <summary>Set from the password box (code-behind). Never stored.</summary>
    public void SetToken(string token)
    {
        _lanToken = token;
        ReadLanCommand.RaiseCanExecuteChanged();
    }

    /// <summary>Set from the password boxes (code-behind). Kept in memory only.</summary>
    public void SetVaultPassword(string password, string confirm)
    {
        _vaultPassword = password;
        _vaultConfirm = confirm;
        OnPropertyChanged(nameof(VaultHint));
        RaiseGenerate();
    }

    public async Task ReadLanAsync()
    {
        if (!LanBackupClient.TryParseTarget(_lanHost, out var address) || !int.TryParse(_lanPort, NumberStyles.None, CultureInfo.InvariantCulture, out var port) || port is < 1 or > 65535) return;
        var vdoms = _lanVdoms.Split([',', ';', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var request = new LanAccountsRequest(address, port, _lanToken, vdoms);
        var key = $"{address}:{port}";
        IsBusy = true;
        Status = Loc.F("Lan.Connecting", key);
        try
        {
            string? confirmed = null;
            _settings?.LanConfirmedCertificates.TryGetValue(key, out confirmed);
            var result = await _readLan(request, confirmed, CancellationToken.None);
            if (result.Outcome == LanOutcome.CertificateNotConfirmed && result.CertificateSha256 is { } fp)
            {
                if (!_dialogs.Confirm(Loc.F("Lan.CertificateConfirm", key, result.CertificateSubject ?? "?", fp)))
                {
                    Status = Loc.T("Lan.CertificateRefused");
                    return;
                }

                if (_settings is not null)
                {
                    _settings.LanConfirmedCertificates[key] = fp;
                    _store?.Save(_settings);
                }

                result = await _readLan(request, fp, CancellationToken.None);
            }

            if (result.Outcome != LanOutcome.Success)
            {
                Status = Loc.T("Lan.Outcome." + result.Outcome) + (result.HttpStatus is { } s ? $" (HTTP {s})" : "");
                return;
            }

            Status = Loc.F("TwoFactor.Lan.ReadOk", result.Accounts.Count);
            var inventory = new AccountInventory(result.Accounts, new EmailServerSettings(false, null, null, null, null, null, null), null, null, [], [], vdoms.Length > 0, vdoms, null);
            SetInventory(inventory, fromLan: true);
            if (result.AdminsEmpty) Status += " " + Loc.T("TwoFactor.Lan.AdminsEmpty");
        }
        finally
        {
            IsBusy = false;
        }
    }

    // ------------------------------------------------------------------ inventory and requests

    private void LoadFromBackup()
    {
        if (Config.Document is not { } doc)
        {
            _inventory = null;
            Accounts.Clear();
            Recompute();
            return;
        }

        SetInventory(AccountInventory.Read(doc), fromLan: false);
    }

    private void SetInventory(AccountInventory inventory, bool fromLan)
    {
        _inventory = inventory;
        _fromLan = fromLan;
        Accounts.Clear();
        foreach (var a in inventory.Accounts) Accounts.Add(new AccountRow(a));
        foreach (var r in Accounts) r.PropertyChanged += (_, _) => AddSelectedCommand.RaiseCanExecuteChanged();
        _requests.Clear();
        _inputErrors = [];
        _confirmedTokens.Clear();
        OnPropertyChanged(nameof(Visible));
        OnPropertyChanged(nameof(Summary));
        OnPropertyChanged(nameof(EmailServerStatus));
        OnPropertyChanged(nameof(EmailServerMissing));
        OnPropertyChanged(nameof(FromLan));
        ImportCommand.RaiseCanExecuteChanged();
        ExportInventoryCommand.RaiseCanExecuteChanged();
        Recompute();
    }

    private bool Matches(AccountRow r)
    {
        var a = r.Entry;
        var ok = _filter switch
        {
            AccountFilter.NoEmail => a.EmailTo is null,
            AccountFilter.NoTwoFactor => a.EffectiveTwoFactor == "disable",
            AccountFilter.WithFortiToken => a.HasFortiToken,
            AccountFilter.AdminsOnly => a.IsAdmin,
            AccountFilter.UsersOnly => !a.IsAdmin,
            AccountFilter.Ldap => a.Type == "ldap",
            AccountFilter.Radius => a.Type == "radius",
            _ => true,
        };
        return ok && (_search.Length == 0 || a.Name.Contains(_search, StringComparison.OrdinalIgnoreCase) || (a.EmailTo?.Contains(_search, StringComparison.OrdinalIgnoreCase) ?? false));
    }

    /// <summary>Adds the selected rows to the requests with the action and email typed above the table.</summary>
    public void AddSelected()
    {
        foreach (var r in Accounts.Where(a => a.IsSelected))
        {
            _requests.RemoveAll(q => q.Username == r.Entry.Name && q.Kind == r.Entry.Kind && q.Vdom == r.Entry.Vdom);
            var email = _editEmail.Trim().Length > 0 ? _editEmail.Trim() : null;
            _requests.Add(new ChangeRequest(0, r.Entry.Name, email, _editAction, Kind: r.Entry.Kind, Vdom: r.Entry.Vdom));
            r.IsSelected = false;
        }

        Recompute();
    }

    public async Task ImportAsync()
    {
        if (_dialogs.OpenFile(Loc.T("TwoFactor.ImportPick"), RequestFilter) is not { } path) return;
        await ImportFileAsync(path);
    }

    public async Task ImportFileAsync(string path)
    {
        try
        {
            var (requests, errors) = path.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase)
                ? TwoFactorInput.ParseXlsx(await Shiftwall.Core.Config.InputFile.ReadAllBytesAsync(path))
                : TwoFactorInput.ParseCsv(System.Text.Encoding.UTF8.GetString(await Shiftwall.Core.Config.InputFile.ReadAllBytesAsync(path)));
            _requests.Clear();
            _requests.AddRange(requests);
            _inputErrors = errors;
            Recompute();
        }
        catch (Exception ex) when (ex is InvalidDataException or System.Xml.XmlException or System.IO.InvalidDataException)
        {
            _dialogs.ShowError(Loc.T("TwoFactor.ImportFailed"));
        }
    }

    /// <summary>Replaces the requests (tests and drag &amp; drop).</summary>
    public void SetRequests(IEnumerable<ChangeRequest> requests)
    {
        _requests.Clear();
        _requests.AddRange(requests);
        _inputErrors = [];
        Recompute();
    }

    private PlanOptions Options() => new(
        new EmailRule(_domain.Trim().Length > 0 ? _domain.Trim() : null, _template.Trim().Length > 0 ? _template.Trim() : null),
        _createMissing, _confirmedTokens, _adminConfirm1, _adminConfirm2);

    private void Recompute()
    {
        Findings.Clear();
        DryRun.Clear();
        _plan = null;
        if (_inventory is not null && _requests.Count > 0)
        {
            _plan = TwoFactorPlanner.Plan(_inventory, _requests, Options());
            if (_fromLan) Findings.Add(new MessageItem(ItemSeverity.Warning, "FromLan", () => Loc.T("TwoFactor.Lan.BackupNeeded")));
            foreach (var f in _inputErrors.Concat(_plan.Findings).Where(f => !(_fromLan && f.Code == "EmailServerMissing")))
            {
                var sev = f.Severity switch { PlanSeverity.Error => ItemSeverity.Error, PlanSeverity.Info => ItemSeverity.Info, _ => ItemSeverity.Warning };
                Findings.Add(new MessageItem(sev, f.Code, () => f.Format(Loc.Instance.Culture)));
            }

            foreach (var i in _plan.Items)
            {
                DryRun.Add(new PlanRow(i.Key.ToString(), TwoFactorReport.Kind(i.Key.Kind, Loc.Instance.Culture),
                    i.Create ? Loc.T("TwoFactor.Create") : Loc.T("TwoFactor.Modify"),
                    i.EmailBefore ?? "", i.EmailAfter ?? "", i.TwoFactorBefore ?? "disable", i.TwoFactorAfter ?? "disable"));
            }
        }

        TokenConfirmations.Clear();
        foreach (var f in _plan?.Findings.Where(f => f.Code is "TokenConfirm" or "TokenConfirmed" && f.Account is not null) ?? [])
        {
            var key = f.Account!.Value;
            TokenConfirmations.Add(new TokenConfirmRow(key, f.Format(Loc.Instance.Culture), _confirmedTokens.Contains(key), () =>
            {
                if (!_confirmedTokens.Remove(key)) _confirmedTokens.Add(key);
                Recompute();
            }));
        }

        OnPropertyChanged(nameof(RequestSummary));
        OnPropertyChanged(nameof(TouchesAdmins));
        OnPropertyChanged(nameof(HasTokenConfirmations));
        OnPropertyChanged(nameof(NeedsVaultPassword));
        OnPropertyChanged(nameof(VaultHint));
        ClearRequestsCommand.RaiseCanExecuteChanged();
        RaiseGenerate();
    }

    private void RaiseGenerate()
    {
        OnPropertyChanged(nameof(CanGenerate));
        GenerateCommand.RaiseCanExecuteChanged();
    }

    // ------------------------------------------------------------------ outputs

    private void Generate()
    {
        if (!CanGenerate || _plan is null || _inventory is null) return;
        if (_plan.TouchesAdmins && !_dialogs.Confirm(Loc.F("TwoFactor.AdminFinalConfirm", _plan.Items.Count(i => i.IsAdmin)))) return;
        if (_dialogs.PickFolder(Loc.T("TwoFactor.PickFolder")) is not { } folder) return;

        var options = new ScriptOptions(int.Parse(_batchSize, CultureInfo.InvariantCulture), _timers, 300, Config.FileName);
        var bundle = TwoFactorScripts.Generate(_plan, _inventory, options);
        var protectedPaths = new[] { Config.Path };
        try
        {
            foreach (var f in bundle.Scripts.Concat(bundle.Rollbacks)) Write(folder, f.FileName, ToolOutput.Utf8(f.Text), protectedPaths);
            var c = Loc.Instance.Culture;
            Write(folder, "2fa-report.html", ToolOutput.Utf8(TwoFactorReport.Html(_plan, _inputErrors, _inventory, Config.FileName, bundle.Passwords.Count, c)), protectedPaths);
            Write(folder, "2fa-report.csv", CsvWriter.ToBytes(TwoFactorReport.Csv(_plan, _inputErrors, c)), protectedPaths);
            if (bundle.Passwords.Count > 0)
            {
                Write(folder, "2fa-new-users" + PasswordVault.Extension, PasswordVault.Encrypt(bundle.Passwords, _inventory, _vaultPassword), protectedPaths);
            }

            Status = Loc.F("TwoFactor.Generated", bundle.Scripts.Count, bundle.Rollbacks.Count, folder);
            _shell.OpenFolder(folder);
        }
        catch (UnsafeOutputException)
        {
            _dialogs.ShowError(Loc.T("Error.WouldOverwriteOriginal"));
        }
    }

    private static void Write(string folder, string name, byte[] bytes, IEnumerable<string?> protectedPaths) =>
        SafeOutput.Write(System.IO.Path.Combine(folder, name), bytes, protectedPaths.Where(p => p is not null)!, allowOverwrite: true);

    private void ExportInventory()
    {
        if (_inventory is null) return;
        var table = TwoFactorReport.Inventory(_inventory, Loc.Instance.Culture);
        if (_dialogs.SaveFile(Loc.T("TwoFactor.ExportPick"), "Excel (*.xlsx)|*.xlsx|CSV (*.csv)|*.csv", ToolOutput.Suggest(Config.Path, "accounts", ".xlsx")) is not { } path) return;
        var bytes = path.EndsWith(".csv", StringComparison.OrdinalIgnoreCase) ? CsvWriter.ToBytes(CsvWriter.Write(table)) : XlsxWriter.Write([table]);
        try
        {
            SafeOutput.Write(path, bytes, Config.Path is { } p ? [p] : [], allowOverwrite: true);
            _dialogs.ShowInfo(Loc.F("Tool.Saved", System.IO.Path.GetFileName(path)));
        }
        catch (UnsafeOutputException)
        {
            _dialogs.ShowError(Loc.T("Error.WouldOverwriteOriginal"));
        }
    }
}
