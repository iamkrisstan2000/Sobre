using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Sobre.Services;
using Sobre.ViewModels;

namespace Sobre.Views
{
    public partial class LockScreen : UserControl
    {
        private int _failedAttempts;
        private DispatcherTimer? _cooldown;

        public LockScreen()
        {
            InitializeComponent();

            IsVisibleChanged += (_, _) =>
            {
                if (!IsVisible) return;
                _failedAttempts = 0;
                ErrorText.Visibility = Visibility.Collapsed;
                PasswordInput.Clear();
                PasswordInput.IsEnabled = true;
                UnlockButton.IsEnabled = true;
                HintText.Text = "Enter your password to open your mail.";
                // Focus once the overlay is actually laid out.
                Dispatcher.BeginInvoke(new Action(() => PasswordInput.Focus()),
                    DispatcherPriority.Input);
            };
        }

        private ShellViewModel? Vm => (Application.Current as App)?.Shell;

        private void OnPasswordKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter) { Attempt(); e.Handled = true; }
        }

        private void OnUnlock(object sender, RoutedEventArgs e) => Attempt();

        private void OnQuit(object sender, RoutedEventArgs e) => Application.Current.Shutdown();

        private void Attempt()
        {
            var vm = Vm;
            if (vm == null) return;

            var settings = vm.Settings;
            bool ok = AppLock.Verify(PasswordInput.Password, settings.LockHash, settings.LockSalt,
                settings.LockIterations);

            if (ok)
            {
                PasswordInput.Clear();
                vm.IsLocked = false;
                ErrorText.Visibility = Visibility.Collapsed;
                return;
            }

            _failedAttempts++;
            PasswordInput.Clear();
            ErrorText.Text = _failedAttempts >= 3
                ? $"Incorrect password ({_failedAttempts} attempts)."
                : "Incorrect password. Try again.";
            ErrorText.Visibility = Visibility.Visible;

            // Gentle backoff after repeated failures (no lockout that could lose data).
            if (_failedAttempts >= 3)
            {
                int seconds = Math.Min(30, (_failedAttempts - 2) * 5);
                PasswordInput.IsEnabled = false;
                UnlockButton.IsEnabled = false;
                int remaining = seconds;
                HintText.Text = $"Wait {remaining}s before trying again.";

                _cooldown?.Stop();
                _cooldown = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
                _cooldown.Tick += (_, _) =>
                {
                    remaining--;
                    if (remaining > 0)
                    {
                        HintText.Text = $"Wait {remaining}s before trying again.";
                        return;
                    }
                    _cooldown!.Stop();
                    PasswordInput.IsEnabled = true;
                    UnlockButton.IsEnabled = true;
                    HintText.Text = "Enter your password to open your mail.";
                    PasswordInput.Focus();
                };
                _cooldown.Start();
            }
            else
            {
                PasswordInput.Focus();
            }
        }
    }
}
