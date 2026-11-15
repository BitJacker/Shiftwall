using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using Shiftwall.Features.Migration;
using Shiftwall.Presentation.Localization;
using Shiftwall.Presentation.ViewModels;

namespace Shiftwall.App;

public sealed class BoolToVisibilityConverter : IValueConverter
{
    public bool Invert { get; set; }

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        (value is true) ^ Invert ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}

public sealed class NullToVisibilityConverter : IValueConverter
{
    public bool Invert { get; set; }

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        (value is null or "") ^ Invert ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>Severity → brush. Colours are mid-tone so they read on light and dark themes.</summary>
public sealed class SeverityToBrushConverter : IValueConverter
{
    private static readonly Brush Error = Freeze(new SolidColorBrush(Color.FromRgb(0xD1, 0x34, 0x38)));
    private static readonly Brush Warning = Freeze(new SolidColorBrush(Color.FromRgb(0xC2, 0x7C, 0x0E)));
    private static readonly Brush Info = Freeze(new SolidColorBrush(Color.FromRgb(0x0F, 0x8A, 0x9A)));

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value switch
    {
        ItemSeverity.Error => Error,
        ItemSeverity.Warning => Warning,
        _ => Info,
    };

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();

    internal static Brush Freeze(Brush b)
    {
        b.Freeze();
        return b;
    }
}

public sealed class SeverityToGlyphConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value switch
    {
        ItemSeverity.Error => "",   // ErrorBadge
        ItemSeverity.Warning => "", // Warning
        _ => "",                    // Info
    };

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>Diff kind → translucent background that works on both themes.</summary>
public sealed class DiffKindToBrushConverter : IValueConverter
{
    private static readonly Brush Added = SeverityToBrushConverter.Freeze(new SolidColorBrush(Color.FromArgb(0x40, 0x22, 0xA0, 0x6B)));
    private static readonly Brush Removed = SeverityToBrushConverter.Freeze(new SolidColorBrush(Color.FromArgb(0x40, 0xD1, 0x34, 0x38)));
    private static readonly Brush Gap = SeverityToBrushConverter.Freeze(new SolidColorBrush(Color.FromArgb(0x18, 0x80, 0x80, 0x80)));

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value switch
    {
        DiffLineKind.Added => Added,
        DiffLineKind.Removed => Removed,
        DiffLineKind.Gap => Gap,
        _ => Brushes.Transparent,
    };

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>Step index → state of a stepper item: "done", "current" or "todo" compared with ConverterParameter.</summary>
public sealed class StepStateConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var current = value is int i ? i : 0;
        var index = int.Parse((string)parameter!, CultureInfo.InvariantCulture);
        return index < current ? "done" : index == current ? "current" : "todo";
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>Localization key → text (for keys stored in view models, e.g. menu titles).</summary>
public sealed class LocKeyConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object? parameter, CultureInfo culture) =>
        values.Length > 0 && values[0] is string key ? Loc.T(key) : "";

    public object[] ConvertBack(object value, Type[] targetTypes, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}

public sealed class ThemeToTextConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object? parameter, CultureInfo culture) =>
        values.Length > 0 && values[0] is Presentation.Services.ThemeChoice t ? Loc.T("Theme." + t) : "";

    public object[] ConvertBack(object value, Type[] targetTypes, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}
