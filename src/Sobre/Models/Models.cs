using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;

namespace Sobre.Models
{
    public enum FolderKind { Inbox, Starred, Drafts, Sent, Archive, Spam, Trash, All }
    public enum MailSecurity { Auto, SslOnConnect, StartTls, StartTlsWhenAvailable, None }

    public sealed class MailFolder : INotifyPropertyChanged
    {
        private int _unread;
        private string _name = "";

        public string Name { get => _name; set { _name = value; Raise(); } }
        public string? RemoteName { get; set; }
        public FolderKind Kind { get; set; }

        /// <summary>Live unread count — the badge in the sidebar follows it.</summary>
        public int Unread { get => _unread; set { if (_unread != value) { _unread = value; Raise(); } } }

        public ObservableCollection<MailFolder> Children { get; } = new();

        public event PropertyChangedEventHandler? PropertyChanged;
        private void Raise([CallerMemberName] string? n = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
    }

    public sealed class MailAddress
    {
        public string Name { get; set; } = "";
        public string Address { get; set; } = "";
        public override string ToString() => string.IsNullOrWhiteSpace(Name) ? Address : $"{Name} <{Address}>";
    }

    public class MessageSummary : INotifyPropertyChanged
    {
        private bool _isRead, _isFlagged;

        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public string AccountId { get; set; } = "";
        public FolderKind Folder { get; set; } = FolderKind.Inbox;
        public string FromName { get; set; } = "";
        public string FromAddress { get; set; } = "";
        public string Subject { get; set; } = "(no subject)";
        public string Preview { get; set; } = "";
        public DateTimeOffset Date { get; set; } = DateTimeOffset.Now;
        public bool HasAttachments { get; set; }
        public IReadOnlyList<string> Labels { get; set; } = Array.Empty<string>();
        public string AvatarText { get; set; } = "?";
        public string AvatarColor { get; set; } = "#5B6470";
        public string? AccountBadge { get; set; }

        public bool IsRead { get => _isRead; set { if (_isRead != value) { _isRead = value; Raise(); Raise(nameof(IsUnread)); } } }
        public bool IsUnread => !_isRead;
        public bool IsFlagged { get => _isFlagged; set { if (_isFlagged != value) { _isFlagged = value; Raise(); } } }

        public string DisplayDate
        {
            get
            {
                var local = Date.ToLocalTime();
                var today = DateTimeOffset.Now.Date;
                if (local.Date == today) return local.ToString("h:mm tt");
                if (local.Date == today.AddDays(-1)) return "Yesterday";
                if (local > DateTimeOffset.Now.AddDays(-6)) return local.ToString("ddd");
                return local.Year == DateTime.Today.Year ? local.ToString("MMM d") : local.ToString("M/d/yy");
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void Raise([CallerMemberName] string? n = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
    }

    public sealed class AttachmentInfo
    {
        public string FileName { get; set; } = "";
        public long SizeBytes { get; set; }
        public string ContentType { get; set; } = "application/octet-stream";
        public string? LocalPath { get; set; }
        public string SizeText => SizeBytes > 1024 * 1024
            ? $"{SizeBytes / 1024.0 / 1024.0:0.#} MB"
            : $"{Math.Max(1, SizeBytes / 1024)} KB";
    }

    public sealed class MailMessageDetail : MessageSummary
    {
        public string? BodyHtml { get; set; }
        public string? BodyText { get; set; }
        public IReadOnlyList<MailAddress> To { get; set; } = Array.Empty<MailAddress>();
        public IReadOnlyList<MailAddress> Cc { get; set; } = Array.Empty<MailAddress>();
        public IReadOnlyList<AttachmentInfo> Attachments { get; set; } = Array.Empty<AttachmentInfo>();
        public bool RemoteImagesBlocked { get; set; } = true;
    }

    public sealed class ComposeDraft
    {
        public string? InReplyToMessageId { get; set; }
        public List<MailAddress> To { get; } = new();
        public List<MailAddress> Cc { get; } = new();
        public List<MailAddress> Bcc { get; } = new();
        public string Subject { get; set; } = "";
        public string Body { get; set; } = "";
        public bool SendAsHtml { get; set; } = true;
        public List<string> AttachmentPaths { get; } = new();
    }

    /// <summary>A pre-made or custom server configuration.</summary>
    public sealed class ProviderPreset
    {
        public string Id { get; set; } = "custom";
        public string Name { get; set; } = "Custom";
        public string Description { get; set; } = "";
        public string ImapHost { get; set; } = "";
        public int ImapPort { get; set; } = 993;
        public MailSecurity ImapSecurity { get; set; } = MailSecurity.SslOnConnect;
        public string SmtpHost { get; set; } = "";
        public int SmtpPort { get; set; } = 587;
        public MailSecurity SmtpSecurity { get; set; } = MailSecurity.StartTls;
        public string? Note { get; set; }
        public string? HelpUrl { get; set; }
        public string AccentHex { get; set; } = "#0078D4";
        public string? AvatarText { get; set; }
        public string Glyph { get; set; } = "Server";
        public string[] Domains { get; set; } = Array.Empty<string>();

        public static readonly IReadOnlyList<ProviderPreset> All = new List<ProviderPreset>
        {
            new()
            {
                Id = "gmail", Name = "Gmail", Description = "Google Workspace & personal Gmail",
                ImapHost = "imap.gmail.com", ImapPort = 993, ImapSecurity = MailSecurity.SslOnConnect,
                SmtpHost = "smtp.gmail.com", SmtpPort = 587, SmtpSecurity = MailSecurity.StartTls,
                AccentHex = "#EA4335", AvatarText = "G", Glyph = "Mail",
                Domains = new[] { "gmail.com", "googlemail.com" },
                Note = "Gmail requires a 16-character App Password when 2-Step Verification is on. Sign in to your Google Account \u203a Security \u203a App passwords and generate one for \u201cMail\u201d.",
                HelpUrl = "https://myaccount.google.com/apppasswords"
            },
            new()
            {
                Id = "yahoo", Name = "Yahoo Mail", Description = "Yahoo Mail & AOL",
                ImapHost = "imap.mail.yahoo.com", ImapPort = 993, ImapSecurity = MailSecurity.SslOnConnect,
                SmtpHost = "smtp.mail.yahoo.com", SmtpPort = 465, SmtpSecurity = MailSecurity.SslOnConnect,
                AccentHex = "#6001D2", AvatarText = "Y", Glyph = "Mail",
                Domains = new[] { "yahoo.com", "aol.com" },
                Note = "Yahoo requires an app password generated at Account Security \u203a Generate app password.",
                HelpUrl = "https://login.yahoo.com/account/security"
            },
            new()
            {
                Id = "icloud", Name = "iCloud Mail", Description = "Apple iCloud with app-specific password",
                ImapHost = "imap.mail.me.com", ImapPort = 993, ImapSecurity = MailSecurity.SslOnConnect,
                SmtpHost = "smtp.mail.me.com", SmtpPort = 587, SmtpSecurity = MailSecurity.StartTls,
                AccentHex = "#5F6C7B", AvatarText = "i", Glyph = "Mail",
                Domains = new[] { "icloud.com", "me.com", "mac.com" },
                Note = "Create an app-specific password at appleid.apple.com \u203a Sign-In and Security.",
                HelpUrl = "https://appleid.apple.com"
            },
            new()
            {
                Id = "fastmail", Name = "Fastmail", Description = "Fastmail with app password",
                ImapHost = "imap.fastmail.com", ImapPort = 993, ImapSecurity = MailSecurity.SslOnConnect,
                SmtpHost = "smtp.fastmail.com", SmtpPort = 465, SmtpSecurity = MailSecurity.SslOnConnect,
                AccentHex = "#0067B9", AvatarText = "F", Glyph = "Mail",
                Domains = new[] { "fastmail.com", "fastmail.fm" },
                Note = "Generate an app password in Settings \u203a Privacy & Security \u203a App passwords.",
                HelpUrl = "https://app.fastmail.com/settings/security/apps"
            },
            new()
            {
                Id = "custom", Name = "Other / Custom", Description = "Any IMAP + SMTP server",
                ImapHost = "", ImapPort = 993, ImapSecurity = MailSecurity.SslOnConnect,
                SmtpHost = "", SmtpPort = 587, SmtpSecurity = MailSecurity.StartTls,
                AccentHex = "#8A8886", AvatarText = ",", Glyph = "Server",
                Note = "Enter the IMAP and SMTP details from your mail provider. Sobre stores credentials with Windows DPAPI, encrypted to your Windows user account."
            }
        };

        public static ProviderPreset Find(string? id) =>
            All.FirstOrDefault(p => string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase)) ?? All[^1];
    }

    /// <summary>Resolves a folder's Fluent glyph without needing an Application instance.</summary>
    public static class FolderGlyphs
    {
        public static string For(FolderKind kind) => kind switch
        {
            FolderKind.Inbox => "MailInbox",
            FolderKind.Drafts => "Drafts",
            FolderKind.Sent => "Send",
            FolderKind.Archive => "Archive",
            FolderKind.Spam => "MailProhibited",
            FolderKind.Trash => "Delete",
            FolderKind.Starred => "Star",
            FolderKind.All => "MailList",
            _ => "Folder"
        };

        public static string For(FolderKind? kind) => For(kind ?? FolderKind.All);
    }

    public static class ProviderPresets
    {
        public static ProviderPreset? ById(string? id) => ProviderPreset.Find(id);
    }
}
