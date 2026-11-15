using Shiftwall.Core.Data;

namespace Shiftwall.Core.Tests.Data;

public sealed class DatabaseValidatorTests
{
    private static readonly DateOnly Today = new(2026, 11, 15);

    private static SourceRef Src(string url = "https://docs.fortinet.com/document/fortigate/7.6.8/hardware-acceleration/1/x") =>
        new(url, "Doc", new DateOnly(2026, 11, 15));

    private static FortiGateModel Model(
        Sourced<IReadOnlyList<InterfaceInfo>>? interfaces = null,
        Sourced<bool?>? disk = null,
        Sourced<IReadOnlyList<HardwareSwitchInfo>>? switches = null,
        string id = "FG-60F") =>
        new(id, "FortiGate 60F", "FortiGate", "hardware",
            interfaces ?? new([new InterfaceInfo("wan1", "rj45"), new InterfaceInfo("internal1", "rj45")], VerificationStatus.Verified, [Src()]),
            disk ?? new(false, VerificationStatus.Verified, [Src()]),
            switches ?? Sourced<IReadOnlyList<HardwareSwitchInfo>>.Unverified("Default config not documented."));

    private static IReadOnlyList<string> Validate(params FortiGateModel[] models) =>
        DatabaseValidator.Validate(
            new ModelsDocument(1, models),
            new ReleasesDocument(1, []),
            new UpgradePathsDocument(1, new UpgradePathInfo(
                "https://docs.fortinet.com/upgrade-tool/fortigate", [],
                new Sourced<bool?>(false, VerificationStatus.Verified, [Src()]))),
            Today);

    [Fact]
    public void ValidModel_HasNoErrors() => Assert.Empty(Validate(Model()));

    [Fact]
    public void UnverifiedDatum_MustNotCarryAValue()
    {
        var errors = Validate(Model(disk: new Sourced<bool?>(true, VerificationStatus.Unverified, [], "guess")));
        Assert.Contains(errors, e => e.Contains("must not carry a value", StringComparison.Ordinal));
    }

    [Fact]
    public void UnverifiedDatum_RequiresNote()
    {
        var errors = Validate(Model(disk: new Sourced<bool?>(null, VerificationStatus.Unverified, [])));
        Assert.Contains(errors, e => e.Contains("explanatory note", StringComparison.Ordinal));
    }

    [Fact]
    public void VerifiedDatum_RequiresSource()
    {
        var errors = Validate(Model(disk: new Sourced<bool?>(true, VerificationStatus.Verified, [])));
        Assert.Contains(errors, e => e.Contains("no source", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("https://example.com/fortigate-60f")]
    [InlineData("http://docs.fortinet.com/document/x")]
    [InlineData("https://fortinetweb.s3.amazonaws.com/other-bucket/file.pdf")]
    public void NonOfficialSources_AreRejected(string url)
    {
        var errors = Validate(Model(disk: new Sourced<bool?>(true, VerificationStatus.Verified, [Src(url)])));
        Assert.NotEmpty(errors);
    }

    [Fact]
    public void SwitchMembers_MustBeListedInterfaces()
    {
        var sw = new Sourced<IReadOnlyList<HardwareSwitchInfo>>(
            [new HardwareSwitchInfo("internal", ["internal1", "internal9"])], VerificationStatus.Verified, [Src()]);
        var errors = Validate(Model(switches: sw));
        Assert.Contains(errors, e => e.Contains("internal9", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("Port1")]
    [InlineData("this-name-is-too-long")]
    [InlineData("")]
    public void InvalidInterfaceNames_AreRejected(string name)
    {
        var ifs = new Sourced<IReadOnlyList<InterfaceInfo>>([new InterfaceInfo(name, "rj45")], VerificationStatus.Verified, [Src()]);
        Assert.NotEmpty(Validate(Model(interfaces: ifs)));
    }

    [Fact]
    public void DuplicateModelIds_AreRejected() =>
        Assert.Contains(Validate(Model(), Model()), e => e.Contains("duplicate model id", StringComparison.Ordinal));

    [Fact]
    public void UpgradePaths_WithoutConsultableSource_AreRejected()
    {
        var errors = DatabaseValidator.Validate(
            new ModelsDocument(1, []),
            new ReleasesDocument(1, []),
            new UpgradePathsDocument(1, new UpgradePathInfo(
                "https://docs.fortinet.com/upgrade-tool/fortigate",
                [new UpgradePath("FG-60F", "7.0.12", "7.4.13", ["7.2.8"], [Src()])],
                new Sourced<bool?>(false, VerificationStatus.Verified, [Src()]))),
            Today);
        Assert.Contains(errors, e => e.Contains("no official consultable source", StringComparison.Ordinal));
    }
}
