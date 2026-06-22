using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ExportAzureWiki.Wpf.ViewModels;

namespace ExportAzureWiki.Wpf.Views;

public partial class LoginView : UserControl
{
    private bool _showPassword;

    public LoginView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        UpdatePasswordVisibility();
    }

    private void TxtPassword_OnPasswordChanged(object sender, RoutedEventArgs e)
    {
        SyncPasswordToViewModel();
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is LoginViewModel oldVm)
        {
            oldVm.LoginSucceeded -= OnLoginSucceeded;
        }

        if (e.NewValue is LoginViewModel newVm)
        {
            newVm.LoginSucceeded += OnLoginSucceeded;
        }
    }

    private void OnLoginSucceeded(object? sender, EventArgs e)
    {
        txtPassword.Password = string.Empty;
        txtPasswordVisible.Text = string.Empty;
        _showPassword = false;
        UpdatePasswordVisibility();
    }

    private void BtnTogglePasswordVisibility_OnClick(object sender, RoutedEventArgs e)
    {
        _showPassword = !_showPassword;
        if (_showPassword)
        {
            txtPasswordVisible.Text = txtPassword.Password;
        }
        else
        {
            txtPassword.Password = txtPasswordVisible.Text;
        }

        UpdatePasswordVisibility();
        SyncPasswordToViewModel();
    }

    private void TxtPasswordVisible_OnTextChanged(object sender, TextChangedEventArgs e)
    {
        if (!_showPassword)
        {
            return;
        }

        SyncPasswordToViewModel();
    }

    private void BtnLogin_OnClick(object sender, RoutedEventArgs e)
    {
        SyncPasswordToViewModel();
    }

    private void PasswordControl_OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
        {
            return;
        }

        SyncPasswordToViewModel();
        if (DataContext is LoginViewModel vm && vm.LoginCommand.CanExecute(null))
        {
            vm.LoginCommand.Execute(null);
            e.Handled = true;
        }
    }

    private void UpdatePasswordVisibility()
    {
        txtPassword.Visibility = _showPassword ? Visibility.Collapsed : Visibility.Visible;
        txtPasswordVisible.Visibility = _showPassword ? Visibility.Visible : Visibility.Collapsed;
        btnTogglePasswordVisibility.Content = _showPassword ? "\uE8F4" : "\uE890";
    }

    private void SyncPasswordToViewModel()
    {
        if (DataContext is not LoginViewModel vm)
        {
            return;
        }

        vm.Password = _showPassword ? txtPasswordVisible.Text : txtPassword.Password;
    }
}

