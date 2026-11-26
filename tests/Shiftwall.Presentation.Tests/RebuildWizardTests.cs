using System.Globalization;
using Shiftwall.Core.Data;
using Shiftwall.Features.Rebuild;
using Shiftwall.Presentation.Localization;
using Shiftwall.Presentation.ViewModels;

namespace Shiftwall.Presentation.Tests;

public sealed class RebuildWizardTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("shiftwall-rebuild-").FullName;
    private readonly FakeDialogs _dialogs = new();
    private readonly MigrationWizardViewModel _vm;

    public RebuildWizardTests()
    {
        Loc.Instance.Culture = CultureInfo.GetCultureInfo("it-IT");
        foreach (var f in Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "Rebuild"))) File.Copy(f, Path.Combine(_dir, Path.GetFileName(f)));
        _vm = new MigrationWizardViewModel(ModelDatabase.LoadEmbedded(), _dialogs, new FakePrinter(), new FakeShell());
    }

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private string P(string f) => Path.Combine(_dir, f);

    private async Task LoadAsync()
    {
        await _vm.LoadSourceAsync(P("110c-5.2-office.fgtcfg"));
        await _vm.Target.LoadBackupAsync(P("40f-8.0-target-synthetic.fgtcfg"));
        await _vm.ReanalyzeAsync();
    }

    [Fact]
    public async Task DifferentVersions_ChooseTheRebuild_AndExplainWhy()
    {
        await LoadAsync();
        Assert.True(_vm.IsRebuild);
        Assert.Contains(_vm.ModeMessages, m => m.Code == RebuildCodes.ModeRebuild);

        _vm.Mode = MigrationMode.Restore;
        Assert.False(_vm.IsRebuild);
        Assert.Contains(_vm.ModeMessages, m => m.Code == RebuildCodes.ModeForced);
        _vm.Mode = MigrationMode.Auto;
        Assert.True(_vm.IsRebuild);
    }

    [Fact]
    public async Task Mapping_OffersTheLanSwitch_AndSuggests110CTo40F()
    {
        await LoadAsync();
        Assert.Contains("lan", _vm.TargetChoices);
        Assert.Equal("lan", _vm.SourcePorts.Single(p => p.Name == "internal").Target);
        Assert.Equal("wan", _vm.SourcePorts.Single(p => p.Name == "wan1").Target);
        Assert.Null(_vm.SourcePorts.Single(p => p.Name == "wan2").Target); // to choose: never taken from the LAN switch silently
        Assert.Equal("lan", _vm.ManagementInterface);
    }

    [Fact]
    public async Task SourceSwitch_IsAnItemOfItsOwn_AndCanBeRecreatedWithChosenPorts()
    {
        await LoadAsync();
        var internalRow = _vm.Row("internal")!;
        Assert.True(internalRow.IsSwitch);
        Assert.False(_vm.Row("wan1")!.IsSwitch);
        Assert.Equal(["a", "lan1", "lan2", "lan3", "wan"], internalRow.PortChoices.Select(p => p.Port));
        Assert.Contains("nello switch lan", internalRow.PortChoices.Single(p => p.Port == "lan1").Status);
        Assert.Contains(_vm.TargetPorts, t => t.Name == "lan" && t.Status.Contains("lan1", StringComparison.Ordinal));

        internalRow.Action = SwitchAction.Recreate;
        Assert.Equal("internal", internalRow.Target);
        Assert.True(internalRow.ShowsPorts);
        internalRow.PortChoices.Single(p => p.Port == "lan1").IsChecked = true;
        internalRow.PortChoices.Single(p => p.Port == "lan2").IsChecked = true;
        _vm.Row("wan2")!.Target = "lan";
        _vm.ManagementInterface = "internal";
        await _vm.ReanalyzeAsync();

        Assert.Contains("→ internal", internalRow.PortChoices.Single(p => p.Port == "lan1").Status);
        Assert.Contains(_vm.MappingFindings, m => m.Code == RebuildCodes.HardwareSwitchChosen);
        Assert.DoesNotContain(_vm.MappingFindings, m => m.Severity == ItemSeverity.Error);

        await _vm.RunRebuildAsync();
        Assert.False(_vm.RebuildResult!.IsBlocked);
        Assert.Contains("edit \"internal\"\n        set physical-switch \"sw0\"", _vm.RebuildResult.FullScript());
    }

    [Fact]
    public async Task SourceSwitch_Split_TakesTheFirstPort_AndBadChoicesShowUpBeforeRunning()
    {
        await LoadAsync();
        var internalRow = _vm.Row("internal")!;
        internalRow.Action = SwitchAction.Split;
        internalRow.PortChoices.Single(p => p.Port == "lan1").IsChecked = true;
        internalRow.PortChoices.Single(p => p.Port == "lan2").IsChecked = true;
        Assert.Equal("lan1", internalRow.Target);
        Assert.False(internalRow.CanEditTarget);

        _vm.Row("wan2")!.Target = "lan3"; // the switch "lan" would be left without ports
        await _vm.ReanalyzeAsync();
        Assert.Contains(_vm.MappingFindings, m => m.Code == RebuildCodes.SwitchWouldBeEmpty);

        _vm.Row("wan2")!.Target = "lan";
        await _vm.ReanalyzeAsync();
        Assert.DoesNotContain(_vm.MappingFindings, m => m.Code == RebuildCodes.SwitchWouldBeEmpty);
        Assert.Contains(_vm.MappingFindings, m => m.Code == RebuildCodes.SplitPortNoAddress);
    }

    [Fact]
    public async Task TypedName_OffersToCreateIt_AsAVlan()
    {
        await LoadAsync();
        var row = _vm.Row("wan2")!;
        row.Target = "guest";
        await _vm.ReanalyzeAsync();
        Assert.True(row.NeedsCreate);
        Assert.True(row.ShowsAnyKind);

        row.CreateKind = NewInterfaceKind.Vlan;
        row.VlanParent = "lan";
        row.VlanId = "30";
        await _vm.ReanalyzeAsync();
        Assert.DoesNotContain(_vm.MappingFindings, m => m.Severity == ItemSeverity.Error);
        await _vm.RunRebuildAsync();
        Assert.Contains("edit \"guest\"\n        set vdom \"root\"\n        set interface \"lan\"\n        set vlanid 30", _vm.RebuildResult!.FullScript());
    }

    [Fact]
    public async Task FullFlow_SavesGuiAndTerminalScripts_AndTheEncryptedPasswords()
    {
        await LoadAsync();
        _vm.SourcePorts.Single(p => p.Name == "wan2").Target = "lan3";
        _vm.Step = Shiftwall.Presentation.ViewModels.WizardStep.Mapping;
        await _vm.NextCommand.ExecuteAsync(null);
        Assert.Equal(Shiftwall.Presentation.ViewModels.WizardStep.Transform, _vm.Step);
        Assert.NotNull(_vm.RebuildResult);
        Assert.True(_vm.CanGoNext);
        Assert.Contains("********", _vm.ScriptPreview);
        Assert.NotNull(_vm.PasswordSummary);
        Assert.NotEmpty(_vm.Counts);
        Assert.NotEmpty(_vm.ManualRows);

        _dialogs.OpenAnswers.Enqueue(_dir);
        _vm.SaveGuiCommand.Execute(null);
        _dialogs.OpenAnswers.Enqueue(_dir);
        _vm.SaveTerminalCommand.Execute(null);
        Assert.Empty(_dialogs.Errors);
        var gui = Directory.GetFiles(Directory.GetDirectories(_dir, "*-gui").Single());
        var cli = Directory.GetFiles(Directory.GetDirectories(_dir, "*-terminal").Single());
        Assert.NotEmpty(gui);
        Assert.True(cli.Length >= gui.Length);
        foreach (var f in gui.Concat(cli))
        {
            var bytes = File.ReadAllBytes(f);
            Assert.False(bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF, "UTF-8 without BOM");
            Assert.DoesNotContain((byte)'\r', bytes);
        }

        // The passwords: only in the encrypted file (and in the scripts that set them).
        _vm.SetVaultPassword("corta");
        _vm.SaveSecretsCommand.Execute(null);
        Assert.Single(_dialogs.Errors);
        _vm.SetVaultPassword("una-password-lunga-per-il-file");
        _dialogs.SaveAnswers.Enqueue(P("passwords.swsecret"));
        _vm.SaveSecretsCommand.Execute(null);
        var entries = RebuildVault.Decrypt(File.ReadAllBytes(P("passwords.swsecret")), "una-password-lunga-per-il-file");
        Assert.Equal(_vm.RebuildResult!.Secrets.Count, entries.Count);

        _dialogs.SaveAnswers.Enqueue(P("report.html"));
        _vm.ExportRebuildHtmlCommand.Execute(null);
        var html = File.ReadAllText(P("report.html"));
        Assert.Contains("Report di ricostruzione", html, StringComparison.Ordinal);
        Assert.All(_vm.RebuildResult.Secrets, s => Assert.DoesNotContain(s.Password, html));

        _vm.HidePasswordsCommand.Execute(null);
        Assert.Null(_vm.PasswordSummary);
    }

    [Fact]
    public async Task InvalidTypedPassword_BlocksTheRun()
    {
        await LoadAsync();
        var admin = _vm.Secrets.Single(s => s.Category == SecretCategory.Admin);
        admin.SetPassword("Corta?1");
        admin.SetConfirm("Corta?1");
        Assert.False(admin.IsValid);
        _vm.Step = Shiftwall.Presentation.ViewModels.WizardStep.Mapping;
        Assert.False(_vm.CanGoNext);
        admin.SetPassword("Valida-Lunga-2026!");
        admin.SetConfirm("Valida-Lunga-2026!");
        Assert.True(admin.IsValid);
        Assert.True(_vm.CanGoNext);
    }
}
