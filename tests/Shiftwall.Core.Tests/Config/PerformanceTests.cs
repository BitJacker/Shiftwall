using System.Diagnostics;
using System.Text;
using Shiftwall.Core.Config;

namespace Shiftwall.Core.Tests.Config;

public sealed class PerformanceTests
{
    /// <summary>A synthetic ~60,000-line configuration (5,000 addresses + 1,000 policies).</summary>
    internal static string LargeConfig()
    {
        var sb = new StringBuilder();
        sb.Append("#config-version=FGT100F-7.6.8-FW-build3744-261022:opmode=0:vdom=0:user=admin\n");
        sb.Append("config system global\n    set hostname \"perf\"\nend\n");
        sb.Append("config firewall address\n");
        for (var i = 0; i < 5000; i++)
        {
            sb.Append("    edit \"host-").Append(i).Append("\"\n");
            sb.Append("        set uuid 00000000-0000-0000-0000-").Append(i.ToString("D12")).Append('\n');
            sb.Append("        set subnet 10.").Append(i / 256 % 256).Append('.').Append(i % 256).Append(".1 255.255.255.255\n");
            sb.Append("        set comment \"synthetic address ").Append(i).Append("\"\n");
            sb.Append("    next\n");
        }

        sb.Append("end\nconfig firewall policy\n");
        for (var i = 1; i <= 5000; i++)
        {
            sb.Append("    edit ").Append(i).Append('\n');
            sb.Append("        set srcintf \"port1\"\n        set dstintf \"port2\"\n");
            sb.Append("        set srcaddr \"host-").Append(i % 5000).Append("\"\n        set dstaddr \"all\"\n");
            sb.Append("        set action accept\n        set schedule \"always\"\n        set service \"ALL\"\n");
            sb.Append("    next\n");
        }

        sb.Append("end\n");
        return sb.ToString();
    }

    [Fact]
    public void LargeConfig_ParsesAndRoundTripsQuickly()
    {
        var text = LargeConfig();
        var lines = text.Count(c => c == '\n');
        Assert.True(lines > 50_000, $"fixture has only {lines} lines");

        var sw = Stopwatch.StartNew();
        var doc = ConfigParser.Parse(text);
        var output = doc.ToText(text.Length);
        sw.Stop();

        Assert.Equal(text, output);
        Assert.Empty(doc.Diagnostics);
        // Generous bound for slow CI machines; typical time is well below one second.
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(5), $"parse + serialize took {sw.Elapsed}");
    }
}
