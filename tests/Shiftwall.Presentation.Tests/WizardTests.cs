using System.Globalization;
using Shiftwall.Core.Data;
using Shiftwall.Presentation.Localization;
using Shiftwall.Presentation.ViewModels;

namespace Shiftwall.Presentation.Tests;

public sealed class WizardTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("shiftwall-ui-").FullName;
    private readonly FakeDialogs _dialogs = new();
    private readonly FakePrinter _printer = new();
    private readonly MigrationWizardViewModel _vm;

    public WizardTests()
    {
        Loc.Instance.Culture = CultureInfo.GetCultureInfo("it-IT");
        foreach (var f in Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "Golden"))) File.Copy(f, Path.Combine(_dir, Path.GetFileName(f)));
        _vm = new MigrationWizardViewModel(ModelDatabase.LoadEmbedded(), _dialogs, _printer, new FakeShell());
    }

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private string P(string f) => Path.Combine(_dir, f);

    private async Task LoadBranchAsync()
    {
        await _vm.LoadSourceAsync(P("branch-source.fgtcfg"));
        await _vm.Target.LoadBackupAsync(P("target-default.fgtcfg"));
        _vm.Target.SelectedModel = ModelDatabase.LoadEmbedded().FindModel("FG-70G");
        await _vm.ReanalyzeAsync();
    }

    [Fact]
    public async Task Step1_RequiresSourceAndTarget()
    {
        Assert.False(_vm.CanGoNext);
        await _vm.LoadSourceAsync(P("branch-source.fgtcfg"));
        Assert.True(_vm.IsSourceReady);
        Assert.False(_vm.CanGoNext);
        await _vm.Target.LoadBackupAsync(P("target-default.fgtcfg"));
        Assert.True(_vm.CanGoNext);
        Assert.Contains("FGT60F", _vm.SourceSummary, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Mapping_AutoMatchesNames_AndBlocksUntilUsedPortsAreMapped()
    {
        await LoadBranchAsync();
        Assert.Equal(["wan1", "wan2", "dmz", "internal1", "internal2", "internal3", "internal4", "internal5", "a", "b"], _vm.SourcePorts.Select(p => p.Name));
        Assert.Equal("wan1", _vm.SourcePorts.Single(p => p.Name == "wan1").Target);
        Assert.Null(_vm.SourcePorts.Single(p => p.Name == "internal1").Target);
        Assert.True(_vm.HasBlockingMapping);
        Assert.True(_vm.SourcePorts.Single(p => p.Name == "internal5").IsBlocking);

        foreach (var (s, t) in new[] { ("internal1", "port1"), ("internal2", "port2"), ("internal3", "port3"), ("internal4", "port4"), ("internal5", "port6") })
        {
            _vm.Map(s, t);
        }

        await _vm.ReanalyzeAsync();
        Assert.False(_vm.HasBlockingMapping);
        Assert.Equal("internal5", _vm.TargetPorts.Single(t => t.Name == "port6").AssignedFrom);
    }

    [Fact]
    public async Task MappingTheSameTargetTwice_FreesTheFirstRow()
    {
        await LoadBranchAsync();
        _vm.Map("internal1", "port1");
        _vm.Map("internal2", "port1");
        Assert.Null(_vm.SourcePorts.Single(p => p.Name == "internal1").Target);
        Assert.Equal("port1", _vm.SourcePorts.Single(p => p.Name == "internal2").Target);
    }

    [Fact]
    public async Task FullFlow_ProducesValidResult_AndSavesWithoutTouchingTheOriginal()
    {
        await LoadBranchAsync();
        _dialogs.OpenAnswers.Enqueue(P("branch-mapping.json"));
        _vm.LoadTemplateCommand.Execute(null);
        await _vm.ReanalyzeAsync();
        Assert.False(_vm.HasBlockingMapping);

        _vm.Step = WizardStep.Mapping;
        await _vm.NextCommand.ExecuteAsync(null);
        Assert.Equal(WizardStep.Transform, _vm.Step);
        Assert.True(_vm.Result!.IsValid);
        Assert.NotEmpty(_vm.DiffRows);
        Assert.NotEmpty(_vm.Checklist);

        // Saving over the original is refused.
        var originalBytes = File.ReadAllBytes(P("branch-source.fgtcfg"));
        _dialogs.SaveAnswers.Enqueue(P("branch-source.fgtcfg"));
        _vm.SaveConfigCommand.Execute(null);
        Assert.Single(_dialogs.Errors);
        Assert.Equal(originalBytes, File.ReadAllBytes(P("branch-source.fgtcfg")));

        // Saving to a new file works and matches the reviewed golden output.
        _dialogs.SaveAnswers.Enqueue(P("out.conf"));
        _vm.SaveConfigCommand.Execute(null);
        Assert.Equal(File.ReadAllText(P("branch-expected.fgtcfg")), File.ReadAllText(P("out.conf")));
        Assert.Equal(P("out.conf"), _vm.SavedPath);

        _dialogs.SaveAnswers.Enqueue(P("out.report.html"));
        _vm.ExportHtmlCommand.Execute(null);
        Assert.Contains("Report di migrazione", File.ReadAllText(P("out.report.html")), StringComparison.Ordinal);

        _vm.PrintPdfCommand.Execute(null);
        Assert.Equal(1, _printer.Printed);
    }

    [Fact]
    public async Task TemplateSaveAndLoad_RoundTrips()
    {
        await LoadBranchAsync();
        _vm.Map("internal1", "port3");
        _dialogs.SaveAnswers.Enqueue(P("t.swmap.json"));
        _vm.SaveTemplateCommand.Execute(null);
        _vm.ClearMappingCommand.Execute(null);
        Assert.All(_vm.SourcePorts, p => Assert.Null(p.Target));
        _dialogs.OpenAnswers.Enqueue(P("t.swmap.json"));
        _vm.LoadTemplateCommand.Execute(null);
        Assert.Equal("port3", _vm.SourcePorts.Single(p => p.Name == "internal1").Target);
    }

    [Fact]
    public async Task EncryptedSource_IsNotReady_AndExplainsWhy()
    {
        File.WriteAllBytes(P("enc.conf"), [0, 1, 2, 3, 0, 9, 8]);
        await _vm.LoadSourceAsync(P("enc.conf"));
        Assert.False(_vm.IsSourceReady);
        Assert.Contains(_vm.SourceDiagnostics, d => d.Code == "SW-D001" && d.Text.Contains("senza cifratura", StringComparison.Ordinal));
    }

    [Fact]
    public void DatabaseTarget_OffersReleasesOfTheModel_AndWarnsOnUnverifiedData()
    {
        var t = _vm.Target;
        t.Mode = Features.Migration.TargetSource.Database;
        t.SelectedModel = ModelDatabase.LoadEmbedded().FindModel("FG-60E");
        Assert.Contains("7.4.13", t.Versions);
        Assert.Equal("7.4.13", t.SelectedVersion); // newest first
        Assert.NotEmpty(t.ModelWarnings);
        Assert.True(t.IsReady);
    }

    [Fact]
    public async Task LanguageSwitch_RetranslatesExistingMessages()
    {
        await _vm.LoadSourceAsync(P("branch-source.fgtcfg"));
        var item = _vm.SourceDiagnostics.First();
        var italian = item.Text;
        Loc.Instance.Culture = CultureInfo.GetCultureInfo("en");
        Assert.NotEqual(italian, item.Text);
        Loc.Instance.Culture = CultureInfo.GetCultureInfo("it-IT");
    }
}
