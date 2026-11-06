using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Reflection;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Sobre.Models;
using Sobre.Services;

namespace Sobre.ViewModels
{
    public sealed partial class ShellViewModel : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;
        private void Raise([CallerMemberName] string? n = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
        private bool Set<T>(ref T field, T value, [CallerMemberName] string? n = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value)) return false;
            field = value; Raise(n); return true;
        }

        private readonly DispatcherTimer _clock = new() { Interval = TimeSpan.FromSeconds(30) };
        private readonly DispatcherTimer _syncTimer = new() { Interval = TimeSpan.FromMinutes(5) };
        private readonly Dictionary<string, string> _passwords = new(StringComparer.Ordinal);
        private readonly Dictionary<string, List<MessageSummary>> _cache = new(StringComparer.Ordinal);
        private readonly Dictionary<string, MailMessageDetail> _detailCache = new(StringComparer.Ordinal);

        // ---------------- collections ----------------
        public ObservableCollection<AccountRecord> Accounts { get; } = new();
        public ObservableCollection<MailFolder> Folders { get; } = new();
        public ObservableCollection<MessageSummary> Messages { get; } = new();
        public ObservableCollection<MessageSummary> AllMessages { get; } = new();
        public ObservableCollection<MailAddress> ComposeTo { get; } = new();
        public ObservableCollection<MailAddress> ComposeCc { get; } = new();
        public ObservableCollection<string> ComposeAttachments { get; } = new();
        public ObservableCollection<ToastModel> Toasts { get; } = new();

        public AppSettings Settings { get; private set; } = new();

        // ---------------- account ----------------
        private AccountRecord? _currentAccount;
        public AccountRecord? CurrentAccount
        {
            get => _currentAccount;
            set
            {
                if (!Set(ref _currentAccount, value)) return;
                Raise(nameof(HasAccount));
                Raise(nameof(AccountHeaderInitials));
                Raise(nameof(AccountHeaderName));
                Raise(nameof(AccountHeaderEmail));
                Raise(nameof(AccountColorBrush));
                _ = LoadAccountAsync(value);
            }
        }

        public bool HasAccount => CurrentAccount != null;
        public string AccountHeaderInitials => CurrentAccount?.Initial ?? "?";
        public string AccountHeaderName => CurrentAccount?.DisplayName ?? "No account";
        public string AccountHeaderEmail => CurrentAccount?.Email ?? "";
        public Brush AccountColorBrush => ToBrush(CurrentAccount?.AccentHex ?? "#0078D4");

        // ---------------- folder ----------------
        private MailFolder? _selectedFolder;
        public MailFolder? SelectedFolder
        {
            get => _selectedFolder;
            set
            {
                if (!Set(ref _selectedFolder, value)) return;
                Raise(nameof(FolderTitle));
                Raise(nameof(IsInFolder));
                ApplyFilter();
            }
        }
        public bool IsInFolder => SelectedFolder != null;
        public string FolderTitle => SelectedFolder?.Name ?? "Inbox";

        // ---------------- selection ----------------
        private MessageSummary? _selectedMessage;
        public MessageSummary? SelectedMessage
        {
            get => _selectedMessage;
            set
            {
                if (!Set(ref _selectedMessage, value)) return;
                Raise(nameof(HasSelection));
                Raise(nameof(ShowEmptyReader));
                _ = LoadBodyAsync(value);
            }
        }
        public bool HasSelection => SelectedMessage != null;
        public bool ShowEmptyReader => SelectedMessage == null;

        private MailMessageDetail? _messageDetail;
        public MailMessageDetail? MessageDetail
        {
            get => _messageDetail;
            set
            {
                if (!Set(ref _messageDetail, value)) return;
                Raise(nameof(HasDetail));
                Raise(nameof(ReaderHtml));
                Raise(nameof(DetailToText));
                Raise(nameof(DetailCcText));
                Raise(nameof(HasCc));
                Raise(nameof(HasAttachments));
                Raise(nameof(AttachmentList));
            }
        }
        public bool HasDetail => MessageDetail != null;
        public string ReaderHtml => MessageDetail?.BodyHtml ?? "";
        public string DetailToText => MessageDetail == null ? "" : string.Join(", ", MessageDetail.To.Select(t => t.ToString()));
        public string DetailCcText => MessageDetail == null ? "" : string.Join(", ", MessageDetail.Cc.Select(t => t.ToString()));
        public bool HasCc => MessageDetail != null && MessageDetail.Cc.Count > 0;
        public bool HasAttachments => MessageDetail != null && MessageDetail.Attachments.Count > 0;
        public IReadOnlyList<AttachmentInfo> AttachmentList => MessageDetail?.Attachments ?? Array.Empty<AttachmentInfo>();

        // ---------------- search + filter ----------------
        private string _searchText = "";
        public string SearchText
        {
            get => _searchText;
            set { if (Set(ref _searchText, value)) { Raise(nameof(HasSearch)); ApplyFilter(); } }
        }
        public bool HasSearch => !string.IsNullOrWhiteSpace(SearchText);

        private bool _unreadOnly;
        public bool UnreadOnly { get => _unreadOnly; set { if (Set(ref _unreadOnly, value)) ApplyFilter(); } }

        private bool _flaggedOnly;
        public bool FlaggedOnly { get => _flaggedOnly; set { if (Set(ref _flaggedOnly, value)) ApplyFilter(); } }

        // ---------------- state ----------------
        private bool _isBusy;
        public bool IsBusy { get => _isBusy; set => Set(ref _isBusy, value); }

        private double _syncProgress;
        public double SyncProgress { get => _syncProgress; set => Set(ref _syncProgress, value); }

        private string _statusText = "Ready";
        public string StatusText { get => _statusText; set => Set(ref _statusText, value); }

        private string _connectionState = "Offline preview";
        public string ConnectionState { get => _connectionState; set => Set(ref _connectionState, value); }

        private int _unreadTotal;
        public int UnreadTotal { get => _unreadTotal; set { if (Set(ref _unreadTotal, value)) Raise(nameof(UnreadTotalText)); } }
        public string UnreadTotalText => UnreadTotal == 0 ? "Inbox clear" : $"{UnreadTotal} unread";

        private string _clockText = DateTime.Now.ToString("h:mm tt");
        public string ClockText { get => _clockText; set => Set(ref _clockText, value); }

        private string _dateText = DateTime.Now.ToString("dddd, d MMMM");
        public string DateText { get => _dateText; set => Set(ref _dateText, value); }

        // ---------------- compose ----------------
        private bool _isComposeOpen;
        public bool IsComposeOpen { get => _isComposeOpen; set { if (Set(ref _isComposeOpen, value)) Raise(nameof(ComposeTitle)); } }

        private string _composeSubject = "";
        public string ComposeSubject { get => _composeSubject; set => Set(ref _composeSubject, value); }

        private string _composeToText = "";
        public string ComposeToText { get => _composeToText; set => Set(ref _composeToText, value); }

        private string _composeCcText = "";
        public string ComposeCcText { get => _composeCcText; set => Set(ref _composeCcText, value); }

        private string _composeBody = "";
        public string ComposeBody { get => _composeBody; set => Set(ref _composeBody, value); }

        private bool _showComposeCc;
        public bool ShowComposeCc { get => _showComposeCc; set => Set(ref _showComposeCc, value); }

        private string _composeFrom = "";
        public string ComposeFrom { get => _composeFrom; set => Set(ref _composeFrom, value); }

        public string ComposeTitle => string.IsNullOrWhiteSpace(ComposeSubject) ? "New message" : ComposeSubject;

        // ---------------- settings panel ----------------
        private bool _isSettingsOpen;
        public bool IsSettingsOpen { get => _isSettingsOpen; set => Set(ref _isSettingsOpen, value); }

        // ---------------- app lock ----------------
        private bool _isLocked;
        /// <summary>Fired with true when the app just locked, false when it was unlocked.</summary>
        public event EventHandler<bool>? LockStateChanged;

        public bool IsLocked
        {
            get => _isLocked;
            set
            {
                if (!Set(ref _isLocked, value)) return;
                Raise(nameof(BodyEnabled));
                Raise(nameof(LockOverlayHint));
                LockStateChanged?.Invoke(this, value);
            }
        }

        /// <summary>True while unlocked — used to disable the shell behind the lock.</summary>
        public bool BodyEnabled => !IsLocked;

        public string LockOverlayHint => Settings.LockEnabled
            ? "Enter your password to open your mail."
            : "";

        /// <summary>Hashes and stores the password. Returns false if the settings file could not be written.</summary>
        public bool EnableLock(string password)
        {
            var (hash, salt, iterations) = AppLock.Create(password);
            Settings.LockHash = hash;
            Settings.LockSalt = salt;
            Settings.LockIterations = iterations;
            Settings.LockEnabled = true;

            try
            {
                Store.SaveSettings(Settings);
            }
            catch (Exception ex)
            {
                Settings.LockEnabled = false;
                Settings.LockHash = "";
                Settings.LockSalt = "";
                Settings.LockIterations = 0;
                Log.Error("EnableLock failed", ex);
                return false;
            }

            PushToast("App lock enabled", "Sobre will ask for this password every time it starts.", ToastKind.Success);
            return true;
        }

        public void DisableLock()
        {
            Settings.LockEnabled = false;
            Settings.LockHash = "";
            Settings.LockSalt = "";
            Settings.LockIterations = 0;
            IsLocked = false;
            Store.SaveSettings(Settings);
            PushToast("App lock disabled", "Sobre will open without a password.", ToastKind.Info);
        }

        public void LockNow() => IsLocked = Settings.LockEnabled;

        private bool _isAddAccountOpen;
        public bool IsAddAccountOpen { get => _isAddAccountOpen; set => Set(ref _isAddAccountOpen, value); }

        public string AppVersion =>
            typeof(ShellViewModel).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()
                ?.InformationalVersion.Split('+')[0] ?? "1.0.0";

        private static readonly int[] SyncChoices = { 1, 5, 15, 30 };

        /// <summary>SelectedIndex-friendly view of Settings.SyncIntervalMinutes.</summary>
        public int SyncIntervalIndex
        {
            get
            {
                var i = Array.IndexOf(SyncChoices, Settings.SyncIntervalMinutes);
                return i < 0 ? 1 : i;
            }
            set
            {
                if (value < 0 || value >= SyncChoices.Length) return;
                Settings.SyncIntervalMinutes = SyncChoices[value];
                _syncTimer.Interval = TimeSpan.FromMinutes(SyncChoices[value]);
                Raise();
            }
        }

        // =====================================================================
        public ShellViewModel()
        {
            try { Log.Info("ShellViewModel ctor starting"); } catch { }
            try { Settings = Store.LoadSettings(); } catch (Exception ex) { try { Log.Error("LoadSettings", ex); } catch { } Settings = new AppSettings(); }
            try { ThemeService.Current.Start(); } catch (Exception ex) { try { Log.Error("ThemeService.Start in VM", ex); } catch { } }
            try { ApplyThemeFromSettings(); } catch (Exception ex) { try { Log.Error("ApplyThemeFromSettings", ex); } catch { } }

            try
            {
                foreach (var a in Store.LoadAccounts()) Accounts.Add(a);
            }
            catch (Exception ex) { try { Log.Error("LoadAccounts", ex); } catch { } }

            try
            {
                CurrentAccount = Accounts.FirstOrDefault(a => a.Id == Settings.ActiveAccountId) ?? Accounts.FirstOrDefault();
                if (CurrentAccount != null) Settings.ActiveAccountId = CurrentAccount.Id;
            }
            catch (Exception ex) { try { Log.Error("Set CurrentAccount", ex); } catch { } }

            if (Accounts.Count == 0)
            {
                StatusText = "No account yet — add one in Settings → Accounts";
                ConnectionState = "Offline";
            }

            try
            {
                _clock.Tick += (_, _) =>
                {
                    ClockText = DateTime.Now.ToString("h:mm tt");
                    DateText = DateTime.Now.ToString("dddd, d MMMM");
                };
                _clock.Start();

                _syncTimer.Interval = TimeSpan.FromMinutes(Math.Max(1, Settings.SyncIntervalMinutes));
                _syncTimer.Tick += async (_, _) => await SyncAsync(silent: true);
                _syncTimer.Start();
            }
            catch (Exception ex) { try { Log.Error("Timers", ex); } catch { } }

            // Ask for the password before anything is shown.
            if (Settings.LockEnabled && !string.IsNullOrEmpty(Settings.LockHash))
                _isLocked = true;

            try { SystemEvents_PowerModeChanged(); } catch (Exception ex) { try { Log.Error("SystemEvents", ex); } catch { } }
            try { Log.Info($"ShellViewModel ready Accounts={Accounts.Count} Locked={_isLocked}"); } catch { }
        }

        private void SystemEvents_PowerModeChanged()
        {
            try
            {
                Microsoft.Win32.SystemEvents.UserPreferenceChanged += (_, _) =>
                {
                    Application.Current?.Dispatcher?.BeginInvoke(new Action(() =>
                    {
                        ThemeService.Current.RefreshSystem();
                    }));
                };
            }
            catch { }
        }

        public void ApplyThemeFromSettings()
        {
            var mode = Settings.ThemeMode switch
            {
                "Light" => AppTheme.Light,
                "Dark" => AppTheme.Dark,
                _ => AppTheme.System
            };
            ThemeService.Current.SetAcrylic(Settings.UseAcrylic);
            ThemeService.Current.SetMode(mode);
        }

        // ---------------- loading ----------------

        private async Task LoadAccountAsync(AccountRecord? account)
        {
            if (account == null) return;

            Folders.Clear();
            AllMessages.Clear();
            Messages.Clear();
            MessageDetail = null;
            SelectedMessage = null;
            SelectedFolder = null;

            var folders = await LoadFoldersAsync(account);
            foreach (var f in folders) Folders.Add(f);

            SelectedFolder = Folders.FirstOrDefault(f => f.Kind == FolderKind.Inbox) ?? Folders.FirstOrDefault();

            if (_cache.TryGetValue(account.Id, out var cached))
            {
                ReplaceAll(cached);
                ConnectionState = "Cached";
            }
            else
            {
                await SyncAsync();
            }

            if (Messages.Count > 0) SelectedMessage = Messages.FirstOrDefault(m => m.IsUnread) ?? Messages.First();
        }

        private async Task<List<MailFolder>> LoadFoldersAsync(AccountRecord account)
        {
            var password = GetPassword(account);
            if (string.IsNullOrEmpty(password)) return DefaultFolders();

            IsBusy = true;
            StatusText = "Reading folders…";
            try
            {
                var result = await MailService.SyncAsync(account, password, 250);
                if (result.Ok)
                {
                    ReplaceAll(result.Messages);
                    _cache[account.Id] = AllMessages.ToList();
                    var list = new List<MailFolder>
                    {
                        new MailFolder { Name = "Starred", Kind = FolderKind.Starred },
                        new MailFolder { Name = "Drafts", Kind = FolderKind.Drafts },
                        new MailFolder { Name = "Sent", Kind = FolderKind.Sent }
                    };
                    list.AddRange(result.Folders.Where(f => f.Kind != FolderKind.Inbox)
                        .OrderBy(f => f.Kind)
                        .Select(f => new MailFolder { Name = f.Name, Kind = f.Kind, RemoteName = f.RemoteName }));
                    var inbox = result.Folders.FirstOrDefault(f => f.Kind == FolderKind.Inbox);
                    list.Insert(0, new MailFolder { Name = "Inbox", Kind = FolderKind.Inbox, Unread = result.Unread });
                    list.Add(new MailFolder { Name = "Archive", Kind = FolderKind.Archive });
                    list.Add(new MailFolder { Name = "Junk", Kind = FolderKind.Spam });
                    list.Add(new MailFolder { Name = "Trash", Kind = FolderKind.Trash });
                    UnreadTotal = result.Unread;
                    ConnectionState = "Connected";
                    StatusText = $"Synced {result.Messages.Count} messages";
                    return list;
                }

                ConnectionState = "Sign-in needed";
                StatusText = result.Message;
                PushToast("Couldn't sync " + account.Email, result.Message, ToastKind.Warning);
                return DefaultFolders();
            }
            finally { IsBusy = false; }
        }

        private static List<MailFolder> DefaultFolders() => new()
        {
            new MailFolder { Name = "Inbox", Kind = FolderKind.Inbox },
            new MailFolder { Name = "Starred", Kind = FolderKind.Starred },
            new MailFolder { Name = "Drafts", Kind = FolderKind.Drafts },
            new MailFolder { Name = "Sent", Kind = FolderKind.Sent },
            new MailFolder { Name = "Archive", Kind = FolderKind.Archive },
            new MailFolder { Name = "Junk", Kind = FolderKind.Spam },
            new MailFolder { Name = "Trash", Kind = FolderKind.Trash }
        };

        private void ReplaceAll(IEnumerable<MessageSummary> items)
        {
            AllMessages.Clear();
            foreach (var m in items) AllMessages.Add(m);
            ApplyFilter();
            UpdateUnread();
        }

        private void UpdateUnread()
        {
            UnreadTotal = AllMessages.Count(m => m.IsUnread);
            var inbox = Folders.FirstOrDefault(f => f.Kind == FolderKind.Inbox);
            if (inbox != null) inbox.Unread = UnreadTotal;
        }

        // ---------------- filtering ----------------

        private void ApplyFilter()
        {
            IEnumerable<MessageSummary> q = AllMessages;

            var kind = SelectedFolder?.Kind ?? FolderKind.Inbox;
            q = kind switch
            {
                FolderKind.Starred => q.Where(m => m.IsFlagged),
                FolderKind.Inbox => q.Where(m => m.Folder == FolderKind.Inbox),
                FolderKind.Drafts => q.Where(m => m.Folder == FolderKind.Drafts),
                FolderKind.Sent => q.Where(m => m.Folder == FolderKind.Sent),
                FolderKind.Archive => q.Where(m => m.Folder == FolderKind.Archive),
                FolderKind.Spam => q.Where(m => m.Folder == FolderKind.Spam),
                FolderKind.Trash => q.Where(m => m.Folder == FolderKind.Trash),
                _ => q
            };

            if (UnreadOnly) q = q.Where(m => m.IsUnread);
            if (FlaggedOnly) q = q.Where(m => m.IsFlagged);

            if (!string.IsNullOrWhiteSpace(SearchText))
            {
                var terms = SearchText.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
                q = q.Where(m => terms.All(t =>
                    m.Subject.Contains(t, StringComparison.OrdinalIgnoreCase) ||
                    m.FromName.Contains(t, StringComparison.OrdinalIgnoreCase) ||
                    m.FromAddress.Contains(t, StringComparison.OrdinalIgnoreCase) ||
                    m.Preview.Contains(t, StringComparison.OrdinalIgnoreCase)));
            }

            var list = q.OrderByDescending(m => m.Date).ToList();

            Messages.Clear();
            foreach (var m in list) Messages.Add(m);

            Raise(nameof(ListCountText));
            Raise(nameof(IsListEmpty));
        }

        public string ListCountText => Messages.Count == 1 ? "1 message" : $"{Messages.Count} messages";
        public bool IsListEmpty => Messages.Count == 0;

        // ---------------- sync ----------------

        public async Task SyncAsync(bool silent = false)
        {
            if (CurrentAccount == null)
            {
                IsBusy = false;
                SyncProgress = 0;
                if (!silent)
                {
                    StatusText = "No account — add one in Settings → Accounts";
                    PushToast("No account", "Add your mail account in Settings → Accounts to get started.", ToastKind.Info);
                }
                return;
            }

            var account = CurrentAccount;
            var password = GetPassword(account);
            if (string.IsNullOrEmpty(password))
            {
                if (!silent)
                {
                    IsAddAccountOpen = true;
                    PushToast("Password needed", "Re-enter the password for " + account.Email + ".", ToastKind.Warning);
                }
                return;
            }

            IsBusy = true;
            SyncProgress = 0;
            StatusText = "Syncing " + account.Email + "…";
            try
            {
                var result = await MailService.SyncAsync(account, password, 300);
                SyncProgress = 100;
                if (result.Ok)
                {
                    ReplaceAll(result.Messages);
                    _cache[account.Id] = result.Messages;
                    UnreadTotal = result.Unread;
                    var inbox = Folders.FirstOrDefault(f => f.Kind == FolderKind.Inbox);
                    if (inbox != null) inbox.Unread = result.Unread;
                    ConnectionState = "Connected";
                    StatusText = $"Synced {DateTime.Now:h:mm tt} · {result.Messages.Count} messages";
                    if (!silent) PushToast("Inbox up to date", $"{result.Unread} unread of {result.Total} in {account.Email}.", ToastKind.Success);
                }
                else
                {
                    ConnectionState = "Sync failed";
                    StatusText = result.Message;
                    PushToast("Sync failed", result.Message, ToastKind.Error);
                }
            }
            finally
            {
                IsBusy = false;
                SyncProgress = 0;
            }
        }

        // ---------------- body loading ----------------

        private readonly HashSet<string> _loadingBodies = new();

        private async Task LoadBodyAsync(MessageSummary? summary)
        {
            if (summary == null) { MessageDetail = null; return; }

            if (_detailCache.TryGetValue(summary.Id, out var cached))
            {
                MessageDetail = cached;
                MarkRead(summary);
                return;
            }

            if (!_loadingBodies.Add(summary.Id)) return;
            IsBusy = true;
            StatusText = "Opening message…";
            try
            {
                var password = GetPassword(CurrentAccount!);
                var detail = await MailService.FetchMessageAsync(CurrentAccount!, password, summary.Id, summary.Folder, RemoteFolderFor(summary.Folder));
                if (detail != null)
                {
                    _detailCache[summary.Id] = detail;
                    if (SelectedMessage?.Id == summary.Id) MessageDetail = detail;
                    MarkRead(summary);
                }
                else
                {
                    MessageDetail = new MailMessageDetail
                    {
                        Id = summary.Id,
                        Subject = summary.Subject,
                        FromName = summary.FromName,
                        FromAddress = summary.FromAddress,
                        Date = summary.Date,
                        BodyText = "This message could not be downloaded. Check the connection and try again.",
                        AvatarText = summary.AvatarText,
                        AvatarColor = summary.AvatarColor
                    };
                }
            }
            finally
            {
                IsBusy = false;
                StatusText = ConnectionState;
                _loadingBodies.Remove(summary.Id);
            }
        }

        /// <summary>IMAP folder that holds messages for a given Sobre view.</summary>
        public string? RemoteFolderFor(FolderKind kind)
        {
            if (kind == FolderKind.Inbox) return "INBOX";
            return Folders.FirstOrDefault(f => f.Kind == kind)?.RemoteName;
        }

        private async void MarkRead(MessageSummary summary)
        {
            if (summary.IsRead) return;
            summary.IsRead = true;
            UpdateUnread();
            if (CurrentAccount == null) return;
            var password = GetPassword(CurrentAccount);
            if (string.IsNullOrEmpty(password)) return;
            await MailService.SetFlagsAsync(CurrentAccount, password, new[] { summary.Id }, read: true, remoteFolder: RemoteFolderFor(summary.Folder));
        }

        private bool HasValidCredentials(AccountRecord account)
        {
            return !string.IsNullOrWhiteSpace(GetPassword(account));
        }

        // ---------------- password cache ----------------

        public string GetPassword(AccountRecord account)
        {
            if (_passwords.TryGetValue(account.Id, out var p)) return p;
            var loaded = SecretStore.Unprotect(account.SecretBlob);
            if (!string.IsNullOrEmpty(loaded)) _passwords[account.Id] = loaded;
            return loaded;
        }

        public void RememberPassword(AccountRecord account, string password)
        {
            _passwords[account.Id] = password;
            account.SecretBlob = SecretStore.Protect(password);
        }

        public void ForgetPassword(AccountRecord account)
        {
            _passwords.Remove(account.Id);
            account.SecretBlob = "";
        }

        // ---------------- account management ----------------

        public void AddAccount(AccountRecord account, string password, bool makeCurrent = true)
        {
            var existing = Accounts.FirstOrDefault(a => a.Id == account.Id) ?? Accounts.FirstOrDefault(a => string.Equals(a.Email, account.Email, StringComparison.OrdinalIgnoreCase));
            if (existing != null) Accounts.Remove(existing);

            if (password != null) RememberPassword(account, password);
            Accounts.Add(account);
            PersistAccounts();

            if (makeCurrent) CurrentAccount = account;
        }

        public void RemoveAccount(AccountRecord account)
        {
            Accounts.Remove(account);
            _cache.Remove(account.Id);
            _passwords.Remove(account.Id);
            if (Settings.ActiveAccountId == account.Id) Settings.ActiveAccountId = "";
            PersistAccounts();

            if (CurrentAccount == account || CurrentAccount == null)
                CurrentAccount = Accounts.FirstOrDefault();
        }

        public void PersistAccounts()
        {
            try { Store.SaveAccounts(Accounts); } catch { }
            Settings.ActiveAccountId = CurrentAccount?.Id ?? "";
            Store.SaveSettings(Settings);
        }

        // ---------------- message actions ----------------

        public async Task ToggleFlagAsync(MessageSummary? m)
        {
            if (m == null) return;
            m.IsFlagged = !m.IsFlagged;
            if (SelectedFolder?.Kind == FolderKind.Starred) ApplyFilter();
            if (CurrentAccount == null) return;
            var pw = GetPassword(CurrentAccount);
            if (string.IsNullOrEmpty(pw)) return;
            await MailService.SetFlagsAsync(CurrentAccount, pw, new[] { m.Id }, flagged: m.IsFlagged, remoteFolder: RemoteFolderFor(m.Folder));
        }

        public async Task ToggleReadAsync(MessageSummary? m)
        {
            if (m == null) return;
            m.IsRead = !m.IsRead;
            UpdateUnread();
            ApplyFilter();
            if (CurrentAccount == null) return;
            var pw = GetPassword(CurrentAccount);
            if (string.IsNullOrEmpty(pw)) return;
            await MailService.SetFlagsAsync(CurrentAccount, pw, new[] { m.Id }, read: m.IsRead, remoteFolder: RemoteFolderFor(m.Folder));
        }

        public async Task ArchiveMessageAsync(MessageSummary? m)
        {
            if (m == null) return;

            if (CurrentAccount != null)
            {
                var pw = GetPassword(CurrentAccount);
                var archive = Folders.FirstOrDefault(f => f.Kind == FolderKind.Archive)?.RemoteName;
                if (!string.IsNullOrEmpty(pw))
                {
                    var result = await MailService.MoveAsync(CurrentAccount, pw, m.Id, RemoteFolderFor(m.Folder), archive);
                    if (!result.Ok) PushToast("Couldn't archive", result.Message, ToastKind.Warning);
                }
            }

            m.Folder = FolderKind.Archive;
            ApplyFilter();
            if (SelectedMessage == m) SelectedMessage = null;
            PushToast("Archived", m.Subject, ToastKind.Success);
        }

        public async Task DeleteMessageAsync(MessageSummary? m)
        {
            if (m == null) return;
            if (Settings.ConfirmOnDelete && !ConfirmDialog("Delete this message?", m.Subject)) return;
            var account = CurrentAccount;
            var pw = account == null ? "" : GetPassword(account);

            if (account != null && !string.IsNullOrEmpty(pw))
            {
                var trash = Folders.FirstOrDefault(f => f.Kind == FolderKind.Trash)?.RemoteName;
                if (!string.IsNullOrEmpty(trash))
                {
                    var moved = await MailService.MoveAsync(account, pw, m.Id, RemoteFolderFor(m.Folder), trash);
                    if (!moved.Ok) PushToast("Couldn't move to Trash", moved.Message, ToastKind.Warning);
                }
                else
                {
                    await MailService.SetFlagsAsync(account, pw, new[] { m.Id }, deleted: true, remoteFolder: RemoteFolderFor(m.Folder));
                }
            }

            AllMessages.Remove(m);
            ApplyFilter();
            if (SelectedMessage == m) SelectedMessage = null;
            UpdateUnread();
            PushToast("Deleted", m.Subject, ToastKind.Info);
        }

        private static bool ConfirmDialog(string title, string body)
        {
            return MessageBox.Show(body, title, MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;
        }

        // ---------------- compose ----------------

        public void ComposeNew()
        {
            ComposeTo.Clear(); ComposeCc.Clear(); ComposeAttachments.Clear();
            ComposeToText = ""; ComposeCcText = ""; ComposeSubject = "";
            ComposeBody = "";
            ShowComposeCc = false;
            ComposeFrom = CurrentAccount?.Email ?? "";
            IsComposeOpen = true;
        }

        public void ComposeReply(MessageSummary? m, bool all = false)
        {
            if (m == null) return;
            ComposeTo.Clear(); ComposeCc.Clear(); ComposeAttachments.Clear();
            ComposeTo.Add(new MailAddress { Address = m.FromAddress, Name = m.FromName });
            ComposeToText = m.FromAddress;
            if (all && MessageDetail != null)
            {
                foreach (var cc in MessageDetail.Cc.Where(c => c.Address != CurrentAccount?.Email))
                    ComposeCc.Add(cc);
                ComposeCcText = string.Join("; ", ComposeCc.Select(c => c.Address));
                ShowComposeCc = ComposeCc.Count > 0;
            }
            ComposeSubject = m.Subject.StartsWith("Re:", StringComparison.OrdinalIgnoreCase) ? m.Subject : "Re: " + m.Subject;
            ComposeBody = $"\n\n---\nOn {m.Date.ToLocalTime():ddd, d MMM yyyy} at {m.Date.ToLocalTime():h:mm tt}, {m.FromName} wrote:\n{(MessageDetail?.BodyText ?? m.Preview)}";
            ComposeFrom = CurrentAccount?.Email ?? "";
            IsComposeOpen = true;
        }

        public void ComposeForward(MessageSummary? m)
        {
            if (m == null) return;
            ComposeTo.Clear(); ComposeCc.Clear(); ComposeAttachments.Clear();
            ComposeToText = "";
            ComposeSubject = m.Subject.StartsWith("Fwd:", StringComparison.OrdinalIgnoreCase) ? m.Subject : "Fwd: " + m.Subject;
            ComposeBody = $"\n\n--- Forwarded message ---\nFrom: {m.FromName} <{m.FromAddress}>\nDate: {m.Date.ToLocalTime():f}\nSubject: {m.Subject}\n\n{(MessageDetail?.BodyText ?? m.Preview)}";
            ComposeFrom = CurrentAccount?.Email ?? "";
            IsComposeOpen = true;
        }

        public async Task<bool> SendAsync()
        {
            if (CurrentAccount == null) return false;
            var draft = new ComposeDraft { Subject = ComposeSubject, Body = ComposeBody };
            foreach (var a in ParseAddresses(ComposeToText)) draft.To.Add(a);
            foreach (var a in ParseAddresses(ComposeCcText)) draft.Cc.Add(a);
            draft.AttachmentPaths.AddRange(ComposeAttachments.Where(File.Exists));

            if (draft.To.Count == 0 && draft.Cc.Count == 0)
            {
                PushToast("Add a recipient", "Type at least one email address in the To field.", ToastKind.Warning);
                return false;
            }

            if (CurrentAccount.SignatureEnabled && !string.IsNullOrWhiteSpace(CurrentAccount.Signature))
                draft.Body = ComposeBody + "\n\n" + CurrentAccount.Signature;

            IsBusy = true;
            StatusText = "Sending…";
            try
            {
                var pw = GetPassword(CurrentAccount);
                var result = await MailService.SendAsync(CurrentAccount, pw, draft);
                if (result.Ok)
                {
                    PushToast("Message sent", draft.Subject, ToastKind.Success);
                    StatusText = "Sent";
                    IsComposeOpen = false;
                    return true;
                }
                PushToast("Couldn't send", result.Message, ToastKind.Error);
                StatusText = result.Message;
                return false;
            }
            finally { IsBusy = false; }
        }

        public static List<MailAddress> ParseAddresses(string? text)
        {
            var list = new List<MailAddress>();
            if (string.IsNullOrWhiteSpace(text)) return list;
            var parts = text.Split(new[] { ',', ';', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var raw in parts)
            {
                var t = raw.Trim().Trim('"');
                if (t.Length == 0) continue;
                var m = System.Text.RegularExpressions.Regex.Match(t, @"^(?<name>[^<]*?)\s*<(?<addr>[^>]+)>$");
                if (m.Success)
                    list.Add(new MailAddress { Name = m.Groups["name"].Value.Trim(), Address = m.Groups["addr"].Value.Trim() });
                else if (t.Contains('@'))
                    list.Add(new MailAddress { Address = t, Name = "" });
            }
            return list;
        }

        // ---------------- toasts ----------------

        public void PushToast(string title, string message, ToastKind kind)
        {
            var toast = new ToastModel(title, message, kind);
            Toasts.Add(toast);
            var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(kind == ToastKind.Error ? 7 : 4.5) };
            timer.Tick += (_, _) =>
            {
                timer.Stop();
                Toasts.Remove(toast);
            };
            timer.Start();
        }

        // ---------------- helpers ----------------

        public static Brush ToBrush(string? hex)
        {
            try { return new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex ?? "#0078D4")); }
            catch { return new SolidColorBrush(Color.FromRgb(0x00, 0x78, 0xD4)); }
        }
    }

    public enum ToastKind { Info, Success, Warning, Error }

    public sealed class ToastModel
    {
        public ToastModel(string title, string message, ToastKind kind)
        {
            Title = title; Message = message; Kind = kind;
            Glyph = kind switch
            {
                ToastKind.Success => "CheckmarkCircle",
                ToastKind.Warning => "Warning",
                ToastKind.Error => "ErrorCircle",
                _ => "Info"
            };
            Brush = kind switch
            {
                ToastKind.Success => Application.Current?.Resources["SuccessFill"] as Brush,
                ToastKind.Warning => Application.Current?.Resources["WarnFill"] as Brush,
                ToastKind.Error => Application.Current?.Resources["ErrorFill"] as Brush,
                _ => Application.Current?.Resources["AccentFill"] as Brush
            } ?? Brushes.SteelBlue;
        }
        public string Title { get; }
        public string Message { get; }
        public ToastKind Kind { get; }
        public string Glyph { get; }
        public Brush Brush { get; }
    }
}
