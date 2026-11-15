using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Shiftwall.Features.Migration;

namespace Shiftwall.Features.Tests.Migration;

public sealed class OutputAndReportTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("shiftwall-out-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private static ReportContext Ctx(bool diff = true) =>
        new("branch.conf", "branch-FG-70G.conf", "0.1.0", new DateTimeOffset(2026, 11, 15, 12, 0, 0, TimeSpan.Zero), CultureInfo.GetCultureInfo("it-IT"), diff);

    [Fact]
    public void SafeOutput_RefusesToOverwriteTheOriginal()
    {
        var original = Path.Combine(_dir, "site.conf");
        File.WriteAllText(original, "original");
        Assert.Throws<UnsafeOutputException>(() => SafeOutput.Write(original, [1, 2, 3], [original], allowOverwrite: true));
        Assert.Throws<UnsafeOutputException>(() => SafeOutput.Write(Path.Combine(_dir, ".", "site.conf"), [1], [original]));
        Assert.Equal("original", File.ReadAllText(original));
    }

    [Fact]
    public void SafeOutput_RefusesExistingFiles_UnlessAllowed()
    {
        var target = Path.Combine(_dir, "out.conf");
        SafeOutput.Write(target, [1], []);
        Assert.Throws<UnsafeOutputException>(() => SafeOutput.Write(target, [2], []));
        SafeOutput.Write(target, [3], [], allowOverwrite: true);
        Assert.Equal([3], File.ReadAllBytes(target));
        Assert.Single(Directory.GetFiles(_dir)); // no temporary leftovers
    }

    [Fact]
    public void SuggestName_NeverReturnsTheSourceOrAnExistingFile()
    {
        var src = Path.Combine(_dir, "site.conf");
        File.WriteAllText(src, "x");
        var first = SafeOutput.SuggestName(src, "FG-70G");
        Assert.Equal(Path.Combine(_dir, "site-FG-70G.conf"), first);
        File.WriteAllText(first, "y");
        Assert.Equal(Path.Combine(_dir, "site-FG-70G-2.conf"), SafeOutput.SuggestName(src, "FG-70G"));
    }

    [Fact]
    public void HtmlReport_IsSelfContained_Escaped_AndHasDisclaimer()
    {
        var source = Fixtures.BranchSource();
        var request = new MigrationRequest(source, Fixtures.BranchTarget(), Fixtures.BranchMapping(source));
        var html = HtmlReport.Build(request, MigrationEngine.Run(request), Ctx());

        Assert.Contains("Shiftwall non è affiliato a Fortinet", html, StringComparison.Ordinal);
        Assert.Contains("non invia dati", html, StringComparison.Ordinal);
        Assert.DoesNotContain("<script", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("http://", html, StringComparison.Ordinal);
        Assert.DoesNotContain("https://", html, StringComparison.Ordinal);
        Assert.Contains("&quot;port6&quot;", html, StringComparison.Ordinal); // diff lines are HTML-escaped
    }

    [Fact]
    public void HtmlReport_WithoutDiff_ContainsNoConfigurationText()
    {
        var source = Fixtures.BranchSource();
        var request = new MigrationRequest(source, Fixtures.BranchTarget(), Fixtures.BranchMapping(source));
        var html = HtmlReport.Build(request, MigrationEngine.Run(request), Ctx(diff: false));
        Assert.DoesNotContain("U2hpZnR3YWxs", html, StringComparison.Ordinal); // ENC values
        Assert.DoesNotContain("203.0.113.10", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Batch_WritesOutputs_AndNeverModifiesOriginals()
    {
        var sources = new List<string>();
        for (var i = 1; i <= 3; i++)
        {
            var p = Path.Combine(_dir, $"site{i}.conf");
            File.WriteAllBytes(p, Fixtures.Bytes("branch-source.fgtcfg"));
            sources.Add(p);
        }

        var hashes = sources.ToDictionary(p => p, p => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(p))));
        var template = MappingTemplate.FromJson(Fixtures.Text("branch-mapping.json"));
        var outDir = Path.Combine(_dir, "out");

        var sim = BatchMigration.Run(sources, Fixtures.BranchTarget(), template, outDir, Ctx(), simulate: true, ct: TestContext.Current.CancellationToken);
        Assert.All(sim, r => Assert.True(r.Succeeded));
        Assert.False(Directory.Exists(outDir));

        var real = BatchMigration.Run(sources, Fixtures.BranchTarget(), template, outDir, Ctx(), simulate: false, ct: TestContext.Current.CancellationToken);
        Assert.All(real, r => Assert.True(r.Succeeded, r.Error));
        Assert.Equal(6, Directory.GetFiles(outDir).Length); // 3 configs + 3 reports
        Assert.All(real, r => Assert.Equal(Fixtures.Text("branch-expected.fgtcfg"), File.ReadAllText(r.OutputPath!, Encoding.UTF8)));
        Assert.All(sources, p => Assert.Equal(hashes[p], Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(p)))));
    }

    [Fact]
    public void Batch_ReportsNonTextFiles_WithoutThrowing()
    {
        var bin = Path.Combine(_dir, "encrypted.conf");
        File.WriteAllBytes(bin, [0, 1, 2, 3, 0, 5]);
        var r = Assert.Single(BatchMigration.Run([bin], Fixtures.BranchTarget(), MappingTemplate.FromJson(Fixtures.Text("branch-mapping.json")), _dir, Ctx(), simulate: false, ct: TestContext.Current.CancellationToken));
        Assert.False(r.Succeeded);
        Assert.NotNull(r.Error);
    }

    [Fact]
    public void Diff_ShowsOnlyChangedRegions()
    {
        var before = string.Join('\n', Enumerable.Range(1, 100).Select(i => $"line {i}")) + "\n";
        var after = before.Replace("line 50\n", "line fifty\n", StringComparison.Ordinal);
        var diff = ConfigDiff.Unified(before, after, context: 2);
        Assert.Contains(diff, l => l.Kind == DiffLineKind.Removed && l.Text == "line 50" && l.OldLine == 50);
        Assert.Contains(diff, l => l.Kind == DiffLineKind.Added && l.Text == "line fifty" && l.NewLine == 50);
        Assert.True(diff.Count < 12);
    }
}
