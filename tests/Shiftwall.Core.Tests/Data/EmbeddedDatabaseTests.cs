using Shiftwall.Core.Data;

namespace Shiftwall.Core.Tests.Data;

/// <summary>Guards the shipped database: it must load, validate and keep its invariants.</summary>
public sealed class EmbeddedDatabaseTests
{
    private static readonly ModelDatabase Db = ModelDatabase.LoadEmbedded();

    [Fact]
    public void EmbeddedDatabase_LoadsAndValidates()
    {
        Assert.NotEmpty(Db.Models);
        Assert.NotEmpty(Db.Releases);
    }

    [Fact]
    public void Releases_AreOrderedAndUnique()
    {
        var versions = Db.Releases.Select(r => FortiOsVersion.Parse(r.Version)).ToList();
        Assert.Equal(versions.Order(), versions);
        Assert.Equal(versions.Count, versions.Distinct().Count());
    }

    [Fact]
    public void UpgradePaths_AreNotListedWithoutOfficialConsultableSource()
    {
        if (Db.UpgradePaths.PublishedInConsultableForm.Value != true)
        {
            Assert.Empty(Db.UpgradePaths.Paths);
        }

        Assert.StartsWith("https://docs.fortinet.com/", Db.UpgradePaths.OfficialToolUrl, StringComparison.Ordinal);
    }

    [Fact]
    public void EveryHardwareModel_IsListedByAtLeastOneRelease()
    {
        var orphans = Db.Models
            .Where(m => m.Platform == "hardware" && !Db.ReleasesSupporting(m.Id).Any())
            .Select(m => m.Id)
            .ToList();
        Assert.Empty(orphans);
    }

    [Fact]
    public void FindModel_IsCaseInsensitive() =>
        Assert.Same(Db.Models[0], Db.FindModel(Db.Models[0].Id.ToLowerInvariant()));
}
