using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using Sobre.Models;
using Sobre.Services;

namespace Sobre.ViewModels
{
    public sealed partial class ShellViewModel
    {
        private RelayCommand? _composeNewCommand;
        private RelayCommand? _replyCommand;
        private RelayCommand? _replyAllCommand;
        private RelayCommand? _forwardCommand;
        private RelayCommand? _flagCommand;
        private AsyncRelayCommand? _archiveCommand;
        private AsyncRelayCommand? _deleteCommand;
        private AsyncRelayCommand? _toggleUnreadCommand;
        private ICommand? _selectFolderCommand;
        private ICommand? _switchAccountCommand;
        private RelayCommand? _openSettingsCommand;
        private RelayCommand? _addAccountCommand;
        private AsyncRelayCommand? _syncCommand;
        private RelayCommand? _clearSearchCommand;
        private RelayCommand? _toggleUnreadOnlyCommand;
        private RelayCommand? _toggleFlaggedOnlyCommand;
        private AsyncRelayCommand? _sendCommand;
        private RelayCommand? _closeComposeCommand;
        private RelayCommand? _toggleComposeCcCommand;
        private RelayCommand? _attachCommand;
        private ICommand? _removeAccountCommand;
        private ICommand? _setThemeCommand;
        private ICommand? _saveSettingsCommand;

        public ICommand ComposeNewCommand => _composeNewCommand ??= new RelayCommand(ComposeNew);

        public ICommand ReplyCommand => _replyCommand ??= new RelayCommand(() => ComposeReply(SelectedMessage));

        public ICommand ReplyAllCommand => _replyAllCommand ??= new RelayCommand(() => ComposeReply(SelectedMessage, all: true));

        public ICommand ForwardCommand => _forwardCommand ??= new RelayCommand(() => ComposeForward(SelectedMessage));

        public ICommand FlagCommand => _flagCommand ??= new RelayCommand(async () => await ToggleFlagAsync(SelectedMessage));

        public ICommand ArchiveCommand => _archiveCommand ??= new AsyncRelayCommand(async () => await ArchiveMessageAsync(SelectedMessage));

        public ICommand DeleteCommand => _deleteCommand ??= new AsyncRelayCommand(async () => await DeleteMessageAsync(SelectedMessage));

        public ICommand ToggleUnreadCommand => _toggleUnreadCommand ??= new AsyncRelayCommand(async () => await ToggleReadAsync(SelectedMessage));

        public ICommand SelectFolderCommand => _selectFolderCommand ??= new RelayCommand<MailFolder>(f => SelectedFolder = f);

        public ICommand SwitchAccountCommand => _switchAccountCommand ??= new RelayCommand<AccountRecord>(a =>
        {
            if (a == null || a == CurrentAccount) return;
            CurrentAccount = a;
            Settings.ActiveAccountId = a.Id;
            Store.SaveSettings(Settings);
        });

        public ICommand OpenSettingsCommand => _openSettingsCommand ??= new RelayCommand(() => IsSettingsOpen = true);

        public ICommand AddAccountCommand => _addAccountCommand ??= new RelayCommand(() =>
        {
            IsSettingsOpen = false;
            IsAddAccountOpen = true;
        });

        private ICommand? _closeSettingsCommand;
        public ICommand CloseSettingsCommand => _closeSettingsCommand ??= new RelayCommand(() =>
        {
            Store.SaveSettings(Settings);
            ApplyThemeFromSettings();
            IsSettingsOpen = false;
        });

        public ICommand SyncCommand => _syncCommand ??= new AsyncRelayCommand(async () => await SyncAsync());

        public ICommand ClearSearchCommand => _clearSearchCommand ??= new RelayCommand(() => SearchText = "");

        public ICommand ToggleUnreadOnlyCommand => _toggleUnreadOnlyCommand ??= new RelayCommand(() =>
        {
            UnreadOnly = !UnreadOnly;
            PushToast(UnreadOnly ? "Showing unread only" : "Showing all messages", "", ToastKind.Info);
        });

        public ICommand ToggleFlaggedOnlyCommand => _toggleFlaggedOnlyCommand ??= new RelayCommand(() =>
        {
            FlaggedOnly = !FlaggedOnly;
            PushToast(FlaggedOnly ? "Showing flagged only" : "Showing all messages", "", ToastKind.Info);
        });

        public ICommand SendCommand => _sendCommand ??= new AsyncRelayCommand(SendAsync);

        public ICommand CloseComposeCommand => _closeComposeCommand ??= new RelayCommand(() => IsComposeOpen = false);

        public ICommand ToggleComposeCcCommand => _toggleComposeCcCommand ??= new RelayCommand(() => ShowComposeCc = !ShowComposeCc);

        public ICommand AttachCommand => _attachCommand ??= new RelayCommand(AddAttachment);

        public ICommand RemoveAccountCommand => _removeAccountCommand ??= new RelayCommand<AccountRecord>(a => { if (a != null) RemoveAccount(a); });

        public ICommand SetThemeCommand => _setThemeCommand ??= new RelayCommand<string>(SetThemeMode);

        public ICommand SaveSettingsCommand => _saveSettingsCommand ??= new RelayCommand<string>(_ =>
        {
            Store.SaveSettings(Settings);
            ApplyThemeFromSettings();
            PushToast("Settings saved", "Your preferences are stored under %LOCALAPPDATA%\\Sobre.", ToastKind.Success);
        });

        // ---------------- helpers ----------------

        public void SetThemeMode(string? mode)
        {
            Settings.ThemeMode = mode ?? "System";
            ApplyThemeFromSettings();
            Store.SaveSettings(Settings);
            Raise(nameof(ThemeModeLabel));
            Raise(nameof(IsSystemTheme));
            Raise(nameof(IsLightTheme));
            Raise(nameof(IsDarkTheme));
        }

        /// <summary>Bound to the three theme cards so the active one shows as selected.</summary>
        public bool IsSystemTheme => Settings.ThemeMode == "System";
        public bool IsLightTheme => Settings.ThemeMode == "Light";
        public bool IsDarkTheme => Settings.ThemeMode == "Dark";

        public string ThemeModeLabel => Settings.ThemeMode switch
        {
            "Light" => "Light",
            "Dark" => "Dark",
            _ => "Use system setting"
        };

        public void AddAttachment()
        {
            var dlg = new Microsoft.Win32.OpenFileDialog { Multiselect = true, Title = "Attach files" };
            if (dlg.ShowDialog() == true)
            {
                foreach (var f in dlg.FileNames)
                    if (!ComposeAttachments.Contains(f)) ComposeAttachments.Add(f);
            }
        }

        public void RemoveAttachment(string path) => ComposeAttachments.Remove(path);

        /// <summary>Used by the Add Account dialog once credentials verify.</summary>
        public void CompleteAddAccount(AccountRecord account, string password, bool makeCurrent)
        {
            AddAccount(account, password, makeCurrent);
            Settings.DefaultProvider = account.ProviderId;
            Store.SaveSettings(Settings);
            IsAddAccountOpen = false;
        }
    }

}
