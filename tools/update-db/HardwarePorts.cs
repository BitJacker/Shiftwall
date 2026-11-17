using System.Text.RegularExpressions;
using Shiftwall.Core.Data;

namespace Shiftwall.UpdateDb;

/// <summary>
/// Extracts interface names and maximum speeds from the <c>diagnose npu ... port-list</c> output printed in
/// the FortiOS Hardware Acceleration guide, and compares them with data/models.json.
/// </summary>
internal static partial class HardwarePorts
{
    public sealed record Port(string Name, int SpeedMbps);

    // A FortiOS interface name as printed in the port list (chip names such as np6_0 are excluded separately).
    [GeneratedRegex(@"^[a-z][a-z0-9._-]*$")]
    private static partial Regex NameToken();

    // NP6 family speed column: 1000M, 25000M, 10G...
    [GeneratedRegex(@"^(\d+)([MG])$")]
    private static partial Regex SpeedToken();

    // Chip, port-group or XAUI columns on lines that carry no interface: np6_0, np6xlite_0, NP#0-5, 0, 0-3.
    [GeneratedRegex(@"^(np\w*|NP#\S*|\d+(-\d+)?)$")]
    private static partial Regex ChipToken();

    /// <summary>
    /// Parses the first port-list block found in the page text. Handles the NP6 family layout (chip, XAUI,
    /// name, speed such as <c>1000M</c> or <c>10G</c>, optional QSGMII column) and the NP7 / NP7Lite layout
    /// (name, max speed in Mbps, default speed, any number of further columns).
    /// </summary>
    /// <param name="duplicates">Receives names printed more than once (documentation typos, e.g. port2 where port24 is meant).</param>
    public static IReadOnlyList<Port> ParsePortList(string pageText, List<string>? duplicates = null)
    {
        for (var at = pageText.IndexOf("port-list", StringComparison.Ordinal); at >= 0; at = pageText.IndexOf("port-list", at + 1, StringComparison.Ordinal))
        {
            var ports = ParseBlock(pageText[at..].Split('\n').Skip(1), duplicates);
            if (ports.Count > 0) return ports;
        }

        return [];
    }

    private static List<Port> ParseBlock(IEnumerable<string> lines, List<string>? duplicates)
    {
        var ports = new List<Port>();
        var np7 = false;
        foreach (var raw in lines)
        {
            var line = raw.Trim();
            if (line.Length == 0) continue;
            if (line.StartsWith("---", StringComparison.Ordinal)) continue; // also separates the chips of multi-NP6 models

            if (line.StartsWith("Name", StringComparison.Ordinal) && line.Contains("Max_speed", StringComparison.Ordinal)) { np7 = true; continue; }
            if (line.StartsWith("Chip", StringComparison.Ordinal) || line.StartsWith("Speed", StringComparison.Ordinal) || line.StartsWith("Front Panel", StringComparison.Ordinal)) continue;

            var tokens = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var port = np7 ? Np7Port(tokens) : Np6Port(tokens);
            if (port is not null)
            {
                if (ports.All(p => p.Name != port.Name)) ports.Add(port);
                else duplicates?.Add(port.Name);
                continue;
            }

            if (tokens.All(t => ChipToken().IsMatch(t))) continue; // chip-only line, e.g. "np6_0" or "3"
            if (np7 && tokens.Length >= 3 && int.TryParse(tokens[1], out _) && int.TryParse(tokens[2], out _)) continue; // e.g. "HA1_np": not a config name
            break; // prose: end of the block (or, before any row, not a port list at all)
        }

        return ports;
    }

    private static Port? Np7Port(string[] t) =>
        t.Length >= 3 && NameToken().IsMatch(t[0]) && int.TryParse(t[1], out var max) && int.TryParse(t[2], out _) ? new Port(t[0], max) : null;

    private static Port? Np6Port(string[] t)
    {
        var s = Array.FindIndex(t, x => SpeedToken().IsMatch(x));
        if (s <= 0) return null;
        var n = Array.FindLastIndex(t, s - 1, x => NameToken().IsMatch(x) && !ChipToken().IsMatch(x));
        if (n < 0) return null;
        var m = SpeedToken().Match(t[s]);
        var mbps = int.Parse(m.Groups[1].Value) * (m.Groups[2].Value == "G" ? 1000 : 1);
        return new Port(t[n], mbps);
    }

    /// <summary>
    /// Adds new models (or fills an unverified interface list) from one Hardware Acceleration page. Disk and
    /// default switches stay unverified: they come from data sheets and factory-default backups, not from this page.
    /// </summary>
    public static async Task<int> AddAsync(JsonStore store, string url, IReadOnlyList<string> ids, DateOnly today, bool apply, CancellationToken ct)
    {
        using var client = new DocsClient();
        var page = await client.GetAsync(url, ct);
        var duplicates = new List<string>();
        var ports = ParsePortList(HtmlText.ToText(page.Html), duplicates);
        if (ports.Count == 0)
        {
            Console.Error.WriteLine($"No port list found at {url}: nothing added.");
            return 1;
        }

        var note = "Names and speeds from the `diagnose npu ... port-list` output in the Hardware Acceleration guide; it lists NP-connected interfaces only (management and HA ports are often missing). Media not documented there.";
        if (duplicates.Count > 0)
        {
            note += $" The documented output prints {string.Join(", ", duplicates.Distinct())} more than once (a typo in the guide): a port may be missing from this list. Load a factory-default backup to complete it.";
            Console.WriteLine($"WARNING: duplicate names in the documented list: {string.Join(", ", duplicates)}");
        }

        var version = Regex.Match(url, @"/fortigate/(\d+\.\d+\.\d+)/").Groups[1].Value;
        var title = $"FortiOS {version} Hardware Acceleration — {HtmlText.Title(page.Html)}";
        var source = new SourceRef(url, title, today, "`diagnose npu ... port-list` output");
        var interfaces = new Sourced<IReadOnlyList<InterfaceInfo>>(
            ports.Select(p => new InterfaceInfo(p.Name, "unknown", FormatSpeed(p.SpeedMbps))).ToList(),
            VerificationStatus.Verified, [source], note);

        var doc = store.ReadModels();
        var models = doc.Models.ToList();
        foreach (var id in ids)
        {
            var i = models.FindIndex(m => m.Id == id);
            if (i >= 0 && models[i].Interfaces.IsVerified)
            {
                Console.WriteLine($"{id}: already verified, skipped (use refresh-hw-ports).");
                continue;
            }

            if (i >= 0)
            {
                models[i] = models[i] with { Interfaces = interfaces };
                Console.WriteLine($"{id}: interfaces filled ({ports.Count}).");
                continue;
            }

            models.Add(new FortiGateModel(id, DisplayName(id), "FortiGate", "hardware", interfaces,
                Sourced<bool?>.Unverified("Not checked yet: see the model's data sheet (Storage / Internal Storage)."),
                Sourced<IReadOnlyList<HardwareSwitchInfo>>.Unverified("Factory-default hardware/virtual switch membership is not documented in the sources used. Load a factory-default backup of the target model to get it.")));
            Console.WriteLine($"{id}: added ({string.Join(",", ports.Select(p => p.Name))}).");
        }

        if (apply) store.Write(doc with { Models = models });
        else Console.WriteLine("Dry run: add --apply to write models.json.");
        return 0;
    }

    /// <summary>FG-400F → "FortiGate 400F", FGR-60F → "FortiGate Rugged 60F".</summary>
    public static string DisplayName(string id) => id.StartsWith("FGR-", StringComparison.Ordinal)
        ? "FortiGate Rugged " + id[4..]
        : "FortiGate " + (id.StartsWith("FG-", StringComparison.Ordinal) ? id[3..] : id);

    public static string FormatSpeed(int mbps) => mbps >= 1000 ? $"{mbps / 1000}G" : $"{mbps}M";

    public static async Task<int> RefreshAsync(JsonStore store, DateOnly today, bool apply, CancellationToken ct)
    {
        var doc = store.ReadModels();
        using var client = new DocsClient();
        var cache = new Dictionary<string, IReadOnlyList<Port>>(StringComparer.Ordinal);
        var updated = new List<FortiGateModel>();
        var differences = 0;

        foreach (var model in doc.Models)
        {
            var src = model.Interfaces.Sources.FirstOrDefault(s => s.Url?.Contains("/hardware-acceleration/", StringComparison.Ordinal) == true);
            if (!model.Interfaces.IsVerified || src?.Url is null)
            {
                updated.Add(model);
                continue;
            }

            if (!cache.TryGetValue(src.Url, out var ports))
            {
                ports = ParsePortList(HtmlText.ToText((await client.GetAsync(src.Url, ct)).Html));
                cache[src.Url] = ports;
            }

            if (ports.Count == 0)
            {
                Console.WriteLine($"{model.Id}: port list not found at {src.Url} — page layout changed? Not modified.");
                differences++;
                updated.Add(model);
                continue;
            }

            var current = model.Interfaces.Value ?? [];
            var fresh = ports.Select(p =>
            {
                var old = current.FirstOrDefault(i => i.Name == p.Name);
                return new InterfaceInfo(p.Name, old?.Media ?? "unknown", FormatSpeed(p.SpeedMbps), old?.Note);
            }).ToList();

            var same = fresh.Count == current.Count && fresh.Zip(current).All(z => z.First.Name == z.Second.Name && z.First.Speed == z.Second.Speed);
            if (same)
            {
                Console.WriteLine($"{model.Id}: unchanged ({fresh.Count} interfaces).");
            }
            else
            {
                differences++;
                var added = fresh.Select(i => i.Name).Except(current.Select(i => i.Name));
                var removed = current.Select(i => i.Name).Except(fresh.Select(i => i.Name));
                Console.WriteLine($"{model.Id}: CHANGED +[{string.Join(",", added)}] -[{string.Join(",", removed)}] (speeds/order may differ). New interfaces get media \"unknown\": fix by hand.");
            }

            var sources = model.Interfaces.Sources.Select(s => s == src ? s with { VerifiedOn = today } : s).ToList();
            updated.Add(model with { Interfaces = model.Interfaces with { Value = fresh, Sources = sources } });
        }

        if (apply)
        {
            store.Write(doc with { Models = updated });
            Console.WriteLine("models.json updated. Review with: git diff data/models.json");
        }
        else if (differences > 0)
        {
            Console.WriteLine("Run again with --apply to write the changes.");
        }

        return 0;
    }
}
