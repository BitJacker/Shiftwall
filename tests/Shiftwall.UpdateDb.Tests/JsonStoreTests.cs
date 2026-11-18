using Shiftwall.UpdateDb;

namespace Shiftwall.UpdateDb.Tests;

/// <summary>The committed data files must already be in canonical form, so tool runs produce minimal diffs.</summary>
public sealed class JsonStoreTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("shiftwall-json-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private static string RepoData()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d is not null && !File.Exists(Path.Combine(d.FullName, "global.json"))) d = d.Parent;
        return Path.Combine(d!.FullName, "data");
    }

    [Theory]
    [InlineData("models.json")]
    [InlineData("fortios-releases.json")]
    [InlineData("upgrade-paths.json")]
    public void DataFiles_AreCanonical(string file)
    {
        foreach (var f in Directory.GetFiles(RepoData(), "*.json")) File.Copy(f, Path.Combine(_dir, Path.GetFileName(f)));
        var store = new JsonStore(_dir);
        store.Write(store.ReadModels());
        store.Write(store.ReadReleases());
        store.Write(store.ReadUpgradePaths());
        Assert.Equal(File.ReadAllText(Path.Combine(RepoData(), file)), File.ReadAllText(Path.Combine(_dir, file)));
    }
}
