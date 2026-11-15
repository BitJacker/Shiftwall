using Shiftwall.Features.Migration;

namespace Shiftwall.Features.Tests.Migration;

public sealed class MigrationGoldenTests
{
    [Fact]
    public void Branch_ProducesTheReviewedOutput()
    {
        var source = Fixtures.BranchSource();
        var result = MigrationEngine.Run(new MigrationRequest(source, Fixtures.BranchTarget(), Fixtures.BranchMapping(source)));

        Assert.False(result.IsBlocked);
        Assert.True(result.IsValid);
        Assert.Equal(Fixtures.Text("branch-expected.fgtcfg"), result.OutputText);
    }

    /// <summary>
    /// Multi-VDOM source: renames inside config global and inside each VDOM (policies with several interfaces,
    /// static routes, VLAN parent, HA heartbeat device). The expected file was reviewed by hand.
    /// </summary>
    [Fact]
    public void MultiVdom_ProducesTheReviewedOutput()
    {
        var source = Shiftwall.Features.Migration.SourceProfile.From(Shiftwall.Core.Config.ConfigInspector.Inspect(Fixtures.Bytes("multivdom-source.fgtcfg")));
        var target = TargetProfile.FromDefaultBackup(Shiftwall.Core.Config.ConfigInspector.Inspect(Fixtures.Bytes("multivdom-target.fgtcfg")));
        var mapping = InterfaceMapping.FromTemplate(MappingTemplate.FromJson(Fixtures.Text("multivdom-mapping.json")), source.PhysicalInterfaces.Select(p => p.Name));
        var result = MigrationEngine.Run(new MigrationRequest(source, target, mapping));

        Assert.False(result.IsBlocked);
        Assert.True(result.IsValid, string.Join("; ", result.Validation.Select(v => v.Format())));
        var expectedPath = Path.Combine(Fixtures.Dir, "multivdom-expected.fgtcfg");
        if (!File.Exists(expectedPath))
        {
            File.WriteAllText(Path.Combine(Fixtures.Dir, "multivdom-actual.fgtcfg"), result.OutputText);
            Assert.Fail("Expected file missing: review multivdom-actual.fgtcfg in the test output folder.");
        }

        Assert.Equal(File.ReadAllText(expectedPath), result.OutputText);
        Assert.Contains(result.Analysis.PreChecks, f => f.Code == MigrationCodes.MultiVdom);
    }

    [Fact]
    public void Branch_ReportsChangesWarningsAndChecklist()
    {
        var source = Fixtures.BranchSource();
        var result = MigrationEngine.Run(new MigrationRequest(source, Fixtures.BranchTarget(), Fixtures.BranchMapping(source)));

        Assert.Contains(result.Changes, c => c.Code == MigrationCodes.Renamed && c.Arguments.SequenceEqual(["internal5", "port6", "2"]));
        Assert.Contains(result.Changes, c => c.Code == MigrationCodes.DefinitionRemoved && c.Arguments[0] == "dmz");
        Assert.Contains(result.Changes, c => c.Code == MigrationCodes.SectionRemoved && c.Arguments[0] == "system storage");
        Assert.Contains(result.Changes, c => c.Code == MigrationCodes.SectionRemoved && c.Arguments[0] == "log disk setting");
        Assert.Contains(result.Changes, c => c.Code == MigrationCodes.HeaderReplaced);
        var possible = Assert.Single(result.Analysis.MappingFindings, f => f.Code == MigrationCodes.PossibleReference);
        Assert.Equal(["internal5", "system snmp sysinfo trap-source-intf"], possible.Arguments);
        Assert.Contains(result.Checklist, c => c.Code == MigrationCodes.CheckLab);
        Assert.Contains(result.Checklist, c => c.Code == MigrationCodes.CheckPossibleReferences);
        Assert.DoesNotContain(result.Checklist, c => c.Code == MigrationCodes.CheckFirmware);
    }

    [Fact]
    public void Simulation_IsTheSameRun_AndWritesNothing()
    {
        var before = Directory.GetFiles(Fixtures.Dir).Length;
        var source = Fixtures.BranchSource();
        _ = MigrationEngine.Run(new MigrationRequest(source, Fixtures.BranchTarget(), Fixtures.BranchMapping(source)));
        Assert.Equal(before, Directory.GetFiles(Fixtures.Dir).Length);
    }
}
