using System.Globalization;
using System.IO.Compression;
using System.Net;
using System.Text;
using Shiftwall.Core.Data;
using Shiftwall.Features.Toolbox.Anonymize;
using Shiftwall.Features.Toolbox.Import;
using Shiftwall.Lan;
using Shiftwall.Presentation.Localization;
using Shiftwall.Presentation.Services;
using Shiftwall.Presentation.ViewModels.Toolbox;

namespace Shiftwall.Presentation.Tests;

public sealed class ToolboxViewModelTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("shiftwall-tb-").FullName;
    private readonly FakeDialogs _dialogs = new();
    private readonly FakeShell _shell = new();

    public ToolboxViewModelTests()
    {
        Loc.Instance.Culture = CultureInfo.GetCultureInfo("en");
        foreach (var f in Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "Golden", "Toolbox"))) File.Copy(f, Path.Combine(_dir, Path.GetFileName(f)));
    }

    private string P(string name) => Path.Combine(_dir, name);

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Fact]
    public async Task Slot_RejectsBinaryFiles_AndSummarizesText()
    {
        var slot = new ConfigSlotViewModel(_dialogs, "Audit.Pick");
        await File.WriteAllBytesAsync(P("enc.conf"), [0x00, 0x01, 0xFE, 0x10, 0x00, 0x7F], TestContext.Current.CancellationToken);
        await slot.LoadAsync(P("enc.conf"));
        Assert.False(slot.IsLoaded);
        Assert.NotEmpty(slot.Diagnostics);

        await slot.LoadAsync(P("site-a.fgtcfg"));
        Assert.True(slot.IsLoaded);
        Assert.Contains("FGT60F", slot.Summary);
        Assert.Contains("7.4.13", slot.Summary);
    }

    [Fact]
    public async Task Compare_SameFile_IsIdentical()
    {
        var vm = new CompareViewModel(_dialogs);
        await vm.Left.LoadAsync(P("site-a.fgtcfg"));
        await vm.Right.LoadAsync(P("site-a.fgtcfg"));
        await vm.RunAsync();
        Assert.Equal(Loc.T("Compare.Identical"), vm.Summary);
        Assert.Empty(vm.Rows);

        await vm.Right.LoadAsync(P("site-b.fgtcfg"));
        await vm.RunAsync();
        Assert.NotEmpty(vm.Rows);
        vm.Filter = "firewall policy";
        Assert.All(vm.Rows, r => Assert.Contains("firewall policy", r.Location));
    }

    [Fact]
    public async Task Audit_ListsFailedRulesFirst_AndOpensSources()
    {
        var vm = new AuditViewModel(_dialogs, _shell);
        await vm.Config.LoadAsync(P("site-a.fgtcfg"));
        await vm.RunAsync();
        Assert.True(vm.HasReport);
        Assert.InRange(vm.Score, 0, 99);
        Assert.All(vm.Rules, r => Assert.False(r.Passed));
        Assert.Equal("AUD-001", vm.Rules[0].Id);

        vm.ShowPassed = true;
        Assert.Contains(vm.Rules, r => r.Passed);

        vm.OpenSourceCommand.Execute(vm.Rules[0].Sources[0].Url);
        Assert.StartsWith("https://", Assert.Single(_shell.Urls));

        _dialogs.SaveAnswers.Enqueue(P("audit.csv"));
        vm.ExportCsvCommand.Execute(null);
        Assert.Contains("AUD-001", await File.ReadAllTextAsync(P("audit.csv"), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Cleanup_ScriptFollowsSelection()
    {
        var vm = new CleanupViewModel(_dialogs, _shell);
        await vm.Config.LoadAsync(P("site-a.fgtcfg"));
        await vm.RunAsync();
        Assert.NotEmpty(vm.Items);
        Assert.Contains("delete \"old-nas\"", vm.Script);
        Assert.StartsWith("# ", vm.Script);

        vm.SelectNoneCommand.Execute(null);
        Assert.Equal("", vm.Script);
        Assert.False(vm.CopyCommand.CanExecute(null));

        vm.Items.First(i => i.Name == "old-printer").IsSelected = true;
        Assert.Contains("delete \"old-printer\"", vm.Script);
        Assert.DoesNotContain("old-nas", vm.Script);
        vm.CopyCommand.Execute(null);
        Assert.Equal(vm.Script, Assert.Single(_shell.Copied));
    }

    [Fact]
    public async Task Cleanup_ReferenceBackupExcludesItsObjects()
    {
        var vm = new CleanupViewModel(_dialogs, _shell);
        await vm.Config.LoadAsync(P("site-a.fgtcfg"));
        await vm.Reference.LoadAsync(P("site-a.fgtcfg"));
        await vm.RunAsync();
        Assert.Empty(vm.Items);
    }

    [Fact]
    public async Task Export_SavesXlsxAndCsv_NeverOverTheSource()
    {
        var vm = new ExportViewModel(_dialogs);
        await vm.Config.LoadAsync(P("site-a.fgtcfg"));
        await vm.RunAsync();
        Assert.Equal(8, vm.Tables.Count);
        Assert.NotNull(vm.Preview);

        _dialogs.SaveAnswers.Enqueue(P("export.xlsx"));
        vm.SaveXlsxCommand.Execute(null);
        using (var zip = ZipFile.OpenRead(P("export.xlsx"))) Assert.NotNull(zip.GetEntry("xl/workbook.xml"));

        var before = await File.ReadAllBytesAsync(P("site-a.fgtcfg"), TestContext.Current.CancellationToken);
        _dialogs.SaveAnswers.Enqueue(P("site-a.fgtcfg"));
        vm.SaveCsvCommand.Execute(null);
        Assert.Equal(before, await File.ReadAllBytesAsync(P("site-a.fgtcfg"), TestContext.Current.CancellationToken));
        Assert.NotEmpty(_dialogs.Errors);
    }

    [Fact]
    public void Import_ConvertsAndReportsErrors()
    {
        var vm = new ImportViewModel(_dialogs, _shell) { Kind = ImportKind.Service };
        vm.SetCsv(CsvImport.Template(ImportKind.Service));
        Assert.Empty(vm.Errors);
        Assert.Contains("config firewall service custom", vm.Script);
        Assert.StartsWith("# ", vm.Script);

        vm.SetCsv("name,protocol,ports\nx,tcp,99999\n");
        Assert.Single(vm.Errors);
        Assert.Equal("", vm.Script);
        Assert.False(vm.SaveCommand.CanExecute(null));
    }

    [Fact]
    public async Task Vpn_TablesAndChecks()
    {
        var vm = new VpnViewModel(_dialogs);
        await vm.Local.LoadAsync(P("site-a.fgtcfg"));
        Assert.Equal(2, vm.Rows.Count);
        await vm.Remote.LoadAsync(P("site-b.fgtcfg"));
        Assert.Contains(vm.Checks, c => c.Result == Shiftwall.Features.Toolbox.Vpn.VpnCheckResult.Mismatch);
        Assert.Contains("1", vm.Summary);
    }

    [Fact]
    public async Task Anonymize_MapFirst_ThenOutput_ThenRestore()
    {
        var vm = new AnonymizeViewModel(_dialogs);
        await vm.Config.LoadAsync(P("site-a.fgtcfg"));
        await vm.RunAsync();
        Assert.DoesNotContain("203.0.113.10", vm.Preview);
        Assert.False(vm.SaveOutputCommand.CanExecute(null));
        Assert.False(vm.SaveMapCommand.CanExecute(null));

        vm.SetPassword("short", "short");
        Assert.False(vm.PasswordOk);
        vm.SetPassword("a long enough password", "a long enough password");
        Assert.True(vm.SaveMapCommand.CanExecute(null));

        _dialogs.SaveAnswers.Enqueue(P("site-a.swmap"));
        vm.SaveMapCommand.Execute(null);
        Assert.True(vm.MapSaved);
        _dialogs.SaveAnswers.Enqueue(P("site-a.anon.conf"));
        vm.SaveOutputCommand.Execute(null);

        // Restore with the map and the password.
        await vm.RestoreSource.LoadAsync(P("site-a.anon.conf"));
        _dialogs.OpenAnswers.Enqueue(P("site-a.swmap"));
        vm.BrowseMapCommand.Execute(null);
        _dialogs.SaveAnswers.Enqueue(P("site-a.restored.conf"));
        vm.RestoreCommand.Execute(null);
        Assert.Equal(await File.ReadAllTextAsync(P("site-a.fgtcfg"), TestContext.Current.CancellationToken), await File.ReadAllTextAsync(P("site-a.restored.conf"), TestContext.Current.CancellationToken));

        // Wrong password.
        vm.SetPassword("another long password", "another long password");
        vm.RestoreCommand.Execute(null);
        Assert.Contains(Loc.T("Anonymize.MapFailed"), _dialogs.Errors);
    }

    [Fact]
    public void Snippets_GenerateAndValidate()
    {
        var vm = new SnippetsViewModel(_dialogs, _shell) { Kind = SnippetKind.StaticRoute, IpCidr = "10.20.0.0/16", Gateway = "192.168.1.254", Interface = "internal" };
        vm.Generate();
        Assert.Empty(vm.Errors);
        Assert.Contains("config router static", vm.Script);

        vm.Kind = SnippetKind.Vlan;
        Assert.Equal("", vm.Script);
        vm.Name = "vlan30";
        vm.Interface = "internal5";
        vm.VlanId = "5000";
        vm.IpCidr = "192.168.30.1/24";
        vm.Generate();
        Assert.Single(vm.Errors);
    }

    [Fact]
    public void Upgrade_ShowsNoPathAndOpensOfficialTool()
    {
        var db = ModelDatabase.LoadEmbedded();
        var vm = new UpgradeViewModel(db, _shell);
        Assert.Equal(Loc.T("Upgrade.NoPaths"), vm.PathStatus);
        vm.SelectedModel = db.FindModel("FG-60F");
        Assert.NotNull(vm.CurrentVersion);
        Assert.NotEmpty(vm.Newer);
        Assert.Contains(vm.Newer, r => r.SupportsModel);
        vm.OpenToolCommand.Execute(null);
        Assert.Equal("https://docs.fortinet.com/upgrade-tool/fortigate", Assert.Single(_shell.Urls));
    }

    private LanViewModel Lan(Func<LanBackupRequest, string?, CancellationToken, Task<LanBackupResult>> download, out AppSettings settings)
    {
        var store = new SettingsStore(P("settings.json"));
        settings = store.Load();
        return new LanViewModel(_dialogs, store, settings, download);
    }

    [Fact]
    public async Task Lan_IsDisabledByDefault_AndNeedsConfirmationToEnable()
    {
        var calls = 0;
        var vm = Lan((_, _, _) => { calls++; return Task.FromResult(new LanBackupResult(LanOutcome.NetworkError)); }, out var settings);
        Assert.False(vm.Enabled);
        vm.Host = "192.168.1.99";
        vm.SetSecrets("token", "");
        Assert.False(vm.DownloadCommand.CanExecute(null));
        await vm.DownloadAsync();
        Assert.Equal(0, calls);

        _dialogs.ConfirmAnswer = false;
        vm.EnableCommand.Execute(null);
        Assert.False(vm.Enabled);
        _dialogs.ConfirmAnswer = true;
        vm.EnableCommand.Execute(null);
        Assert.True(vm.Enabled);
        Assert.True(settings.LanFeatureEnabled);
        Assert.True(vm.DownloadCommand.CanExecute(null));
    }

    [Theory]
    [InlineData("8.8.8.8")]
    [InlineData("fortigate.example.com")]
    [InlineData("127.0.0.1")]
    public void Lan_RejectsNonPrivateHosts(string host)
    {
        var vm = Lan((_, _, _) => throw new InvalidOperationException("must not be called"), out var settings);
        settings.LanFeatureEnabled = true;
        vm.Host = host;
        vm.SetSecrets("token", "");
        Assert.NotNull(vm.HostError);
        Assert.False(vm.DownloadCommand.CanExecute(null));
    }

    [Fact]
    public async Task Lan_ConfirmsFingerprint_SavesBackup_AndClearsSecrets()
    {
        var requests = new List<(LanBackupRequest Request, string? Fingerprint)>();
        var config = Encoding.UTF8.GetBytes("#config-version=FGT60F-7.6.8-FW-build9999-261101:opmode=0:vdom=0:user=admin\nconfig system global\nend\n");
        var vm = Lan((r, fp, _) =>
        {
            requests.Add((r, fp));
            return Task.FromResult(fp is null
                ? new LanBackupResult(LanOutcome.CertificateNotConfirmed, CertificateSha256: "AA:BB", CertificateSubject: "CN=FGT60F")
                : new LanBackupResult(LanOutcome.Success, config, "AA:BB", "CN=FGT60F", 200, "POST"));
        }, out var settings);
        settings.LanFeatureEnabled = true;
        vm.Host = "192.168.1.99";
        vm.SetSecrets("secret-token", "");
        var cleared = 0;
        vm.SecretsCleared += (_, _) => cleared++;
        _dialogs.SaveAnswers.Enqueue(P("downloaded.conf"));

        await vm.DownloadAsync();

        Assert.Equal(2, requests.Count);
        Assert.Equal("secret-token", requests[0].Request.Token);
        Assert.Equal(IPAddress.Parse("192.168.1.99"), requests[0].Request.Address);
        Assert.Contains("AA:BB", Assert.Single(_dialogs.Confirmations));
        Assert.Equal("AA:BB", settings.LanConfirmedCertificates["192.168.1.99:443"]);
        Assert.Equal(config, await File.ReadAllBytesAsync(P("downloaded.conf"), TestContext.Current.CancellationToken));
        Assert.Equal(1, cleared);
        Assert.False(vm.DownloadCommand.CanExecute(null)); // token cleared

        var stored = await File.ReadAllTextAsync(P("settings.json"), TestContext.Current.CancellationToken);
        Assert.DoesNotContain("secret-token", stored);
        Assert.Contains("AA:BB", stored);
    }

    [Fact]
    public async Task Lan_801Mode_NeedsBackupPassword()
    {
        LanBackupRequest? seen = null;
        var vm = Lan((r, _, _) => { seen = r; return Task.FromResult(new LanBackupResult(LanOutcome.Unauthorized, HttpStatus: 401)); }, out var settings);
        settings.LanFeatureEnabled = true;
        vm.Host = "10.0.0.1";
        vm.FortiOs801 = true;
        vm.SetSecrets("t", "short");
        Assert.False(vm.DownloadCommand.CanExecute(null));
        vm.SetSecrets("t", "long enough");
        await vm.DownloadAsync();
        Assert.Equal(BackupProtection.SecretsWithPassword, seen!.Protection);
        Assert.Equal("long enough", seen.BackupPassword);
        Assert.Contains("401", vm.Status);
    }
}
