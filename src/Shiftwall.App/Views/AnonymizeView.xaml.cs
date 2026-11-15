using System.Windows;
using System.Windows.Controls;
using Shiftwall.Presentation.ViewModels.Toolbox;

namespace Shiftwall.App.Views;

/// <summary>Password boxes cannot be bound; their values go to the view model only, in memory.</summary>
public partial class AnonymizeView : UserControl
{
    public AnonymizeView() => InitializeComponent();

    private void OnPasswordChanged(object sender, RoutedEventArgs e)
    {
        if (DataContext is AnonymizeViewModel vm) vm.SetPassword(Password.Password, PasswordConfirm.Password);
    }

    private void OnRestorePasswordChanged(object sender, RoutedEventArgs e)
    {
        if (DataContext is AnonymizeViewModel vm) vm.SetPassword(RestorePassword.Password, RestorePassword.Password);
    }
}
