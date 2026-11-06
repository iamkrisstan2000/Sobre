using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Sobre.Models;
using Sobre.Services;
using Sobre.ViewModels;

namespace Sobre.Views
{
    public partial class AddAccountWindow : UserControl
    {
        private sealed class ProviderVm
        {
            public ProviderPreset Preset { get; init; } = new();
            public bool IsSelected { get; set; }
            public string Name => Preset.Name;
            public string Description => Preset.Description;
            public string AccentHex => Preset.AccentHex;
            public string AvatarText => Preset.AvatarText ?? "?";
        }

        private readonly List<ProviderVm> _providers = new();
        private ProviderVm? _selected;
        private bool _advanced;

        public AddAccountWindow()
        {
            InitializeComponent();

            foreach (var p in ProviderPreset.All)
                _providers.Add(new ProviderVm { Preset = p });

            ProviderList.ItemsSource = _providers;

            var shell = Application.Current is App app ? app.Shell : null;
            var preferred = shell?.Settings.DefaultProvider ?? "gmail";
            SelectProvider(_providers.FirstOrDefault(x => x.Preset.Id == preferred) ?? _providers[0]);

            IsVisibleChanged += (_, _) =>
            {
                if (IsVisible)
                {
                    DisplayNameBox.Text = "";
                    EmailBox.Text = "";
                    PasswordBox.Clear();
                    _advanced = false;
                    AdvancedPanel.Visibility = Visibility.Collapsed;
                    AdvancedLabel.Text = "Show";
                    SelectProvider(_providers.FirstOrDefault(x => x.Preset.Id == (_selected?.Preset.Id ?? "gmail")) ?? _providers[0]);
                }
            };
        }

        private ShellViewModel? Shell => (Application.Current as App)?.Shell;

        private void OnProviderClick(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.Tag is ProviderVm vm) SelectProvider(vm);
        }

        private void SelectProvider(ProviderVm vm)
        {
            _selected = vm;
            foreach (var p in _providers) p.IsSelected = ReferenceEquals(p, vm);
            ProviderList.Items.Refresh();

            ProviderTitle.Text = vm.Name;

            if (!string.IsNullOrWhiteSpace(vm.Preset.Note))
            {
                GuidanceText.Text = vm.Preset.Note;
                GuidanceBox.Visibility = Visibility.Visible;
            }
            else
            {
                GuidanceBox.Visibility = Visibility.Collapsed;
            }
            // Show the official generator link when the preset has one (Gmail etc)
            if (!string.IsNullOrWhiteSpace(vm.Preset.HelpUrl))
            {
                try { GuidanceHyperlink.NavigateUri = new Uri(vm.Preset.HelpUrl); } catch { }
                GuidanceLinkText.Text = vm.Preset.Id == "gmail"
                    ? "Open Google App Passwords →"
                    : $"Open {vm.Preset.Name} help →";
                GuidanceLink.Visibility = Visibility.Visible;
            }
            else
            {
                GuidanceLink.Visibility = Visibility.Collapsed;
            }
            if (!string.IsNullOrEmpty(vm.Preset.ImapHost)) ImapHostBox.Text = vm.Preset.ImapHost;
            if (!string.IsNullOrEmpty(vm.Preset.SmtpHost)) SmtpHostBox.Text = vm.Preset.SmtpHost;
            ImapPortBox.Text = vm.Preset.ImapPort.ToString();
            SmtpPortBox.Text = vm.Preset.SmtpPort.ToString();
            SelectSecurity(ImapSecurityBox, vm.Preset.ImapSecurity);
            SelectSecurity(SmtpSecurityBox, vm.Preset.SmtpSecurity);

            // Pre-made providers hide the server fields; custom shows them.
            bool custom = vm.Preset.Id == "custom";
            if (custom) SetAdvanced(true);

            ShowResult(null);
        }

        private static void SelectSecurity(ComboBox box, MailSecurity security)
        {
            foreach (var item in box.Items.OfType<ComboBoxItem>())
            {
                if (string.Equals(item.Tag?.ToString(), security.ToString(), StringComparison.OrdinalIgnoreCase))
                {
                    box.SelectedItem = item;
                    return;
                }
            }
            box.SelectedIndex = 0;
        }

        private static MailSecurity ReadSecurity(ComboBox box) =>
            Enum.TryParse<MailSecurity>(box.SelectedItem is ComboBoxItem i ? i.Tag?.ToString() : null, out var v) ? v : MailSecurity.SslOnConnect;

        private void SetAdvanced(bool on)
        {
            _advanced = on;
            AdvancedPanel.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
            AdvancedLabel.Text = on ? "Hide" : "Show";
        }

        private void OnToggleAdvanced(object sender, RoutedEventArgs e) => SetAdvanced(!_advanced);

        private void OnFieldChanged(object sender, TextChangedEventArgs e)
        {
            // Suggest a display name from the address while typing.
            if (string.IsNullOrWhiteSpace(DisplayNameBox.Text) && EmailBox.Text.Contains('@'))
                DisplayNameBox.Text = EmailBox.Text.Split('@')[0];
        }

        private void OnEmailChanged(object sender, TextChangedEventArgs e)
        {
            var email = EmailBox.Text.Trim();
            var domain = email.Contains('@') ? email.Split('@').Last().ToLowerInvariant() : "";
            if (string.IsNullOrEmpty(domain)) return;
            if (_selected == null) return;
            if (_selected.Preset.Id == "custom") return;

            var match = _providers.FirstOrDefault(p => p.Preset.Domains.Contains(domain));
            if (match != null && !ReferenceEquals(match, _selected)) SelectProvider(match);

            if (string.IsNullOrWhiteSpace(UserNameBox.Text)) UserNameBox.Text = email;
        }

        private AccountRecord BuildAccount()
        {
            var preset = _selected?.Preset ?? ProviderPreset.All[^1];
            var email = EmailBox.Text.Trim();
            var user = string.IsNullOrWhiteSpace(UserNameBox.Text) ? email : UserNameBox.Text.Trim();
            var name = string.IsNullOrWhiteSpace(DisplayNameBox.Text)
                ? (email.Contains('@') ? email.Split('@')[0] : "Account")
                : DisplayNameBox.Text.Trim();

            var account = new AccountRecord
            {
                Id = Guid.NewGuid().ToString("N"),
                DisplayName = name,
                Email = email,
                UserName = user,
                ProviderId = preset.Id,
                ImapHost = ImapHostBox.Text.Trim(),
                ImapPort = int.TryParse(ImapPortBox.Text.Trim(), out var ip) ? ip : preset.ImapPort,
                ImapSecurity = ReadSecurity(ImapSecurityBox),
                SmtpHost = SmtpHostBox.Text.Trim(),
                SmtpPort = int.TryParse(SmtpPortBox.Text.Trim(), out var sp) ? sp : preset.SmtpPort,
                SmtpSecurity = ReadSecurity(SmtpSecurityBox),
                SmtpRequiresAuth = SmtpAuthBox.IsChecked == true,
                Signature = SignatureBox.Text,
                SignatureEnabled = !string.IsNullOrWhiteSpace(SignatureBox.Text),
                AccentHex = preset.AccentHex,
                AvatarText = string.IsNullOrEmpty(name) ? "?" : name.Substring(0, 1).ToUpperInvariant()
            };
            return account;
        }

        private bool Validate(out string error)
        {
            error = "";
            if (!EmailBox.Text.Contains('@'))
            {
                error = "Enter a complete email address.";
                return false;
            }
            if (PasswordBox.Password.Length == 0)
            {
                error = "Enter your password or app password.";
                return false;
            }
            if (string.IsNullOrWhiteSpace(ImapHostBox.Text) || string.IsNullOrWhiteSpace(SmtpHostBox.Text))
            {
                error = "IMAP and SMTP host names are required.";
                SetAdvanced(true);
                return false;
            }
            return true;
        }

        private void ShowResult(MailResult? result)
        {
            if (result == null) { ResultText.Text = ""; return; }
            ResultText.Text = result.Message;
            ResultText.Foreground = result.Ok
                ? ThemeService.BrushOrDefault("SuccessFill", Brushes.Green)
                : ThemeService.BrushOrDefault("ErrorFill", Brushes.IndianRed);
        }

        private async void OnTest(object sender, RoutedEventArgs e)
        {
            if (!Validate(out var err)) { ShowResult(MailResult.Fail(err)); return; }
            TestButton.IsEnabled = false;
            SaveButton.IsEnabled = false;
            ResultText.Text = "Contacting the mail server…";
            ResultText.Foreground = ThemeService.BrushOrDefault("TextSecondary", Brushes.Gray);
            try
            {
                var account = BuildAccount();
                var result = await MailService.TestConnectionAsync(account, PasswordBox.Password);
                ShowResult(result);
            }
            finally
            {
                TestButton.IsEnabled = true;
                SaveButton.IsEnabled = true;
            }
        }

        private async void OnSave(object sender, RoutedEventArgs e)
        {
            if (!Validate(out var err)) { ShowResult(MailResult.Fail(err)); return; }
            if (Shell == null) return;

            SaveButton.IsEnabled = false;
            TestButton.IsEnabled = false;
            ResultText.Text = "Verifying with the server…";
            ResultText.Foreground = ThemeService.BrushOrDefault("TextSecondary", Brushes.Gray);
            try
            {
                var account = BuildAccount();
                var result = await MailService.TestConnectionAsync(account, PasswordBox.Password);

                if (!result.Ok)
                {
                    // Offer to save anyway so a temporarily offline server doesn't block setup.
                    var keep = MessageBox.Show(
                        "The connection test failed:\n\n" + result.Message +
                        "\n\nSave this account anyway? Sobre will keep trying to connect when it next syncs.",
                        "Sobre", MessageBoxButton.YesNo, MessageBoxImage.Warning);
                    if (keep != MessageBoxResult.Yes)
                    {
                        ShowResult(result);
                        return;
                    }
                }

                Shell.CompleteAddAccount(account, PasswordBox.Password, makeCurrent: true);
                ShowResult(MailResult.Success("Account added."));
            }
            finally
            {
                SaveButton.IsEnabled = true;
                TestButton.IsEnabled = true;
            }
        }

        private void OnHelpNavigate(object sender, System.Windows.Navigation.RequestNavigateEventArgs e)
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
            }
            catch { }
            e.Handled = true;
        }

        private void OnClose(object sender, RoutedEventArgs e)
        {
            if (Shell != null) Shell.IsAddAccountOpen = false;
        }
    }
}
