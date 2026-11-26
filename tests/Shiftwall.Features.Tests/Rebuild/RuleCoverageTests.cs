using Shiftwall.Features.Rebuild;

namespace Shiftwall.Features.Tests.Rebuild;

/// <summary>
/// Every difference between the CLI schemas of two versions, on the paths Shiftwall rebuilds, must have a rule.
/// A new schema (or a new path) with an uncovered difference makes this test fail: add the rule with its source.
/// </summary>
public sealed class RuleCoverageTests
{
    public static TheoryData<string, string> Pairs() => new() { { "5.2", "8.0" } };

    [Theory]
    [MemberData(nameof(Pairs))]
    public void EveryDifferenceHasARule(string from, string to)
    {
        var rules = RuleSet.Load(from, to);
        var old = CliSchema.Load(from);
        var @new = CliSchema.Load(to);
        var missing = new List<string>();
        foreach (var path in rules.Paths)
        {
            Assert.True(old.Paths.ContainsKey(path), $"{path} missing in the {from} schema");
            Assert.True(@new.Paths.ContainsKey(path), $"{path} missing in the {to} schema");
            Walk(old.Paths[path], @new.Paths[path], path, [], rules, missing);
        }

        Assert.True(missing.Count == 0, "Differences without a rule:\n" + string.Join("\n", missing));
    }

    private static void Walk(SchemaNode a, SchemaNode b, string path, List<string> sub, RuleSet rules, List<string> missing)
    {
        foreach (var (name, p) in a.Params)
        {
            if (!b.Params.TryGetValue(name, out var q))
            {
                var r = rules.ForParam(path, sub, name);
                if (r is null || r.Kind is "value-map" or "default-change") missing.Add($"{path} [{string.Join(' ', sub)}] param {name}");
                continue;
            }

            if (p is { Type: "option", Options: { } po } && q is { Type: "option", Options: { } qo })
            {
                foreach (var v in po.Where(v => !qo.Contains(v) && !v.Contains(' ', StringComparison.Ordinal)))
                {
                    var r = rules.ForParam(path, sub, name, [v]);
                    var ok = r is not null && (r.Kind == "value-map" ? r.Map.ContainsKey(v) : r.Kind is "remove-no-equivalent" or "remove-with-equivalent" or "not-a-difference");
                    if (!ok) missing.Add($"{path} [{string.Join(' ', sub)}] {name} value {v}");
                }
            }
        }

        foreach (var (name, node) in a.Sub)
        {
            if (!b.Sub.TryGetValue(name, out var other))
            {
                if (rules.ForTable(path, sub, name) is null) missing.Add($"{path} [{string.Join(' ', sub)}] table {name}");
                continue;
            }

            Walk(node, other, path, [.. sub, name], rules, missing);
        }
    }

    [Theory]
    [MemberData(nameof(Pairs))]
    public void EveryRuleHasAnOfficialSource_AndAValidKind(string from, string to)
    {
        var rules = RuleSet.Load(from, to);
        string[] kinds = ["rename", "value-map", "default-change", "remove-no-equivalent", "remove-with-equivalent", "skip", "not-a-difference"];
        Assert.All(rules.Rules, r =>
        {
            Assert.Contains(r.Kind, kinds);
            Assert.NotEmpty(r.Sources);
            Assert.All(r.Sources, s => Assert.Matches(@"^https://(docs\.fortinet\.com|fortinetweb\.s3\.amazonaws\.com)/", s.Url));
            Assert.False(string.IsNullOrWhiteSpace(r.Note), r.Id);
        });
        Assert.Equal(rules.Rules.Count, rules.Rules.Select(r => r.Id).Distinct().Count());
    }

    [Theory]
    [MemberData(nameof(Pairs))]
    public void ValueMapsAndDefaults_AreValidOnTheTarget(string from, string to)
    {
        var rules = RuleSet.Load(from, to);
        var target = CliSchema.Load(to);
        foreach (var r in rules.Rules.Where(r => r.Kind is "value-map" or "default-change"))
        {
            var node = target.Find(r.Path, r.Sub)!;
            var p = node.Params[r.Param!];
            var values = r.Kind == "value-map" ? r.Map.Values.ToList() : [.. r.Value!.Split(' ')];
            Assert.Null(CliSchema.Check(p, values));
        }
    }

    [Fact]
    public void Schemas_RecordTheirOfficialSource()
    {
        foreach (var v in CliSchema.Bundled)
        {
            var s = CliSchema.Load(v);
            Assert.Matches(@"^https://(docs\.fortinet\.com|fortinetweb\.s3\.amazonaws\.com)/", s.SourceUrl);
            Assert.Matches(@"^\d{4}-\d{2}-\d{2}$", s.VerifiedOn);
            Assert.True(s.Paths.Count > 300, $"{v}: {s.Paths.Count} paths");
        }
    }
}
