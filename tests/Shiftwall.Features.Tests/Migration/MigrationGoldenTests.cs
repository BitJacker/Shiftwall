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
