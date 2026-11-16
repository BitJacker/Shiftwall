using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using Shiftwall.Core.Config;
using Shiftwall.Core.Data;
using Shiftwall.Features.Migration;

namespace Shiftwall.Features.Tests;

/// <summary>
/// The files in samples/ are the only .conf files the repository accepts. They must stay synthetic: a marker line,
/// documentation or private IPv4 addresses only, example domains only, and ENC values that decode to a test string.
/// They must also work for the "Try it in one minute" steps in the README.
/// </summary>
public sealed partial class SamplesTests
{
    private const string Marker = "#shiftwall-sample: synthetic data written for Shiftwall, not taken from any real device";

    private static string SamplesDir()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d is not null && !File.Exists(Path.Combine(d.FullName, "global.json"))) d = d.Parent;
        return Path.Combine(d!.FullName, "samples");
    }

    public static TheoryData<string> Configs()
    {
        var data = new TheoryData<string>();
        foreach (var f in Directory.GetFiles(SamplesDir(), "*.conf").Order(StringComparer.Ordinal)) data.Add(Path.GetFileName(f));
        return data;
    }

    private static string Read(string name) => File.ReadAllText(Path.Combine(SamplesDir(), name));

    [Fact]
    public void SamplesExist()
    {
        Assert.InRange(Directory.GetFiles(SamplesDir(), "*.conf").Length, 2, 5);
    }

    [Theory]
    [MemberData(nameof(Configs))]
    public void Sample_EndsWithTheSyntheticMarker(string name)
    {
        var last = Read(name).TrimEnd('\r', '\n').Split('\n')[^1].TrimEnd('\r');
        Assert.Equal(Marker, last);
    }

    [Theory]
    [MemberData(nameof(Configs))]
    public void Sample_UsesOnlyDocumentationOrPrivateIPv4(string name)
    {
        foreach (Match m in Ipv4().Matches(Read(name)))
        {
            Assert.True(IPAddress.TryParse(m.Value, out var ip), m.Value);
            var b = ip!.GetAddressBytes();
            var allowed =
                b[0] == 10 || (b[0] == 172 && b[1] is >= 16 and <= 31) || (b[0] == 192 && b[1] == 168) // RFC 1918
                || (b[0] == 192 && b[1] == 0 && b[2] == 2) || (b[0] == 198 && b[1] == 51 && b[2] == 100) || (b[0] == 203 && b[1] == 0 && b[2] == 113) // RFC 5737
                || b[0] == 255 || m.Value == "0.0.0.0"; // netmasks and "any"
            Assert.True(allowed, $"{name}: {m.Value} is not a documentation or private address");
        }
    }

    [Theory]
    [MemberData(nameof(Configs))]
    public void Sample_UsesOnlyExampleDomains(string name)
    {
        foreach (Match m in Domain().Matches(Read(name)))
        {
            Assert.True(m.Value == "example.com" || m.Value.EndsWith(".example.com", StringComparison.Ordinal), $"{name}: {m.Value}");
        }
    }

    [Theory]
    [MemberData(nameof(Configs))]
    public void Sample_EncValuesAreSyntheticTestStrings(string name)
    {
        foreach (Match m in Enc().Matches(Read(name)))
        {
            var text = Encoding.UTF8.GetString(Convert.FromBase64String(m.Groups[1].Value));
            Assert.Contains("synthetic", text, StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>README "Try it in one minute": the migration with the sample template is valid.</summary>
    [Fact]
    public void BranchSample_MigratesToTheFactoryDefaultSample()
    {
        var source = SourceProfile.From(ConfigInspector.Inspect(File.ReadAllBytes(Path.Combine(SamplesDir(), "branch-60f.conf"))));
        var target = TargetProfile.FromDefaultBackup(
            ConfigInspector.Inspect(File.ReadAllBytes(Path.Combine(SamplesDir(), "factory-default-70g.conf"))),
            ModelDatabase.LoadEmbedded().FindModel("FG-70G"));
        var template = MappingTemplate.FromJson(Read("branch-60f-to-70g.swmap.json"));
        var mapping = InterfaceMapping.FromTemplate(template, source.PhysicalInterfaces.Select(p => p.Name));

        var result = MigrationEngine.Run(new MigrationRequest(source, target, mapping));

        Assert.False(result.IsBlocked);
        Assert.True(result.IsValid, string.Join("; ", result.Validation.Select(v => v.Format())));
    }

    [GeneratedRegex(@"(?<![\d.])(?:\d{1,3}\.){3}\d{1,3}(?![\d.])")]
    private static partial Regex Ipv4();

    [GeneratedRegex(@"[A-Za-z0-9-]+(?:\.[A-Za-z0-9-]+)*\.(?:com|net|org|it|io|eu|de|fr|uk|local|lan)\b")]
    private static partial Regex Domain();

    [GeneratedRegex(@"ENC ([A-Za-z0-9+/=]+)")]
    private static partial Regex Enc();
}
