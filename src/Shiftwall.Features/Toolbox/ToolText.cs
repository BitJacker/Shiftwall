using System.Globalization;
using System.Resources;

namespace Shiftwall.Features.Toolbox;

/// <summary>Localized texts of the toolbox (Resources/ToolboxMessages(.it).resx).</summary>
public static class ToolText
{
    private static readonly ResourceManager Resources = new("Shiftwall.Features.Resources.ToolboxMessages", typeof(ToolText).Assembly);

    public static string Get(string key, CultureInfo? culture = null) =>
        Resources.GetString(key, culture ?? CultureInfo.CurrentUICulture) ?? key;

    public static string Format(string key, IReadOnlyList<string> args, CultureInfo? culture = null)
    {
        culture ??= CultureInfo.CurrentUICulture;
        var template = Get(key, culture);
        return args.Count == 0 ? template : string.Format(culture, template, [.. args.Cast<object>()]);
    }

    /// <summary>All keys of the neutral resource file (tests check the Italian translation).</summary>
    internal static IEnumerable<string> Keys()
    {
        using var set = Resources.GetResourceSet(CultureInfo.InvariantCulture, createIfNotExists: true, tryParents: false)!;
        foreach (System.Collections.DictionaryEntry e in set) yield return (string)e.Key;
    }
}
