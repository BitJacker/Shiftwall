using System.Windows.Data;
using System.Windows.Markup;
using Shiftwall.Presentation.Localization;

namespace Shiftwall.App.Localization;

/// <summary>XAML: <c>Text="{l:T Wizard.Next}"</c>. Updates live when the language changes.</summary>
[MarkupExtensionReturnType(typeof(object))]
public sealed class TExtension(string key) : MarkupExtension
{
    public TExtension()
        : this("")
    {
    }

    [ConstructorArgument("key")]
    public string Key { get; set; } = key;

    public override object ProvideValue(IServiceProvider serviceProvider) =>
        new Binding($"[{Key}]") { Source = Loc.Instance, Mode = BindingMode.OneWay }.ProvideValue(serviceProvider);
}
