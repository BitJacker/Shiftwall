using System.Windows;
using System.Windows.Controls;
using Shiftwall.Presentation.ViewModels.Toolbox;

namespace Shiftwall.App.Views;

/// <summary>The token and backup password stay in the password boxes and the view model memory only.</summary>
public partial class LanView : UserControl
{
    public LanView()
    {
        InitializeComponent();
        DataContextChanged += (_, e) =>
        {
            if (e.OldValue is LanViewModel old) old.SecretsCleared -= OnSecretsCleared;
            if (e.NewValue is LanViewModel vm) vm.SecretsCleared += OnSecretsCleared;
        };
    }

    private void OnSecretChanged(object sender, RoutedEventArgs e)
    {
        if (DataContext is LanViewModel vm) vm.SetSecrets(Token.Password, BackupPassword.Password);
    }

    private void OnSecretsCleared(object? sender, EventArgs e)
    {
        Token.Clear();
        BackupPassword.Clear();
    }
}
