using System.Diagnostics;
using System.Text;
using Shiftwall.Core.Config;
using Shiftwall.Features.Toolbox;
using Shiftwall.Features.Toolbox.Anonymize;
using Shiftwall.Features.Toolbox.Audit;
using Shiftwall.Features.Toolbox.Export;

namespace Shiftwall.Features.Tests.Toolbox;

/// <summary>Every tool must handle a large configuration (50,000+ lines) without hanging the UI thread for long.</summary>
public sealed class ToolboxPerformanceTests
{
    private static readonly string Large = Build(policies: 3000, addresses: 6000);

    private static string Build(int policies, int addresses)
    {
        var sb = new StringBuilder(Tb.Header);
        sb.Append("config system interface\n");
        for (var i = 1; i <= 16; i++) sb.Append($"    edit \"port{i}\"\n        set ip 10.{i}.0.1 255.255.255.0\n        set type physical\n    next\n");
        sb.Append("end\nconfig firewall address\n");
        for (var i = 0; i < addresses; i++) sb.Append($"    edit \"h{i}\"\n        set subnet 10.{i / 250 % 250}.{i % 250}.1 255.255.255.255\n        set comment \"host {i}\"\n    next\n");
        sb.Append("end\nconfig firewall policy\n");
        for (var i = 1; i <= policies; i++)
        {
            sb.Append($"    edit {i}\n        set name \"p{i}\"\n        set srcintf \"port{i % 16 + 1}\"\n        set dstintf \"port{(i + 3) % 16 + 1}\"\n")
              .Append($"        set action accept\n        set srcaddr \"h{i}\"\n        set dstaddr \"h{i + 1}\"\n        set schedule \"always\"\n        set service \"ALL\"\n        set comments \"policy {i}\"\n    next\n");
        }

        return sb.Append("end\n").ToString();
    }

    private static void Within(double seconds, Action action)
    {
        var sw = Stopwatch.StartNew();
        action();
        Assert.True(sw.Elapsed.TotalSeconds < seconds, $"took {sw.Elapsed.TotalSeconds:F2} s");
    }

    [Fact]
    public void Fixture_IsOver50kLines() => Assert.True(ConfigParser.Parse(Large).LineCount > 50_000);

    [Fact]
    public void Audit_IsFastOnLargeConfigs() => Within(10, () => AuditEngine.Run(ConfigParser.Parse(Large)));

    [Fact]
    public void UnusedObjects_AreFastOnLargeConfigs() => Within(10, () => UnusedObjects.Find(ConfigParser.Parse(Large)));

    [Fact]
    public void Export_IsFastOnLargeConfigs() => Within(10, () => XlsxWriter.Write(ConfigExport.Build(ConfigParser.Parse(Large))));

    [Fact]
    public void Anonymize_IsFastOnLargeConfigs() => Within(10, () => Anonymizer.Anonymize(ConfigParser.Parse(Large), new AnonymizeOptions(PrivateIps: true)));

    [Fact]
    public void Compare_IsFastOnLargeConfigs() =>
        Within(10, () => SectionDiff.Compare(ConfigParser.Parse(Large), ConfigParser.Parse(Large.Replace("policy 7\"", "policy 7 changed\"", StringComparison.Ordinal))));
}
