using Shiftwall.Core.Data;
using Shiftwall.UpdateDb;

// update-db: developer tool that maintains data/*.json from official Fortinet documentation.
// It is NOT shipped in Shiftwall.exe. Every change it makes is meant to be reviewed in a git diff.

const string Usage = """
    Usage: dotnet run --project tools/update-db -- <command> [options]

    Commands:
      validate                         Validate data/*.json (same rules the app enforces at load time).
      fetch-releases [--branches 7.4,7.6,8.0]
                                       Add/refresh FortiOS releases from the official release notes
                                       (build, initial-release date, supported models).
      refresh-hw-ports [--apply]       Re-read the hardware-acceleration page cited for each model and
                                       compare the documented port list with data/models.json.
      import-default-backup --model <id> --file <path> [--apply]
                                       Take interface names and virtual-switch membership from a real
                                       factory-default backup read from the local disk. The backup is never
                                       copied or committed: only the names go to models.json.
      check-links                      Check that every source URL still answers HTTP 200.
      gen-docs [--check]               Regenerate docs/SOURCES.md, docs/supported-models.md and the model
                                       tables in README.md and README.it.md. With --check, write nothing and
                                       fail if any of them is out of date (used by CI).
      normalize                        Rewrite data/*.json in canonical formatting (after hand edits).

    Options:
      --data <dir>                     Data directory (default: ./data)
      --docs <dir>                     Docs directory (default: ./docs)
    """;

if (args.Length == 0 || args[0] is "-h" or "--help")
{
    Console.WriteLine(Usage);
    return 0;
}

var dataDir = Option("--data") ?? Path.Combine(Environment.CurrentDirectory, "data");
var docsDir = Option("--docs") ?? Path.Combine(Environment.CurrentDirectory, "docs");
var store = new JsonStore(dataDir);
var today = DateOnly.FromDateTime(DateTime.UtcNow);
using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

try
{
    switch (args[0])
    {
        case "validate":
            ModelDatabase.LoadFromDirectory(dataDir);
            Console.WriteLine("OK: database is valid.");
            return 0;

        case "fetch-releases":
            return await FetchReleasesAsync(cts.Token);

        case "refresh-hw-ports":
            return await HardwarePorts.RefreshAsync(store, today, args.Contains("--apply"), cts.Token);

        case "check-links":
            return await CheckLinksAsync(cts.Token);

        case "import-default-backup":
            if (Option("--model") is not { } model || Option("--file") is not { } file)
            {
                Console.Error.WriteLine("import-default-backup needs --model <id> and --file <path>.");
                return 2;
            }

            return DefaultBackupImport.Run(store, model, file, today, args.Contains("--apply"));

        case "normalize":
            ModelDatabase.LoadFromDirectory(dataDir);
            store.Write(store.ReadModels());
            store.Write(store.ReadReleases());
            store.Write(store.ReadUpgradePaths());
            ModelDatabase.LoadFromDirectory(dataDir);
            Console.WriteLine("data/*.json rewritten in canonical format.");
            return 0;

        case "gen-docs":
        {
            var db = ModelDatabase.LoadFromDirectory(dataDir);
            var root = Path.GetDirectoryName(Path.GetFullPath(docsDir))!;
            var outputs = new List<(string Path, string Text)>
            {
                (Path.Combine(docsDir, "SOURCES.md"), DocsGenerator.Sources(db)),
                (Path.Combine(docsDir, "supported-models.md"), DocsGenerator.SupportedModels(db)),
            };
            foreach (var (name, italian) in new[] { ("README.md", false), ("README.it.md", true) })
            {
                var readme = Path.Combine(root, name);
                if (!File.Exists(readme)) continue;
                var table = DocsGenerator.ModelsTable(db, "docs/SOURCES.md", italian);
                outputs.Add((readme, DocsGenerator.ReplaceTable(File.ReadAllText(readme), table)));
            }

            if (args.Contains("--check"))
            {
                var stale = outputs.Where(o => !File.Exists(o.Path) || File.ReadAllText(o.Path).ReplaceLineEndings() != o.Text.ReplaceLineEndings()).ToList();
                foreach (var o in stale) Console.Error.WriteLine($"Out of date: {o.Path} (run gen-docs)");
                return stale.Count == 0 ? 0 : 1;
            }

            Directory.CreateDirectory(docsDir);
            foreach (var o in outputs)
            {
                File.WriteAllText(o.Path, o.Text);
                Console.WriteLine($"Wrote {o.Path}");
            }

            return 0;
        }

        default:
            Console.Error.WriteLine($"Unknown command '{args[0]}'.");
            Console.Error.WriteLine(Usage);
            return 2;
    }
}
catch (DatabaseException ex)
{
    Console.Error.WriteLine(ex.Message);
    return 1;
}
catch (OperationCanceledException)
{
    Console.Error.WriteLine("Cancelled.");
    return 130;
}

string? Option(string name)
{
    var i = Array.IndexOf(args, name);
    return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
}

async Task<int> FetchReleasesAsync(CancellationToken ct)
{
    var branches = (Option("--branches") ?? "6.4,7.0,7.2,7.4,7.6,8.0").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    using var client = new DocsClient();
    var fetcher = new ReleaseNotesFetcher(client, today);
    var doc = File.Exists(Path.Combine(dataDir, "fortios-releases.json")) ? store.ReadReleases() : new ReleasesDocument(ModelDatabase.SupportedSchemaVersion, []);
    var byVersion = doc.Releases.ToDictionary(r => r.Version, StringComparer.Ordinal);

    foreach (var branch in branches)
    {
        foreach (var version in await fetcher.ListVersionsAsync(branch, ct))
        {
            var fresh = await fetcher.FetchAsync(version, ct);
            var status = byVersion.TryGetValue(version, out var old) ? Describe(old, fresh) : "new";
            byVersion[version] = fresh;
            Console.WriteLine($"{version,-8} build {fresh.Build.Value ?? "?",-5} date {fresh.ReleaseDate.Value?.ToString("yyyy-MM-dd") ?? "?",-10} models {fresh.SupportedModels.Value?.Count ?? 0,3}  {status}");
        }
    }

    store.Write(doc with { Releases = [.. byVersion.Values.OrderBy(r => FortiOsVersion.Parse(r.Version))] });
    Console.WriteLine("Review the changes with: git diff data/fortios-releases.json");
    return 0;

    static string Describe(FortiOsRelease old, FortiOsRelease fresh)
    {
        var changes = new List<string>();
        if (old.Build.Value != fresh.Build.Value) changes.Add($"build {old.Build.Value}->{fresh.Build.Value}");
        if (old.ReleaseDate.Value != fresh.ReleaseDate.Value) changes.Add($"date {old.ReleaseDate.Value}->{fresh.ReleaseDate.Value}");
        var a = old.SupportedModels.Value ?? [];
        var b = fresh.SupportedModels.Value ?? [];
        var added = b.Except(a).ToList();
        var removed = a.Except(b).ToList();
        if (added.Count > 0) changes.Add("models +" + string.Join(",", added));
        if (removed.Count > 0) changes.Add("models -" + string.Join(",", removed));
        return changes.Count == 0 ? "unchanged" : "CHANGED: " + string.Join("; ", changes);
    }
}

async Task<int> CheckLinksAsync(CancellationToken ct)
{
    var urls = new SortedSet<string>(StringComparer.Ordinal);
    var models = store.ReadModels();
    var releases = store.ReadReleases();
    var paths = store.ReadUpgradePaths();
    void Add<T>(Sourced<T> s) { foreach (var src in s.Sources) if (src.Url is not null) urls.Add(src.Url); }
    foreach (var m in models.Models) { Add(m.Interfaces); Add(m.HasDisk); Add(m.DefaultSwitches); }
    foreach (var r in releases.Releases) { Add(r.Build); Add(r.ReleaseDate); Add(r.SupportedModels); }
    Add(paths.UpgradePaths.PublishedInConsultableForm);
    urls.Add(paths.UpgradePaths.OfficialToolUrl);

    using var client = new DocsClient(TimeSpan.FromMilliseconds(400));
    var failures = 0;
    foreach (var url in urls)
    {
        var code = await client.StatusAsync(url, ct);
        if (code != 200)
        {
            failures++;
            Console.WriteLine($"{code} {url}");
        }
    }

    Console.WriteLine($"{urls.Count} URLs checked, {failures} failing.");
    return failures == 0 ? 0 : 1;
}
