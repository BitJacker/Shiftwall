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

    // NP6 / NP6Lite / NP6XLite: "<xaui> <name> <speed>M <YES|NO>"
    [GeneratedRegex(@"^\d+\s+([a-z0-9._-]+)\s+(\d+)M\s+(?:YES|NO)$")]
    private static partial Regex Np6Row();

    // NP7Lite: "<name> <max_speed> <default_speed> <col> <col>"
    [GeneratedRegex(@"^([a-z][a-z0-9._-]*)\s+(\d+)\s+(\d+)\s+\S+\s+\S+$")]
    private static partial Regex Np7Row();

    /// <summary>Parses the first port-list block found in the page text.</summary>
    public static IReadOnlyList<Port> ParsePortList(string pageText)
    {
        var ports = new List<Port>();
        var start = pageText.IndexOf("port-list", StringComparison.Ordinal);
        if (start < 0) return ports;

        // Skip the sentence that mentions the command; the block starts at the command echo line.
        var echo = pageText.IndexOf("port-list", start + 1, StringComparison.Ordinal);
        var lines = pageText[(echo >= 0 ? echo : start)..].Split('\n');
        foreach (var raw in lines.Skip(1))
        {
            var line = raw.Trim();
            if (line.Length == 0) { if (ports.Count > 0) break; continue; }

            var m6 = Np6Row().Match(line);
            if (m6.Success) { ports.Add(new Port(m6.Groups[1].Value, int.Parse(m6.Groups[2].Value))); continue; }

            var m7 = Np7Row().Match(line);
            if (m7.Success && m7.Groups[1].Value != "name") { ports.Add(new Port(m7.Groups[1].Value, int.Parse(m7.Groups[2].Value))); continue; }

            var isHeader = line.StartsWith("np6", StringComparison.Ordinal) || line.StartsWith("np7", StringComparison.Ordinal)
                || line.StartsWith("---", StringComparison.Ordinal) || line.StartsWith("Chip", StringComparison.Ordinal)
                || line.StartsWith("Speed", StringComparison.Ordinal) || line.StartsWith("Front Panel", StringComparison.Ordinal)
                || line.StartsWith("Name", StringComparison.Ordinal);
            if (!isHeader && ports.Count > 0) break;
        }

        return ports;
    }

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
