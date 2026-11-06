using System;
using System.Security.Cryptography;
using System.Text;

namespace Sobre.Services
{
    /// <summary>
    /// The optional Sobre app lock. The password is never stored — only a PBKDF2-SHA256
    /// hash with a per-install random salt, so the settings file alone can't be used to
    /// recover it. Verification is constant-time.
    /// </summary>
    public static class AppLock
    {
        /// <summary>OWASP-recommended work factor for PBKDF2-SHA256.</summary>
        public const int Iterations = 600_000;
        private const int SaltBytes = 16;
        private const int HashBytes = 32;

        public static (string Hash, string Salt, int Iterations) Create(string password)
        {
            var salt = RandomNumberGenerator.GetBytes(SaltBytes);
            var hash = Derive(password, salt, Iterations);
            return (Convert.ToBase64String(hash), Convert.ToBase64String(salt), Iterations);
        }

        public static bool Verify(string password, string? hashB64, string? saltB64, int iterations)
        {
            if (string.IsNullOrEmpty(hashB64) || string.IsNullOrEmpty(saltB64)) return false;
            if (iterations <= 0) iterations = Iterations;

            byte[] expected, salt;
            try
            {
                expected = Convert.FromBase64String(hashB64);
                salt = Convert.FromBase64String(saltB64);
            }
            catch { return false; }

            var actual = Derive(password ?? "", salt, iterations);
            return CryptographicOperations.FixedTimeEquals(actual, expected);
        }

        private static byte[] Derive(string password, byte[] salt, int iterations) =>
            Rfc2898DeriveBytes.Pbkdf2(
                Encoding.UTF8.GetBytes(password ?? ""),
                salt,
                iterations,
                HashAlgorithmName.SHA256,
                HashBytes);

        /// <summary>Rough password strength label used by Settings.</summary>
        public static string Strength(string? password)
        {
            if (string.IsNullOrEmpty(password)) return "";
            int score = 0;
            if (password.Length >= 8) score++;
            if (password.Length >= 12) score++;
            if (password.Any(char.IsUpper) && password.Any(char.IsLower)) score++;
            if (password.Any(char.IsDigit)) score++;
            if (password.Any(c => !char.IsLetterOrDigit(c))) score++;
            return score switch
            {
                <= 1 => "Weak — use at least 8 characters",
                2 => "Fair — add length or mixed case",
                3 => "Good",
                _ => "Strong"
            };
        }
    }

    internal static class CharExtensions
    {
        public static bool Any(this string s, Func<char, bool> predicate)
        {
            foreach (var c in s) if (predicate(c)) return true;
            return false;
        }
    }
}
