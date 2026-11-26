using System.Globalization;
using System.Net;
using Shiftwall.Features.Toolbox.TwoFactor;
using Shiftwall.Lan;
using Shiftwall.Presentation.Localization;
using Shiftwall.Presentation.ViewModels.Toolbox;

namespace Shiftwall.Presentation.Tests;

public sealed class TwoFactorViewModelTests : IDisposable
{
    private const string Enc = "ENC U2hpZnR3YWxsIHN5bnRoZXRpYyB0ZXN0IHZhbHVlIC0gbm90IGEgc2VjcmV0";

    private static readonly string Config = $"""
        #config-version=FGTSYN-7.4.9-FW-build2829-250101:opmode=0:vdom=0:user=admin
        config system email-server
            set server "smtp.example.com"
            set port 465
            set security smtps
        end
        config system admin
            edit "admin"
                set accprofile "super_admin"
                set password {Enc}
            next
            edit "breakglass"
                set accprofile "super_admin"
                set password {Enc}
            next
        end
        config user local
            edit "mario.rossi"
                set passwd {Enc}
            next
            edit "anna.bianchi"
                set passwd {Enc}
                set two-factor fortitoken
                set fortitoken "FTKMOB0000000009"
            next
        end

        """;

    private readonly string _dir = Directory.CreateTempSubdirectory("shiftwall-2fa-").FullName;
    private readonly FakeDialogs _dialogs = new();
    private readonly FakeShell _shell = new();

    public TwoFactorViewModelTests() => Loc.Instance.Culture = CultureInfo.GetCultureInfo("it-IT");

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private async Task<TwoFactorViewModel> LoadedAsync(string config = "")
    {
        var path = Path.Combine(_dir, "lab.conf");
        await File.WriteAllTextAsync(path, config.Length > 0 ? config : Config, TestContext.Current.CancellationToken);
        var vm = new TwoFactorViewModel(_dialogs, _shell);
        await vm.Config.LoadAsync(path);
        return vm;
    }

    [Fact]
    public async Task Backup_FillsTheTable_AndFiltersWork()
    {
        var vm = await LoadedAsync();
        Assert.Equal(4, vm.Accounts.Count);
        vm.Filter = AccountFilter.WithFortiToken;
        Assert.Equal("anna.bianchi", Assert.Single(vm.Visible).Entry.Name);
        vm.Filter = AccountFilter.AdminsOnly;
        Assert.Equal(2, vm.Visible.Count());
        vm.Filter = AccountFilter.All;
        vm.Search = "mario";
        Assert.Single(vm.Visible);
        Assert.False(vm.EmailServerMissing);
    }

    [Fact]
    public async Task SelectedRows_BecomeRequests_AndTheDryRunShowsBeforeAndAfter()
    {
        var vm = await LoadedAsync();
        vm.DefaultDomain = "example.com";
        vm.Accounts.Single(a => a.Entry.Name == "mario.rossi").IsSelected = true;
        vm.EditEmail = "mario.rossi";
        vm.EditAction = ChangeAction.Set;
        vm.AddSelected();

        var row = Assert.Single(vm.DryRun);
        Assert.Equal(("", "mario.rossi@example.com", "disable", "email"), (row.EmailBefore, row.EmailAfter, row.TwoFactorBefore, row.TwoFactorAfter));
        Assert.True(vm.CanGenerate);
    }

    [Fact]
    public async Task FortiTokenAndAdmins_NeedConfirmations_ThenScriptsAreWritten()
    {
        var vm = await LoadedAsync();
        vm.SetRequests([
            new ChangeRequest(2, "anna.bianchi", "anna.bianchi@example.com", ChangeAction.Set),
            new ChangeRequest(3, "admin", "it@example.com", ChangeAction.Set, Kind: AccountKind.Admin),
        ]);
        Assert.False(vm.CanGenerate);
        Assert.True(vm.HasTokenConfirmations);
        Assert.True(vm.TouchesAdmins);

        vm.TokenConfirmations.Single().Confirmed = true;
        vm.AdminConfirm1 = true;
        Assert.False(vm.CanGenerate);
        vm.AdminConfirm2 = true;
        Assert.True(vm.CanGenerate);

        var output = Directory.CreateDirectory(Path.Combine(_dir, "out")).FullName;
        _dialogs.OpenAnswers.Enqueue(output);
        vm.GenerateCommand.Execute(null);

        Assert.Contains(_dialogs.Confirmations, c => c.Contains("amministrator", StringComparison.Ordinal));
        var files = Directory.GetFiles(output).Select(Path.GetFileName).Order().ToList();
        Assert.Equal(["2fa-admins-001.txt", "2fa-report.csv", "2fa-report.html", "2fa-users-001.txt", "rollback-admins-001.txt", "rollback-users-001.txt"], files);
        Assert.Contains("set two-factor email", await File.ReadAllTextAsync(Path.Combine(output, "2fa-users-001.txt"), TestContext.Current.CancellationToken), StringComparison.Ordinal);
    }

    [Fact]
    public async Task NewPasswordUsers_NeedTheVaultPassword_AndNoPasswordIsWrittenInClear()
    {
        var vm = await LoadedAsync();
        vm.CreateMissingUsers = true;
        vm.SetRequests([new ChangeRequest(2, "nuovo.utente", "nuovo.utente@example.com", ChangeAction.Set, "password")]);
        Assert.True(vm.NeedsVaultPassword);
        Assert.False(vm.CanGenerate);
        vm.SetVaultPassword("una password lunga", "una password lunga");
        Assert.True(vm.CanGenerate);

        var output = Directory.CreateDirectory(Path.Combine(_dir, "out")).FullName;
        _dialogs.OpenAnswers.Enqueue(output);
        vm.GenerateCommand.Execute(null);

        var vault = Path.Combine(output, "2fa-new-users" + PasswordVault.Extension);
        Assert.True(File.Exists(vault));
        var password = PasswordVault.Decrypt(await File.ReadAllBytesAsync(vault, TestContext.Current.CancellationToken), "una password lunga").Single().Password;
        foreach (var f in Directory.GetFiles(output).Where(f => f != vault))
        {
            Assert.DoesNotContain(password, await File.ReadAllTextAsync(f, TestContext.Current.CancellationToken), StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task MissingEmailServer_BlocksAndShowsTheExample()
    {
        var vm = await LoadedAsync(Config.Replace("config system email-server", "config system ntp", StringComparison.Ordinal));
        vm.SetRequests([new ChangeRequest(2, "mario.rossi", "mario.rossi@example.com", ChangeAction.Set)]);
        Assert.True(vm.EmailServerMissing);
        Assert.False(vm.CanGenerate);
        Assert.Contains(vm.Findings, f => f.Code == "EmailServerMissing");
        vm.CopySnippetCommand.Execute(null);
        Assert.StartsWith("config system email-server\n", Assert.Single(_shell.Copied), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ListReadThroughTheLan_CannotGenerateScripts()
    {
        var settings = new Shiftwall.Presentation.Services.AppSettings { LanFeatureEnabled = true };
        var vm = new TwoFactorViewModel(_dialogs, _shell, null, settings, (_, _, _) => Task.FromResult(new LanAccountsResult(LanOutcome.Success,
            [new AccountEntry(AccountKind.LocalUser, null, "mario.rossi", "password", null, null, null, null, null, 0)], AdminsEmpty: true)));
        vm.LanHost = "192.168.1.99";
        vm.SetToken("token");
        await vm.ReadLanAsync();

        Assert.True(vm.FromLan);
        Assert.Single(vm.Accounts);
        Assert.Contains("super_admin", vm.Status, StringComparison.Ordinal);
        vm.SetRequests([new ChangeRequest(2, "mario.rossi", "mario.rossi@example.com", ChangeAction.Set)]);
        Assert.False(vm.CanGenerate);
        Assert.Contains(vm.Findings, f => f.Code == "FromLan");
        Assert.True(IPAddress.TryParse(vm.LanHost, out _));
    }
}
