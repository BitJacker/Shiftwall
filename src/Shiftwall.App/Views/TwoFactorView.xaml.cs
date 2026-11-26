using System.Windows;
using System.Windows.Controls;
using Shiftwall.Presentation.ViewModels.Toolbox;

namespace Shiftwall.App.Views;

/// <summary>Password boxes cannot be bound; the token and the file password go to the view model only, in memory.</summary>
public partial class TwoFactorView : UserControl
{
    public TwoFactorView()
    {
        InitializeComponent();
        DataContextChanged += (_, e) =>
        {
            if (e.OldValue is TwoFactorViewModel old) old.VaultCleared -= OnVaultCleared;
            if (e.NewValue is TwoFactorViewModel vm) vm.VaultCleared += OnVaultCleared;
        };
    }

    private void OnVaultCleared(object? sender, EventArgs e)
    {
        Vault.Clear();
        VaultConfirm.Clear();
    }

    private void OnTokenChanged(object sender, RoutedEventArgs e)
    {
        if (DataContext is TwoFactorViewModel vm) vm.SetToken(Token.Password);
    }

    private void OnVaultChanged(object sender, RoutedEventArgs e)
    {
        if (DataContext is TwoFactorViewModel vm) vm.SetVaultPassword(Vault.Password, VaultConfirm.Password);
    }
}
