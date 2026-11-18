using System.Globalization;
using System.Resources;

namespace Shiftwall.Core.Config;

/// <summary>Turns a <see cref="ConfigDiagnostic"/> into a localized sentence (English and Italian resources).</summary>
public static class DiagnosticFormatter
{
    private static readonly ResourceManager Resources =
        new("Shiftwall.Core.Resources.DiagnosticMessages", typeof(DiagnosticFormatter).Assembly);

    public static string Format(ConfigDiagnostic diagnostic, CultureInfo? culture = null)
    {
        ArgumentNullException.ThrowIfNull(diagnostic);
        culture ??= CultureInfo.CurrentUICulture;
        var template = Resources.GetString(diagnostic.Code, culture) ?? diagnostic.Code;

        IEnumerable<string> args = diagnostic.Arguments;
        if (diagnostic.Code == DiagnosticCodes.PartialConfig)
        {
            args = [string.Join(", ", diagnostic.Arguments.Select(a => Resources.GetString("Partial." + a, culture) ?? a))];
        }
        else if (diagnostic.Code == DiagnosticCodes.MultiVdom)
        {
            args = [string.Join(", ", diagnostic.Arguments)];
        }

        var text = template.Replace("{line}", diagnostic.Line?.ToString(culture) ?? "?", StringComparison.Ordinal);
        var argArray = args.Cast<object>().ToArray();
        return argArray.Length == 0 ? text : string.Format(culture, text, argArray);
    }
}
