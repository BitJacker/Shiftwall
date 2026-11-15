using System.ComponentModel;
using System.Globalization;
using System.Resources;

namespace Shiftwall.Presentation.Localization;

/// <summary>
/// UI strings (Strings.resx, Strings.it.resx). XAML binds to <c>Loc.Instance[Key]</c>; changing
/// <see cref="Culture"/> refreshes every binding without restarting.
/// </summary>
public sealed class Loc : INotifyPropertyChanged
{
    private static readonly ResourceManager Resources = new("Shiftwall.Presentation.Localization.Strings", typeof(Loc).Assembly);

    public static Loc Instance { get; } = new();

    public static IReadOnlyList<CultureInfo> SupportedCultures { get; } = [CultureInfo.GetCultureInfo("it-IT"), CultureInfo.GetCultureInfo("en")];

    private CultureInfo _culture = CultureInfo.GetCultureInfo("it-IT");

    public event PropertyChangedEventHandler? PropertyChanged;

    public CultureInfo Culture
    {
        get => _culture;
        set
        {
            if (Equals(_culture, value)) return;
            _culture = value;
            CultureInfo.CurrentUICulture = value;
            CultureInfo.DefaultThreadCurrentUICulture = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Culture)));
        }
    }

    public string this[string key] => Resources.GetString(key, _culture) ?? $"[{key}]";

    public static string T(string key) => Instance[key];

    public static string F(string key, params object[] args) => string.Format(Instance._culture, Instance[key], args);

    /// <summary>All keys of the neutral resource file (used by tests to check the Italian translation).</summary>
    internal static IEnumerable<string> Keys()
    {
        using var set = Resources.GetResourceSet(CultureInfo.InvariantCulture, createIfNotExists: true, tryParents: false)!;
        foreach (System.Collections.DictionaryEntry e in set) yield return (string)e.Key;
    }
}
