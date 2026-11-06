using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using Sobre.Services;
using Sobre.ViewModels;

namespace Sobre.Views
{
    public partial class SettingsPane : UserControl
    {
        private bool _loading;

        public SettingsPane()
        {
            InitializeComponent();

            IsVisibleChanged += (_, _) =>
            {
                if (!IsVisible) return;
                SyncLockUi();
                NewPasswordBox.Clear();
                ConfirmPasswordBox.Clear();
                LockError.Visibility = Visibility.Collapsed;
                StrengthText.Text = "";
            };
        }

        private ShellViewModel? Vm => (Application.Current as App)?.Shell;

        private void OnAppearanceChanged(object sender, RoutedEventArgs e)
        {
            if (Application.Current is App app) app.Shell.ApplyThemeFromSettings();
        }

        // ---------------- app lock ----------------

        private void SyncLockUi()
        {
            var vm = Vm;
            if (vm == null) return;

            _loading = true;
            LockToggle.IsChecked = vm.Settings.LockEnabled;

            bool hasPassword = vm.Settings.LockEnabled;
            LockForm.Visibility = Visibility.Collapsed;
            LockOnHint.Visibility = hasPassword ? Visibility.Visible : Visibility.Collapsed;
            ChangeLockButton.Visibility = hasPassword ? Visibility.Visible : Visibility.Collapsed;
            RemoveLockButton.Visibility = hasPassword ? Visibility.Visible : Visibility.Collapsed;
            SaveLockButton.Content = "Set password";
            LockError.Visibility = Visibility.Collapsed;
            _loading = false;
        }

        private void OnLockToggleChanged(object sender, RoutedEventArgs e)
        {
            if (_loading) return;
            var vm = Vm;
            if (vm == null) return;

            if (LockToggle.IsChecked == true)
            {
                // Turning on: reveal the form and ask for a password.
                LockForm.Visibility = Visibility.Visible;
                ChangeLockButton.Visibility = Visibility.Collapsed;
                NewPasswordBox.Focus();
            }
            else
            {
                // Turning off: require confirmation that the password is removed.
                var answer = MessageBox.Show(
                    "Turn off the app lock?\n\nSobre will open without asking for a password.",
                    "Sobre", MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (answer != MessageBoxResult.Yes)
                {
                    _loading = true;
                    LockToggle.IsChecked = true;
                    _loading = false;
                    return;
                }
                vm.DisableLock();
                SyncLockUi();
            }
        }

        private void OnShowLockForm(object sender, RoutedEventArgs e)
        {
            LockForm.Visibility = Visibility.Visible;
            LockError.Visibility = Visibility.Collapsed;
            NewPasswordBox.Clear();
            ConfirmPasswordBox.Clear();
            NewPasswordBox.Focus();
        }

        private void OnNewPasswordChanged(object sender, RoutedEventArgs e)
        {
            StrengthText.Text = AppLock.Strength(NewPasswordBox.Password);
        }

        private void OnSaveLock(object sender, RoutedEventArgs e)
        {
            var vm = Vm;
            if (vm == null) return;

            var pw = NewPasswordBox.Password;
            var confirm = ConfirmPasswordBox.Password;

            if (pw.Length < 4)
            {
                ShowError("Use at least 4 characters. Eight or more is much safer.");
                return;
            }
            if (pw != confirm)
            {
                ShowError("The two passwords don't match.");
                return;
            }

            vm.EnableLock(pw);
            NewPasswordBox.Clear();
            ConfirmPasswordBox.Clear();
            StrengthText.Text = "";
            LockError.Visibility = Visibility.Collapsed;
            SyncLockUi();

            // Confirm the lock is genuinely armed — never leave the user thinking it is when it isn't.
            if (!vm.Settings.LockEnabled || string.IsNullOrEmpty(vm.Settings.LockHash))
                ShowError("Sobre could not save the password. Check that %LOCALAPPDATA%\\Sobre is writable.");
            else
                StrengthText.Text = "App lock is on. Sobre will ask for this password next time it opens.";
        }

        private void OnRemoveLock(object sender, RoutedEventArgs e)
        {
            var vm = Vm;
            if (vm == null) return;

            var answer = MessageBox.Show(
                "Remove the app lock?\n\nSobre will open without asking for a password.",
                "Sobre", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (answer != MessageBoxResult.Yes) return;

            vm.DisableLock();
            SyncLockUi();
        }

        private void OnLockNow(object sender, RoutedEventArgs e)
        {
            var vm = Vm;
            if (vm == null) return;
            if (!vm.Settings.LockEnabled)
            {
                ShowError("Set a password first, then you can lock Sobre at any time.");
                LockForm.Visibility = Visibility.Visible;
                NewPasswordBox.Focus();
                return;
            }
            vm.IsSettingsOpen = false;
            vm.LockNow();
        }

        private void ShowError(string message)
        {
            LockError.Text = message;
            LockError.Visibility = Visibility.Visible;
        }

        private void OnAboutIconClicked(object sender, RoutedEventArgs e)
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "https://github.com/iamkrisstan2000/Sobre",
                    UseShellExecute = true
                });
            }
            catch { }
        }
    }
}
