using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MailKit;
using MailKit.Net.Imap;
using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;
using HtmlAgilityPack;
using Sobre.Models;
// MailKit has same-named types; Sobre's models win inside this file.
using MailFolder = Sobre.Models.MailFolder;
using MessageSummary = Sobre.Models.MessageSummary;
using MailMessageDetail = Sobre.Models.MailMessageDetail;

namespace Sobre.Services
{
    public sealed class MailResult
    {
        public bool Ok { get; init; }
        public string Message { get; init; } = "";
        public static MailResult Success(string msg = "") => new() { Ok = true, Message = msg };
        public static MailResult Fail(string msg) => new() { Ok = false, Message = msg };
    }

    public sealed class SyncResult
    {
        public bool Ok { get; init; }
        public string Message { get; init; } = "";
        public List<MailFolder> Folders { get; init; } = new();
        public List<MessageSummary> Messages { get; init; } = new();
        public int Unread { get; init; }
        public int Total { get; init; }
    }

    /// <summary>
    /// All network mail traffic. MailKit handles IMAP/SMTP; nothing here talks to an
    /// ad, telemetry or analytics endpoint. Credentials arrive already decrypted and
    /// are never logged.
    /// </summary>
    public static class MailService
    {
        public static SecureSocketOptions ToSocketOptions(MailSecurity s) => s switch
        {
            MailSecurity.SslOnConnect => SecureSocketOptions.SslOnConnect,
            MailSecurity.StartTls => SecureSocketOptions.StartTls,
            MailSecurity.StartTlsWhenAvailable => SecureSocketOptions.StartTlsWhenAvailable,
            MailSecurity.None => SecureSocketOptions.None,
            _ => SecureSocketOptions.Auto
        };

        private static ImapClient NewImap(AccountRecord a, string password)
        {
            var client = new ImapClient
            {
                Timeout = 30000
            };
            client.ServerCertificateValidationCallback = (s, cert, chain, errors) => errors == System.Net.Security.SslPolicyErrors.None;
            return client;
        }

        public static async Task<MailResult> TestConnectionAsync(AccountRecord account, string password, CancellationToken ct = default)
        {
            try
            {
                using var imap = NewImap(account, password);
                await imap.ConnectAsync(account.ImapHost, account.ImapPort, ToSocketOptions(account.ImapSecurity), ct);
                await imap.AuthenticateAsync(account.UserName, password, ct);
                var folders = imap.GetFolder(imap.PersonalNamespaces[0]).Count;
                await imap.DisconnectAsync(true, ct);

                using var smtp = new SmtpClient { Timeout = 30000 };
                await smtp.ConnectAsync(account.SmtpHost, account.SmtpPort, ToSocketOptions(account.SmtpSecurity), ct);
                if (account.SmtpRequiresAuth)
                    await smtp.AuthenticateAsync(account.UserName, password, ct);
                await smtp.DisconnectAsync(true, ct);

                return MailResult.Success($"Connected — IMAP and SMTP both accepted your sign-in ({folders} top-level folders found).");
            }
            catch (AuthenticationException ex)
            {
                return MailResult.Fail("Sign-in was rejected. For Gmail you usually need an app password, not your normal account password. " + ex.Message);
            }
            catch (SslHandshakeException ex)
            {
                return MailResult.Fail("TLS handshake failed — check the port and security setting. " + ex.Message);
            }
            catch (ImapCommandException ex)
            {
                return MailResult.Fail("The server refused a command. " + ex.Message);
            }
            catch (Exception ex)
            {
                return MailResult.Fail("Could not reach the server: " + ex.Message);
            }
        }

        // ---------- folder discovery ----------

        private static FolderKind KindFor(string remoteName, string? attributes)
        {
            var n = remoteName.ToLowerInvariant();
            if (attributes?.Contains("\\Inbox", StringComparison.OrdinalIgnoreCase) == true || n == "inbox") return FolderKind.Inbox;
            if (attributes?.Contains("\\Sent", StringComparison.OrdinalIgnoreCase) == true || n.EndsWith("sent") || n.Contains("sent items") || n.Contains("sent mail")) return FolderKind.Sent;
            if (attributes?.Contains("\\Drafts", StringComparison.OrdinalIgnoreCase) == true || n.Contains("draft")) return FolderKind.Drafts;
            if (attributes?.Contains("\\Junk", StringComparison.OrdinalIgnoreCase) == true || n.Contains("junk") || n.Contains("spam")) return FolderKind.Spam;
            if (attributes?.Contains("\\Trash", StringComparison.OrdinalIgnoreCase) == true || n.Contains("trash") || n.Contains("deleted")) return FolderKind.Trash;
            if (attributes?.Contains("\\Archive", StringComparison.OrdinalIgnoreCase) == true || n.Contains("archive")) return FolderKind.Archive;
            return FolderKind.All;
        }

        public static async Task<SyncResult> SyncAsync(AccountRecord account, string password, int messageLimit, CancellationToken ct = default)
        {
            try
            {
                using var imap = NewImap(account, password);
                await imap.ConnectAsync(account.ImapHost, account.ImapPort, ToSocketOptions(account.ImapSecurity), ct);
                await imap.AuthenticateAsync(account.UserName, password, ct);

                var folders = new List<MailFolder>();
                IMailFolder? inboxFolder = null;

                var root = imap.GetFolder(imap.PersonalNamespaces.FirstOrDefault() ?? imap.PersonalNamespaces[0]);
                foreach (var f in root.GetSubfolders(false))
                {
                    var kind = KindFor(f.FullName, f.Attributes.ToString());
                    if (string.Equals(f.FullName, "INBOX", StringComparison.OrdinalIgnoreCase)) kind = FolderKind.Inbox;
                    if (kind == FolderKind.Inbox) inboxFolder = f;

                    var mf = new MailFolder { Name = f.Name, RemoteName = f.FullName, Kind = kind };
                    try
                    {
                        if (f.Count > 0) mf.Unread = f.Unread;
                    }
                    catch { }
                    folders.Add(mf);
                }

                inboxFolder ??= imap.Inbox;

                // Starred / All / Archive / Trash are synthetic views Sobre builds itself.
                var messages = new List<MessageSummary>();
                int unread = 0, total = 0;

                if (inboxFolder != null)
                {
                    await inboxFolder.OpenAsync(FolderAccess.ReadOnly, ct);
                    total = inboxFolder.Count;
                    unread = inboxFolder.Unread;

                    int end = total - 1;
                    int start = Math.Max(0, end - Math.Max(1, messageLimit) + 1);
                    if (end >= start)
                    {
                        var items = await inboxFolder.FetchAsync(start, end,
                            MessageSummaryItems.Envelope |
                            MessageSummaryItems.Flags |
                            MessageSummaryItems.BodyStructure |
                            MessageSummaryItems.PreviewText |
                            MessageSummaryItems.Body, ct);

                        foreach (var m in items.Reverse())
                            messages.Add(ToSummary(m, account.Id));
                    }
                    await inboxFolder.CloseAsync(false, ct);
                }

                // Secondary folders: a bounded window keeps sync quick while making
                // Sent / Drafts / Archive / Junk / Trash views real rather than empty.
                foreach (var mf in folders.Where(f => f.Kind != FolderKind.Inbox))
                {
                    if (mf.RemoteName == null) continue;
                    try
                    {
                        var folder = await imap.GetFolderAsync(mf.RemoteName, ct);
                        if (folder == null || folder.Count == 0) continue;
                        await folder.OpenAsync(FolderAccess.ReadOnly, ct);
                        int end = folder.Count - 1;
                        int start = Math.Max(0, end - 59);
                        var items = await folder.FetchAsync(start, end,
                            MessageSummaryItems.Envelope | MessageSummaryItems.Flags |
                            MessageSummaryItems.BodyStructure | MessageSummaryItems.PreviewText | MessageSummaryItems.Body, ct);
                        foreach (var m in items.Reverse())
                        {
                            var summary = ToSummary(m, account.Id);
                            summary.Folder = mf.Kind;
                            messages.Add(summary);
                        }
                        await folder.CloseAsync(false, ct);
                    }
                    catch { /* one unreadable folder must not abort the whole sync */ }
                }

                await imap.DisconnectAsync(true, ct);
                return new SyncResult { Ok = true, Folders = folders, Messages = messages, Unread = unread, Total = total };
            }
            catch (AuthenticationException ex)
            {
                return new SyncResult { Ok = false, Message = "Sign-in rejected. Try an app password. " + ex.Message };
            }
            catch (Exception ex)
            {
                return new SyncResult { Ok = false, Message = ex.Message };
            }
        }

        private static MessageSummary ToSummary(IMessageSummary m, string accountId)
        {
            var env = m.Envelope;
            var from = env?.From?.Mailboxes?.FirstOrDefault();
            var name = from?.Name ?? "";
            var addr = from?.Address ?? "";
            var display = string.IsNullOrWhiteSpace(name) ? (string.IsNullOrWhiteSpace(addr) ? "(unknown sender)" : addr.Split('@')[0]) : name;

            string preview = m.PreviewText ?? "";
            if (string.IsNullOrWhiteSpace(preview))
                preview = env?.Subject != null ? "" : "";

            var labels = new List<string>();
            if (m.Flags?.HasFlag(MessageFlags.Flagged) == true) labels.Add("Flagged");

            return new MessageSummary
            {
                Id = m.UniqueId.IsValid ? m.UniqueId.ToString() : m.Index.ToString(),
                AccountId = accountId,
                Folder = FolderKind.Inbox,
                FromName = display,
                FromAddress = addr,
                Subject = string.IsNullOrWhiteSpace(env?.Subject) ? "(no subject)" : env!.Subject!,
                Preview = preview.Replace("\r", " ").Replace("\n", " ").Trim(),
                Date = env?.Date ?? DateTimeOffset.Now,
                IsRead = m.Flags?.HasFlag(MessageFlags.Seen) == true,
                IsFlagged = m.Flags?.HasFlag(MessageFlags.Flagged) == true,
                HasAttachments = m.Attachments != null && m.Attachments.Any(),
                Labels = labels,
                AvatarText = Initials(display),
                AvatarColor = ColorFor(addr)
            };
        }

        public static string Initials(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return "?";
            var parts = name.Split(new[] { ' ', '.', '_', '-' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 2) return (char.ToUpperInvariant(parts[0][0]).ToString() + char.ToUpperInvariant(parts[1][0]));
            return parts[0].Length >= 2 ? parts[0].Substring(0, 2).ToUpperInvariant() : parts[0].ToUpperInvariant();
        }

        private static readonly string[] AvatarPalette =
        {
            "#5B6470", "#0F6CBD", "#C239B3", "#E3008C", "#8764B8", "#00766C",
            "#986F0B", "#CA5010", "#4F6BED", "#498205", "#A4262C", "#3A96DD"
        };

        public static string ColorFor(string key)
        {
            if (string.IsNullOrEmpty(key)) return AvatarPalette[0];
            int h = 0;
            foreach (var ch in key) h = (h * 31 + ch) & 0x7FFFFFFF;
            return AvatarPalette[h % AvatarPalette.Length];
        }

        // ---------- message detail ----------

        /// <summary>
        /// Downloads one message. IMAP UIDs are scoped to a folder, so the caller passes the
        /// folder's remote name; "INBOX" is used when it isn't known.
        /// </summary>
        public static async Task<MailMessageDetail?> FetchMessageAsync(AccountRecord account, string password,
            string uniqueId, FolderKind folder, string? remoteFolder = null, CancellationToken ct = default)
        {
            try
            {
                using var imap = NewImap(account, password);
                await imap.ConnectAsync(account.ImapHost, account.ImapPort, ToSocketOptions(account.ImapSecurity), ct);
                await imap.AuthenticateAsync(account.UserName, password, ct);

                if (!uint.TryParse(uniqueId, out var uid)) return null;

                var candidates = new List<string?>();
                if (!string.IsNullOrWhiteSpace(remoteFolder)) candidates.Add(remoteFolder);
                candidates.Add(folder switch
                {
                    FolderKind.Sent => "Sent",
                    FolderKind.Drafts => "Drafts",
                    FolderKind.Archive => "Archive",
                    FolderKind.Spam => "Junk",
                    FolderKind.Trash => "Trash",
                    _ => "INBOX"
                });
                candidates.Add("INBOX");

                MimeMessage? msg = null;
                foreach (var name in candidates.Where(c => !string.IsNullOrWhiteSpace(c)).Distinct())
                {
                    try
                    {
                        IMailFolder target;
                        if (string.Equals(name, "INBOX", StringComparison.OrdinalIgnoreCase)) target = imap.Inbox;
                        else
                        {
                            target = await imap.GetFolderAsync(name!, ct);
                            if (target == null) continue;
                        }
                        await target.OpenAsync(FolderAccess.ReadOnly, ct);
                        msg = await target.GetMessageAsync(new UniqueId(uid), ct);
                        await target.CloseAsync(false, ct);
                        if (msg != null) break;
                    }
                    catch { /* try the next candidate */ }
                }

                await imap.DisconnectAsync(true, ct);
                return msg == null ? null : ToDetail(msg, uniqueId, account.Id);
            }
            catch
            {
                return null;
            }
        }

        public static MailMessageDetail ToDetail(MimeMessage msg, string id, string accountId)
        {
            var from = msg.From.Mailboxes.FirstOrDefault();
            string bodyText = msg.TextBody ?? "";
            string? bodyHtml = msg.HtmlBody;

            var attachments = new List<AttachmentInfo>();
            foreach (var part in msg.Attachments)
            {
                if (part is MimePart mp)
                {
                    attachments.Add(new AttachmentInfo
                    {
                        FileName = mp.FileName ?? "attachment",
                        SizeBytes = mp.Content?.Stream?.Length ?? 0,
                        ContentType = mp.ContentType?.MimeType ?? "application/octet-stream"
                    });
                }
            }

            var labels = new List<string>();
            if (msg.Headers.Contains(HeaderId.Importance) && msg.Headers[HeaderId.Importance]?.Contains("high", StringComparison.OrdinalIgnoreCase) == true)
                labels.Add("Important");

            return new MailMessageDetail
            {
                Id = id,
                AccountId = accountId,
                FromName = from?.Name ?? from?.Address ?? "(unknown sender)",
                FromAddress = from?.Address ?? "",
                Subject = string.IsNullOrWhiteSpace(msg.Subject) ? "(no subject)" : msg.Subject,
                Date = msg.Date,
                Preview = (bodyText.Length > 220 ? bodyText.Substring(0, 220) : bodyText).Replace("\r", " ").Replace("\n", " ").Trim(),
                BodyHtml = bodyHtml,
                BodyText = bodyText,
                To = msg.To.Mailboxes.Select(m => new MailAddress { Name = m.Name ?? "", Address = m.Address }).ToList(),
                Cc = msg.Cc.Mailboxes.Select(m => new MailAddress { Name = m.Name ?? "", Address = m.Address }).ToList(),
                Attachments = attachments,
                Labels = labels,
                IsRead = true,
                AvatarText = Initials(from?.Name ?? from?.Address ?? ""),
                AvatarColor = ColorFor(from?.Address ?? "")
            };
        }

        // ---------- sending ----------

        public static async Task<MailResult> SendAsync(AccountRecord account, string password, ComposeDraft draft, CancellationToken ct = default)
        {
            try
            {
                var msg = new MimeMessage();
                msg.From.Add(new MailboxAddress(account.DisplayName, account.Email));
                foreach (var t in draft.To.Where(x => !string.IsNullOrWhiteSpace(x.Address)))
                    msg.To.Add(new MailboxAddress(t.Name, t.Address));
                foreach (var c in draft.Cc.Where(x => !string.IsNullOrWhiteSpace(x.Address)))
                    msg.Cc.Add(new MailboxAddress(c.Name, c.Address));
                foreach (var b in draft.Bcc.Where(x => !string.IsNullOrWhiteSpace(x.Address)))
                    msg.Bcc.Add(new MailboxAddress(b.Name, b.Address));

                if (msg.To.Count == 0 && msg.Cc.Count == 0 && msg.Bcc.Count == 0)
                    return MailResult.Fail("Add at least one recipient.");

                msg.Subject = draft.Subject ?? "";

                var builder = new BodyBuilder();
                if (draft.SendAsHtml)
                {
                    builder.HtmlBody = draft.Body;
                    builder.TextBody = HtmlToText(draft.Body);
                }
                else
                {
                    builder.TextBody = draft.Body;
                }

                foreach (var path in draft.AttachmentPaths.Where(File.Exists))
                {
                    try { builder.Attachments.Add(path); } catch { }
                }

                msg.Body = builder.ToMessageBody();

                using var smtp = new SmtpClient { Timeout = 30000 };
                await smtp.ConnectAsync(account.SmtpHost, account.SmtpPort, ToSocketOptions(account.SmtpSecurity), ct);
                if (account.SmtpRequiresAuth)
                    await smtp.AuthenticateAsync(account.UserName, password, ct);
                await smtp.SendAsync(msg, ct);
                await smtp.DisconnectAsync(true, ct);
                return MailResult.Success("Message sent.");
            }
            catch (AuthenticationException ex)
            {
                return MailResult.Fail("SMTP sign-in rejected — check the app password and that SMTP AUTH is enabled. " + ex.Message);
            }
            catch (Exception ex)
            {
                return MailResult.Fail("Send failed: " + ex.Message);
            }
        }

        // ---------- flag operations ----------

        public static async Task<MailResult> SetFlagsAsync(AccountRecord account, string password, IEnumerable<string> uids,
            bool? read = null, bool? flagged = null, bool? deleted = null, string? remoteFolder = null, CancellationToken ct = default)
        {
            try
            {
                using var imap = NewImap(account, password);
                await imap.ConnectAsync(account.ImapHost, account.ImapPort, ToSocketOptions(account.ImapSecurity), ct);
                await imap.AuthenticateAsync(account.UserName, password, ct);

                IMailFolder inbox;
                if (string.IsNullOrWhiteSpace(remoteFolder) || string.Equals(remoteFolder, "INBOX", StringComparison.OrdinalIgnoreCase))
                    inbox = imap.Inbox;
                else
                    inbox = await imap.GetFolderAsync(remoteFolder!, ct) ?? imap.Inbox;
                await inbox.OpenAsync(FolderAccess.ReadWrite, ct);

                var ids = uids.Select(u => uint.TryParse(u, out var v) ? new UniqueId(v) : default)
                              .Where(u => u.IsValid).ToList();
                if (ids.Count == 0) return MailResult.Fail("No message identifiers.");

                if (read.HasValue)
                {
                    if (read.Value) await inbox.AddFlagsAsync(ids, MessageFlags.Seen, true, ct);
                    else await inbox.RemoveFlagsAsync(ids, MessageFlags.Seen, true, ct);
                }
                if (flagged.HasValue)
                {
                    if (flagged.Value) await inbox.AddFlagsAsync(ids, MessageFlags.Flagged, true, ct);
                    else await inbox.RemoveFlagsAsync(ids, MessageFlags.Flagged, true, ct);
                }
                if (deleted == true)
                {
                    await inbox.AddFlagsAsync(ids, MessageFlags.Deleted, true, ct);
                    await inbox.ExpungeAsync(ct);
                }

                await imap.DisconnectAsync(true, ct);
                return MailResult.Success();
            }
            catch (Exception ex)
            {
                return MailResult.Fail(ex.Message);
            }
        }

        /// <summary>Moves a message to another folder (IMAP MOVE, falling back to COPY + DELETE).</summary>
        public static async Task<MailResult> MoveAsync(AccountRecord account, string password, string uid,
            string? fromFolder, string? toFolderName, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(toFolderName))
                return MailResult.Fail("The destination folder isn't available on this server.");

            try
            {
                using var imap = NewImap(account, password);
                await imap.ConnectAsync(account.ImapHost, account.ImapPort, ToSocketOptions(account.ImapSecurity), ct);
                await imap.AuthenticateAsync(account.UserName, password, ct);

                var source = string.IsNullOrWhiteSpace(fromFolder) || fromFolder == "INBOX"
                    ? imap.Inbox
                    : await imap.GetFolderAsync(fromFolder!, ct);
                var target = await imap.GetFolderAsync(toFolderName, ct);
                if (source == null || target == null) return MailResult.Fail("Folder not found.");

                if (!uint.TryParse(uid, out var uidValue)) return MailResult.Fail("Bad message id.");

                await source.OpenAsync(FolderAccess.ReadWrite, ct);
                var id = new UniqueId(uidValue);

                if ((source.Access & FolderAccess.ReadWrite) != 0)
                {
                    try { await source.MoveToAsync(id, target, ct); }
                    catch
                    {
                        await source.CopyToAsync(id, target, ct);
                        await source.AddFlagsAsync(id, MessageFlags.Deleted, true, ct);
                        await source.ExpungeAsync(ct);
                    }
                }
                await imap.DisconnectAsync(true, ct);
                return MailResult.Success();
            }
            catch (Exception ex)
            {
                return MailResult.Fail(ex.Message);
            }
        }

        public static string HtmlToText(string html)
        {
            if (string.IsNullOrWhiteSpace(html)) return "";
            var t = System.Text.RegularExpressions.Regex.Replace(html, "<br\\s*/?>|</p>", "\n", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            t = System.Text.RegularExpressions.Regex.Replace(t, "<[^>]+>", "");
            t = System.Net.WebUtility.HtmlDecode(t);
            return t.Trim();
        }
    }
}
