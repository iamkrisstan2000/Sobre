using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Sobre.Models
{
    public sealed class AccountRecord
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public string DisplayName { get; set; } = "";
        public string Email { get; set; } = "";
        public string UserName { get; set; } = "";
        public string ProviderId { get; set; } = "custom";
        public string ImapHost { get; set; } = "";
        public int ImapPort { get; set; } = 993;
        public MailSecurity ImapSecurity { get; set; } = MailSecurity.SslOnConnect;
        public string SmtpHost { get; set; } = "";
        public int SmtpPort { get; set; } = 587;
        public MailSecurity SmtpSecurity { get; set; } = MailSecurity.StartTls;
        public bool SmtpRequiresAuth { get; set; } = true;
        public string Signature { get; set; } = "";
        public bool SignatureEnabled { get; set; } = true;
        public string AccentHex { get; set; } = "#0078D4";
        public string AvatarText { get; set; } = "";
        public bool SyncEnabled { get; set; } = true;

        /// <summary>DPAPI + AES-GCM sealed password. Never stored in plaintext.</summary>
        public string SecretBlob { get; set; } = "";

        public DateTimeOffset AddedUtc { get; set; } = DateTimeOffset.UtcNow;

        [JsonIgnore] public string Initial => string.IsNullOrEmpty(AvatarText)
            ? (string.IsNullOrEmpty(DisplayName) ? "?" : DisplayName.Substring(0, 1).ToUpperInvariant())
            : AvatarText;

        [JsonIgnore] public string Address => Email;

        [JsonIgnore] public string Provider => ProviderPreset.Find(ProviderId).Name;
    }

    /// <summary>
    /// Windows DPAPI (CurrentUser scope) protects the password blob, which is additionally
    /// sealed with an AES-256-GCM envelope so the ciphertext is authenticated.
    /// Only the Windows account that configured the app can ever decrypt it.
    /// </summary>
    public static class SecretStore
    {
        private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("Sobre.Mail.v1");

        public static string Protect(string plain)
        {
            if (string.IsNullOrEmpty(plain)) return "";
            try
            {
                var raw = Encoding.UTF8.GetBytes(plain);
                var sealedBytes = AesGcmSeal(raw);
                var dpapi = ProtectedData.Protect(sealedBytes, Entropy, DataProtectionScope.CurrentUser);
                return "v1:" + Convert.ToBase64String(dpapi);
            }
            catch
            {
                // Fall back to DPAPI alone — still user-bound, still not plaintext.
                try
                {
                    var dpapi = ProtectedData.Protect(Encoding.UTF8.GetBytes(plain), Entropy, DataProtectionScope.CurrentUser);
                    return "v0:" + Convert.ToBase64String(dpapi);
                }
                catch { return ""; }
            }
        }

        public static string Unprotect(string? blob)
        {
            if (string.IsNullOrWhiteSpace(blob)) return "";
            try
            {
                if (blob.StartsWith("v1:", StringComparison.Ordinal))
                {
                    var dpapi = Convert.FromBase64String(blob.Substring(3));
                    var sealedBytes = ProtectedData.Unprotect(dpapi, Entropy, DataProtectionScope.CurrentUser);
                    return Encoding.UTF8.GetString(AesGcmOpen(sealedBytes));
                }
                if (blob.StartsWith("v0:", StringComparison.Ordinal))
                {
                    var dpapi = Convert.FromBase64String(blob.Substring(3));
                    return Encoding.UTF8.GetString(ProtectedData.Unprotect(dpapi, Entropy, DataProtectionScope.CurrentUser));
                }
            }
            catch { }
            return "";
        }

        private static byte[] AesGcmSeal(byte[] plain)
        {
            using var key = new ProtectedKey();
            var nonce = RandomNumberGenerator.GetBytes(12);
            var tag = new byte[16];
            var cipher = new byte[plain.Length];
            using var gcm = new AesGcm(key.Key, 16);
            gcm.Encrypt(nonce, plain, cipher, tag);
            var outBuf = new byte[12 + 16 + cipher.Length];
            Buffer.BlockCopy(nonce, 0, outBuf, 0, 12);
            Buffer.BlockCopy(tag, 0, outBuf, 12, 16);
            Buffer.BlockCopy(cipher, 0, outBuf, 28, cipher.Length);
            return outBuf;
        }

        private static byte[] AesGcmOpen(byte[] buf)
        {
            if (buf.Length < 28) throw new CryptographicException("short blob");
            var nonce = buf.AsSpan(0, 12).ToArray();
            var tag = buf.AsSpan(12, 16).ToArray();
            var cipher = buf.AsSpan(28).ToArray();
            var plain = new byte[cipher.Length];
            using var key = new ProtectedKey();
            using var gcm = new AesGcm(key.Key, 16);
            gcm.Decrypt(nonce, cipher, tag, plain);
            return plain;
        }

        /// <summary>Per-install AES key, itself DPAPI-protected in the profile.</summary>
        private sealed class ProtectedKey : IDisposable
        {
            private static readonly object Gate = new();
            private static byte[]? _cached;

            public byte[] Key { get; }

            public ProtectedKey()
            {
                lock (Gate)
                {
                    _cached ??= Load();
                    Key = _cached;
                }
            }

            private static string KeyPath => Path.Combine(Store.DataDirectory, "device.key");

            private static byte[] Load()
            {
                try
                {
                    if (File.Exists(KeyPath))
                    {
                        var wrapped = File.ReadAllBytes(KeyPath);
                        return ProtectedData.Unprotect(wrapped, Entropy, DataProtectionScope.CurrentUser);
                    }
                }
                catch { }

                var fresh = RandomNumberGenerator.GetBytes(32);
                try
                {
                    Directory.CreateDirectory(Store.DataDirectory);
                    File.WriteAllBytes(KeyPath, ProtectedData.Protect(fresh, Entropy, DataProtectionScope.CurrentUser));
                    // Lock the key file down to the current user.
                    try
                    {
                        var fi = new FileInfo(KeyPath);
                        var acl = fi.GetAccessControl();
                        acl.SetAccessRuleProtection(true, false);
                        var user = System.Security.Principal.WindowsIdentity.GetCurrent().User;
                        if (user != null)
                        {
                            acl.AddAccessRule(new System.Security.AccessControl.FileSystemAccessRule(
                                user, System.Security.AccessControl.FileSystemRights.FullControl,
                                System.Security.AccessControl.AccessControlType.Allow));
                        }
                        fi.SetAccessControl(acl);
                    }
                    catch { }
                }
                catch { }
                return fresh;
            }

            public void Dispose() { }
        }
    }

    /// <summary>Message bodies are cached encrypted too, so the cache is not readable on disk.</summary>
    public static class BodyCacheCipher
    {
        public static string Seal(string body)
        {
            try
            {
                return SecretStore.Protect(body);
            }
            catch { return ""; }
        }
        public static string Open(string? blob) => SecretStore.Unprotect(blob);
    }

    public sealed class AppSettings : System.ComponentModel.INotifyPropertyChanged
    {
        public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;

        private void Raise(string name) =>
            PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(name));

        private bool SetBool(ref bool field, bool value, string name)
        {
            if (field == value) return false;
            field = value; Raise(name); return true;
        }

        private bool SetInt(ref int field, int value, string name)
        {
            if (field == value) return false;
            field = value; Raise(name); return true;
        }

        private bool SetStr(ref string field, string value, string name)
        {
            if (field == value) return false;
            field = value; Raise(name); return true;
        }

        private string _themeMode = "System";
        public string ThemeMode { get => _themeMode; set => SetStr(ref _themeMode, value, nameof(ThemeMode)); }           // System | Light | Dark
        private bool _useAcrylic = true;
        public bool UseAcrylic { get => _useAcrylic; set => SetBool(ref _useAcrylic, value, nameof(UseAcrylic)); }
        private bool _blockRemoteImages = true;
        public bool BlockRemoteImages { get => _blockRemoteImages; set => SetBool(ref _blockRemoteImages, value, nameof(BlockRemoteImages)); }
        private bool _showPreviewText = true;
        public bool ShowPreviewText { get => _showPreviewText; set => SetBool(ref _showPreviewText, value, nameof(ShowPreviewText)); }
        public string Density { get; set; } = "Comfortable";        // Comfortable | Compact
        public string ReadingFontSize { get; set; } = "Medium";     // Small | Medium | Large
        private bool _confirmOnDelete = false;
        public bool ConfirmOnDelete { get => _confirmOnDelete; set => SetBool(ref _confirmOnDelete, value, nameof(ConfirmOnDelete)); }
        public bool SendReadReceipts { get; set; } = false;
        public string DefaultProvider { get; set; } = "gmail";
        public bool CheckSpelling { get; set; } = true;
        private int _syncIntervalMinutes = 5;
        public int SyncIntervalMinutes { get => _syncIntervalMinutes; set => SetInt(ref _syncIntervalMinutes, value, nameof(SyncIntervalMinutes)); }
        public string ActiveAccountId { get; set; } = "";
        public double SidebarWidth { get; set; } = 232;
        public double ListWidth { get; set; } = 372;
        private bool _showAccountRail = true;
        public bool ShowAccountRail { get => _showAccountRail; set => SetBool(ref _showAccountRail, value, nameof(ShowAccountRail)); }

        // ---- app lock: PBKDF2 verifier only, the password is never stored ----
        private bool _lockEnabled;
        public bool LockEnabled { get => _lockEnabled; set => SetBool(ref _lockEnabled, value, nameof(LockEnabled)); }
        public string LockHash { get; set; } = "";
        public string LockSalt { get; set; } = "";
        public int LockIterations { get; set; }
    }

    public static class Store
    {
        public static string DataDirectory { get; } = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Sobre");

        private static string AccountsPath => Path.Combine(DataDirectory, "accounts.json");
        private static string SettingsPath => Path.Combine(DataDirectory, "settings.json");

        private static readonly JsonSerializerOptions JsonOpts = new()
        {
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        public static List<AccountRecord> LoadAccounts()
        {
            try
            {
                if (File.Exists(AccountsPath))
                    return JsonSerializer.Deserialize<List<AccountRecord>>(File.ReadAllText(AccountsPath), JsonOpts) ?? new();
            }
            catch { }
            return new List<AccountRecord>();
        }

        public static void SaveAccounts(IEnumerable<AccountRecord> accounts)
        {
            Directory.CreateDirectory(DataDirectory);
            File.WriteAllText(AccountsPath, JsonSerializer.Serialize(accounts.ToList(), JsonOpts));
        }

        public static AppSettings LoadSettings()
        {
            try
            {
                if (File.Exists(SettingsPath))
                    return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(SettingsPath), JsonOpts) ?? new();
            }
            catch { }
            return new AppSettings();
        }

        public static void SaveSettings(AppSettings s)
        {
            try
            {
                Directory.CreateDirectory(DataDirectory);
                File.WriteAllText(SettingsPath, JsonSerializer.Serialize(s, JsonOpts));
            }
            catch { }
        }
    }
}
