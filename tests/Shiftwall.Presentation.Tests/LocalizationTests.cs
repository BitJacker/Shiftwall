using System.Globalization;
using System.Resources;
using System.Text.RegularExpressions;
using Shiftwall.Presentation.Localization;

namespace Shiftwall.Presentation.Tests;

public sealed partial class LocalizationTests
{
    [GeneratedRegex(@"\{l:T ([A-Za-z0-9.]+)\}")]
    private static partial Regex XamlKey();

    [GeneratedRegex(@"Loc\.(?:T|F)\(""([A-Za-z0-9.]+)""\s*[,)]")]
    private static partial Regex CodeKey();

    [GeneratedRegex(@"""(Nav\.[A-Za-z]+)""")]
    private static partial Regex NavKey();

    private static string Src()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d is not null && !File.Exists(Path.Combine(d.FullName, "global.json"))) d = d.Parent;
        return Path.Combine(d!.FullName, "src");
    }

    public static TheoryData<string> UsedKeys()
    {
        var keys = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var f in Directory.EnumerateFiles(Path.Combine(Src(), "Shiftwall.App"), "*.xaml", SearchOption.AllDirectories))
        {
            foreach (Match m in XamlKey().Matches(File.ReadAllText(f))) keys.Add(m.Groups[1].Value);
        }

        foreach (var dir in new[] { "Shiftwall.App", "Shiftwall.Presentation" })
        {
            foreach (var f in Directory.EnumerateFiles(Path.Combine(Src(), dir), "*.cs", SearchOption.AllDirectories).Where(f => !f.Contains("/obj/", StringComparison.Ordinal)))
            {
                var text = File.ReadAllText(f);
                foreach (Match m in CodeKey().Matches(text)) keys.Add(m.Groups[1].Value);
                foreach (Match m in NavKey().Matches(text)) keys.Add(m.Groups[1].Value);
            }
        }

        foreach (var k in new[] { "Theme.System", "Theme.Light", "Theme.Dark", "Source.Summary.Kind.BinaryOrEncrypted", "Source.Summary.Kind.Yaml", "Source.Summary.Kind.Empty" })
        {
            keys.Add(k);
        }

        // Keys built from enum values at run time.
        foreach (var v in Enum.GetNames<Shiftwall.Features.Toolbox.SectionChangeKind>()) keys.Add("Compare.Kind." + v);
        foreach (var v in Enum.GetNames<Shiftwall.Lan.LanOutcome>()) keys.Add("Lan.Outcome." + v);
        foreach (var v in Enum.GetNames<Shiftwall.Features.Toolbox.Vpn.VpnCheckResult>()) keys.Add("Vpn.Result." + v);
        foreach (var v in Enum.GetNames<Shiftwall.Features.Toolbox.Import.ImportKind>()) keys.Add("Import.Kind." + v);
        foreach (var v in Enum.GetNames<Shiftwall.Presentation.ViewModels.Toolbox.SnippetKind>()) keys.Add("Snippets.Kind." + v);
        return [.. keys];
    }

    [Theory]
    [MemberData(nameof(UsedKeys))]
    public void EveryUsedKey_HasItalianAndEnglishText(string key)
    {
        var rm = new ResourceManager("Shiftwall.Presentation.Localization.Strings", typeof(Loc).Assembly);
        var en = rm.GetString(key, CultureInfo.GetCultureInfo("en"));
        var it = rm.GetString(key, CultureInfo.GetCultureInfo("it-IT"));
        Assert.False(string.IsNullOrWhiteSpace(en), $"missing English text for {key}");
        Assert.False(string.IsNullOrWhiteSpace(it), $"missing Italian text for {key}");
        Assert.NotNull(rm.GetResourceSet(CultureInfo.GetCultureInfo("it"), true, false)?.GetString(key));
    }

    [Fact]
    public void PrivacyStatementAndDisclaimer_AreVerbatim()
    {
        Loc.Instance.Culture = CultureInfo.GetCultureInfo("it-IT");
        Assert.Equal("Shiftwall non invia dati. Funziona completamente offline.", Loc.T("About.Privacy.Statement"));
        Assert.Equal("Shiftwall non è affiliato a Fortinet. FortiGate e FortiOS sono marchi di Fortinet, Inc. Le config modificate non sono supportate dal TAC Fortinet: testare sempre in laboratorio prima della produzione.", Loc.T("About.Disclaimer"));
    }
}
