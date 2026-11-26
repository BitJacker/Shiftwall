using System.Collections.ObjectModel;
using System.Text;
using Shiftwall.Core.Config;
using Shiftwall.Features.Migration;
using Shiftwall.Features.Rebuild;
using Shiftwall.Presentation.Localization;
using Shiftwall.Presentation.Mvvm;

namespace Shiftwall.Presentation.ViewModels;

/// <summary>How the configuration is migrated.</summary>
public enum MigrationMode
{
    /// <summary>Shiftwall chooses: rebuild when the FortiOS versions differ and rules exist, restore otherwise.</summary>
    Auto,

    /// <summary>The backup is kept as it is, with the interfaces renamed (same or close versions).</summary>
    Restore,

    /// <summary>The configuration is rewritten as a clean CLI script in the target syntax.</summary>
    Rebuild,
}

/// <summary>The rebuild mode of the wizard (different FortiOS versions).</summary>
public sealed partial class MigrationWizardViewModel
{
    private MigrationMode _mode = MigrationMode.Auto;
    private RebuildResult? _rebuild;
    private bool _generatePasswords = true;
    private string _currentAdmin = "admin";
    private string? _managementInterface;
    private string? _passwordSummary;
    private string _vaultPassword = "";
    private string _scriptPreview = "";

    public IReadOnlyList<Choice<MigrationMode>> ModeChoices { get; } =
    [
        new(MigrationMode.Auto, "Rebuild.Mode.Auto"),
        new(MigrationMode.Restore, "Rebuild.Mode.Restore"),
        new(MigrationMode.Rebuild, "Rebuild.Mode.Rebuild"),
    ];

    public IReadOnlyList<Choice<AddressingMode>> AddressingChoices { get; } =
    [
        new(AddressingMode.KeepSource, "Rebuild.Addressing.Keep"),
        new(AddressingMode.Static, "Rebuild.Addressing.Static"),
        new(AddressingMode.Dhcp, "Rebuild.Addressing.Dhcp"),
    ];

    public ObservableCollection<SecretRow> Secrets { get; } =
    [
        new(SecretCategory.Admin), new(SecretCategory.LocalUser), new(SecretCategory.VpnPsk),
        new(SecretCategory.Server), new(SecretCategory.Pppoe), new(SecretCategory.Other),
    ];

    public ObservableCollection<CountRow> Counts { get; } = [];

    public ObservableCollection<ManualRow> ManualRows { get; } = [];

    public ObservableCollection<MessageItem> RebuildFindings { get; } = [];

    public MigrationMode Mode
    {
        get => _mode;
        set
        {
            if (Set(ref _mode, value)) OnInputsChanged();
        }
    }

    private string? SourceVersion => _source?.Document.Header.Version;

    private string? TargetVersion => Target.BuildProfile()?.Version;

    /// <summary>True when Shiftwall has a schema and rules from the source version to the target version.</summary>
    public bool RebuildSupported => RebuildEngine.Supports(SourceVersion, TargetVersion);

    private bool VersionsDiffer => SourceVersion is not null && TargetVersion is not null
        && RebuildEngine.MajorMinor(SourceVersion) != RebuildEngine.MajorMinor(TargetVersion);

    /// <summary>The mode actually used.</summary>
    public bool IsRebuild => _mode switch
    {
        MigrationMode.Rebuild => true,
        MigrationMode.Restore => false,
        _ => VersionsDiffer && RebuildSupported,
    };

    /// <summary>Why this mode, and a warning when the user forced the other one.</summary>
    public IReadOnlyList<MessageItem> ModeMessages
    {
        get
        {
            var list = new List<MessageItem>();
            if (_source is null || TargetVersion is null) return list;
            var src = SourceVersion ?? "?";
            var dst = TargetVersion ?? "?";
            list.Add(MessageItem.From(MigrationFinding.Info(IsRebuild ? RebuildCodes.ModeRebuild : RebuildCodes.ModeRestore, src, dst)));
            var auto = VersionsDiffer && RebuildSupported;
            if (_mode != MigrationMode.Auto && IsRebuild != auto)
            {
                list.Add(MessageItem.From(MigrationFinding.Warning(RebuildCodes.ModeForced, Loc.T(IsRebuild ? "Rebuild.Mode.Rebuild" : "Rebuild.Mode.Restore"))));
            }

            if (VersionsDiffer && !RebuildSupported)
            {
                list.Add(new MessageItem(ItemSeverity.Warning, "RB-NORULES", () => Loc.F("Rebuild.NoRules", src, dst)));
            }

            if (IsRebuild && !RebuildSupported)
            {
                list.Add(new MessageItem(ItemSeverity.Error, "RB-NORULES", () => Loc.F("Rebuild.NoRules", src, dst)));
            }

            return list;
        }
    }

    public bool GeneratePasswords
    {
        get => _generatePasswords;
        set
        {
            if (Set(ref _generatePasswords, value)) RaiseCommands();
        }
    }

    public string CurrentAdmin { get => _currentAdmin; set => Set(ref _currentAdmin, value?.Trim() ?? ""); }

    public string? ManagementInterface { get => _managementInterface; set => Set(ref _managementInterface, string.IsNullOrWhiteSpace(value) ? null : value.Trim()); }

    /// <summary>Masked script shown in the transform step.</summary>
    public string ScriptPreview { get => _scriptPreview; private set => Set(ref _scriptPreview, value); }

    /// <summary>The passwords, shown once after the rebuild; null once hidden.</summary>
    public string? PasswordSummary
    {
        get => _passwordSummary;
        private set
        {
            if (Set(ref _passwordSummary, value)) HidePasswordsCommand.RaiseCanExecuteChanged();
        }
    }

    public RebuildResult? RebuildResult => _rebuild;

    private bool SecretsValid => Secrets.All(s => !s.HasPassword || s.IsValid);

    private bool CanSaveRebuild => _rebuild is { IsBlocked: false, LintPassed: true };

    /// <summary>Target interface names offered in the mapping step: physical and logical (switches) in the rebuild mode.</summary>
    private IReadOnlyList<string> TargetInterfaceNames(TargetProfile target) =>
        IsRebuild ? RebuildTargets.From(target, Target.IsBackupMode ? Target.Backup?.Document : null).Interfaces : target.PhysicalInterfaces;

    /// <summary>Suggests the management interface: the first target that a source LAN-like port is mapped to.</summary>
    private void SuggestManagement()
    {
        if (_managementInterface is not null && TargetPorts.Any(t => t.Name == _managementInterface)) return;
        ManagementInterface = SourcePorts.FirstOrDefault(s => s.Target is not null && InterfaceMapping.IsLanLike(s.Name))?.Target
            ?? TargetPorts.FirstOrDefault(t => t.Name == "lan")?.Name;
    }

    public void SetVaultPassword(string value) => _vaultPassword = value ?? "";

    // ------------------------------------------------------------------ run

    /// <summary>Runs the rebuild in memory. Nothing is written until the user saves.</summary>
    public async Task RunRebuildAsync()
    {
        var target = Target.BuildProfile();
        if (_source is null || target is null) return;
        if (!SecretsValid)
        {
            _dialogs.ShowError(Loc.T("Rebuild.Secret.Invalid"));
            return;
        }

        IsBusy = true;
        try
        {
            var plans = SourcePorts.Select(s => new InterfacePlan(
                s.Name,
                s.Target,
                s.Addressing.Mode,
                s.Addressing.Address,
                string.IsNullOrWhiteSpace(s.Addressing.Gateway) ? null : s.Addressing.Gateway.Trim(),
                s.Addressing.NameServers.Split([' ', ',', ';'], StringSplitOptions.RemoveEmptyEntries))).ToList();
            var secrets = new SecretPlan
            {
                Generate = _generatePasswords,
                Provided = Secrets.Where(s => s.IsValid).ToDictionary(s => s.Category, s => s.Password),
            };
            var request = new RebuildRequest(
                _source.Document,
                SourceVersion ?? "",
                RebuildTargets.From(target, Target.IsBackupMode ? Target.Backup?.Document : null),
                plans,
                secrets,
                new RebuildOptions(string.IsNullOrEmpty(_currentAdmin) ? "admin" : _currentAdmin, _managementInterface));
            _rebuild = await Task.Run(() => RebuildEngine.Run(request));
            PublishRebuild();
            SavedPath = null;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void PublishRebuild()
    {
        Counts.Clear();
        ManualRows.Clear();
        RebuildFindings.Clear();
        Checklist.Clear();
        if (_rebuild is null) return;
        foreach (var c in _rebuild.Counts) Counts.Add(new CountRow(c.Path, c.Source, c.Rebuilt, c.Converted, c.Manual));
        foreach (var m in _rebuild.Manual) ManualRows.Add(new ManualRow(m.Path, m.Object, m.Format(Loc.Instance.Culture), m.Original));
        foreach (var f in _rebuild.Findings) RebuildFindings.Add(MessageItem.From(f));
        foreach (var l in _rebuild.Lint.Where(l => l.Severity == LintSeverity.Error))
        {
            RebuildFindings.Add(new MessageItem(ItemSeverity.Error, l.Code, () => Loc.F("Rebuild.LintError", l.Line, l.Code, l.Detail)));
        }

        foreach (var k in RebuildReport.Checklist) Checklist.Add(new MessageItem(ItemSeverity.Info, k, () => MigrationText.Get(k, Loc.Instance.Culture)));
        ScriptPreview = _rebuild.FullScript(maskSecrets: true);
        PasswordSummary = _rebuild.Secrets.Count == 0 ? null : RebuildVault.Summary(_rebuild.Secrets, c => Loc.T("Rebuild.Secret." + c));
        RaiseCommands();
    }

    /// <summary>Hides the passwords: they stay only in the script and in the encrypted file, if saved.</summary>
    public void HidePasswords() => PasswordSummary = null;

    // ------------------------------------------------------------------ save

    private string OutputFolder(string kind)
    {
        var name = $"{Path.GetFileNameWithoutExtension(_sourcePath ?? "source")}-to-{Target.Label}-{kind}";
        return new string([.. name.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c)]);
    }

    private IEnumerable<string> ProtectedPaths() => new[] { _sourcePath, Target.BackupPath }.OfType<string>();

    /// <summary>Saves the numbered script parts. A part that fails the final lint is never saved.</summary>
    public void SaveScripts(bool forGui)
    {
        if (_rebuild is null || !CanSaveRebuild) return;
        var files = forGui ? _rebuild.GuiFiles($"{Path.GetFileName(_sourcePath)} to {Target.Label}") : _rebuild.TerminalFiles();
        foreach (var f in files)
        {
            var issues = CliLint.Check(f.Text, _rebuild.TargetSchema, allowComments: forGui).Where(l => l.Severity == LintSeverity.Error).ToList();
            if (issues.Count > 0)
            {
                _dialogs.ShowError(Loc.F("Rebuild.LintBlocked", f.FileName, string.Join("; ", issues.Take(5).Select(l => $"{l.Line}: {l.Code} {l.Detail}"))));
                return;
            }
        }

        if (_dialogs.PickFolder(Loc.T(forGui ? "Rebuild.Save.Gui" : "Rebuild.Save.Terminal")) is not { } parent) return;
        var folder = Path.Combine(parent, OutputFolder(forGui ? "gui" : "terminal"));
        try
        {
            Directory.CreateDirectory(folder);
            var utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
            foreach (var f in files) SafeOutput.Write(Path.Combine(folder, f.FileName), utf8.GetBytes(f.Text), ProtectedPaths(), allowOverwrite: true);
            SavedPath = Path.Combine(folder, files[0].FileName);
            _dialogs.ShowInfo(Loc.F("Rebuild.Saved", files.Count, folder));
        }
        catch (UnsafeOutputException)
        {
            _dialogs.ShowError(Loc.T("Error.WouldOverwriteOriginal"));
        }
    }

    public void SaveSecrets()
    {
        if (_rebuild is null || _rebuild.Secrets.Count == 0) return;
        if (_vaultPassword.Length < Features.Toolbox.Anonymize.MapProtector.MinPasswordLength)
        {
            _dialogs.ShowError(Loc.T("Rebuild.Vault.TooShort"));
            return;
        }

        var suggested = Path.Combine(Path.GetDirectoryName(_sourcePath ?? ".") ?? ".", OutputFolder("passwords") + RebuildVault.Extension);
        if (_dialogs.SaveFile(Loc.T("Rebuild.Save.Secrets"), "Shiftwall secrets (*.swsecret)|*.swsecret", suggested) is not { } path) return;
        try
        {
            SafeOutput.Write(path, RebuildVault.Encrypt(_rebuild.Secrets, _vaultPassword), ProtectedPaths(), allowOverwrite: true);
            _dialogs.ShowInfo(Loc.F("Wizard.Report.Saved", Path.GetFileName(path)));
        }
        catch (UnsafeOutputException)
        {
            _dialogs.ShowError(Loc.T("Error.WouldOverwriteOriginal"));
        }
    }

    public void ExportRebuildHtml()
    {
        if (_rebuild is null) return;
        var suggested = Path.Combine(Path.GetDirectoryName(_sourcePath ?? ".") ?? ".", OutputFolder("report") + ".html");
        if (_dialogs.SaveFile(Loc.T("Wizard.Report.ExportHtml"), "HTML (*.html)|*.html", suggested) is not { } path) return;
        var html = RebuildReport.Build(_rebuild, ReportContext(_savedPath));
        try
        {
            SafeOutput.Write(path, Encoding.UTF8.GetBytes(html), ProtectedPaths(), allowOverwrite: true);
        }
        catch (UnsafeOutputException)
        {
            _dialogs.ShowError(Loc.T("Error.WouldOverwriteOriginal"));
        }
    }

    private void ResetRebuild()
    {
        _rebuild = null;
        Counts.Clear();
        ManualRows.Clear();
        RebuildFindings.Clear();
        ScriptPreview = "";
        PasswordSummary = null;
        foreach (var s in Secrets)
        {
            s.SetPassword("");
            s.SetConfirm("");
        }
    }

    private void RaiseModeProperties()
    {
        OnPropertyChanged(nameof(IsRebuild));
        OnPropertyChanged(nameof(RebuildSupported));
        OnPropertyChanged(nameof(ModeMessages));
    }
}
