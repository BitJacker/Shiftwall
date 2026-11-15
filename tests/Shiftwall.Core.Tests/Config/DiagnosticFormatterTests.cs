using System.Globalization;
using System.Reflection;
using Shiftwall.Core.Config;

namespace Shiftwall.Core.Tests.Config;

public sealed class DiagnosticFormatterTests
{
    private static readonly CultureInfo En = CultureInfo.GetCultureInfo("en");
    private static readonly CultureInfo It = CultureInfo.GetCultureInfo("it-IT");

    public static TheoryData<string> AllCodes() =>
    [
        .. typeof(DiagnosticCodes).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Select(f => (string)f.GetValue(null)!),
    ];

    [Theory]
    [MemberData(nameof(AllCodes))]
    public void EveryCode_HasEnglishAndItalianText(string code)
    {
        var d = new ConfigDiagnostic(code, DiagnosticSeverity.Warning, 12, ["x"]);
        var en = DiagnosticFormatter.Format(d, En);
        var it = DiagnosticFormatter.Format(d, It);
        Assert.NotEqual(code, en);
        Assert.NotEqual(code, it);
        Assert.NotEqual(en, it);
        Assert.DoesNotContain("{", en, StringComparison.Ordinal);
        Assert.DoesNotContain("{", it, StringComparison.Ordinal);
    }

    [Fact]
    public void LineAndArguments_AreSubstituted()
    {
        var d = ConfigDiagnostic.Error(DiagnosticCodes.UnclosedBlock, 42, "firewall address");
        Assert.Equal("Riga 42: il blocco \"config firewall address\" non viene mai chiuso con \"end\". Il file è troncato o incompleto.",
            DiagnosticFormatter.Format(d, It));
    }

    [Fact]
    public void PartialReasons_AreTranslated()
    {
        var d = ConfigDiagnostic.Warning(DiagnosticCodes.PartialConfig, null, "no-header", "no-system-global");
        Assert.Contains("manca l'intestazione, manca \"config system global\"", DiagnosticFormatter.Format(d, It), StringComparison.Ordinal);
    }
}
