using Shiftwall.Core.Data;
using Shiftwall.UpdateDb;

namespace Shiftwall.UpdateDb.Tests;

public sealed class DefaultBackupImportTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("shiftwall-db-").FullName;

    public DefaultBackupImportTests()
    {
        var repoData = Path.Combine(FindRepoRoot(), "data");
        foreach (var f in Directory.GetFiles(repoData, "*.json")) File.Copy(f, Path.Combine(_dir, Path.GetFileName(f)));
    }

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private static string FindRepoRoot()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d is not null && !File.Exists(Path.Combine(d.FullName, "global.json"))) d = d.Parent;
        return d?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }

    private static string Fixture => Path.Combine(FindRepoRoot(), "tests", "Shiftwall.Core.Tests", "Golden", "Parser", "default-synthetic.fgtcfg");

    [Fact]
    public void DryRun_DoesNotWrite()
    {
        var before = File.ReadAllText(Path.Combine(_dir, "models.json"));
        Assert.Equal(0, DefaultBackupImport.Run(new JsonStore(_dir), "FG-60E", Fixture, new DateOnly(2026, 11, 15), apply: false));
        Assert.Equal(before, File.ReadAllText(Path.Combine(_dir, "models.json")));
    }

    [Fact]
    public void Apply_WritesNamesWithBackupSource_AndStaysValid()
    {
        Assert.Equal(0, DefaultBackupImport.Run(new JsonStore(_dir), "FG-60E", Fixture, new DateOnly(2026, 11, 15), apply: true));
        var db = ModelDatabase.LoadFromDirectory(_dir);
        var m = db.FindModel("FG-60E")!;
        Assert.True(m.Interfaces.IsVerified);
        Assert.Equal(["wan1", "dmz", "internal1", "internal2"], m.Interfaces.Value!.Select(i => i.Name));
        var src = Assert.Single(m.Interfaces.Sources);
        Assert.Equal(SourceKind.FactoryDefaultBackup, src.Kind);
        Assert.Null(src.Url);
        Assert.Equal(["internal1", "internal2"], m.DefaultSwitches.Value!.Single().Members);

        // Nothing from the backup but names ends up in the database.
        var json = File.ReadAllText(Path.Combine(_dir, "models.json"));
        Assert.DoesNotContain("192.168.1.99", json, StringComparison.Ordinal);
        Assert.DoesNotContain("FortiGate-60F", json, StringComparison.Ordinal);
    }

    [Fact]
    public void PartialFile_IsRejected()
    {
        var partial = Path.Combine(_dir, "partial.fgtcfg");
        File.WriteAllText(partial, "config system interface\n    edit \"port1\"\n        set type physical\n    next\nend\n");
        Assert.Equal(1, DefaultBackupImport.Run(new JsonStore(_dir), "FG-60E", partial, new DateOnly(2026, 11, 15), apply: true));
    }
}
