namespace GamaEdtech.Application.Service
{
    using System.Globalization;
    using System.Security.Cryptography;
    using System.Text;

    using GamaEdtech.Common.Caching;

    using Microsoft.Extensions.Caching.Distributed;

    /// <summary>
    /// The one-time code sent to an email address to prove it belongs to the person signing up or resetting their password
    /// (IdentityService register and password reset). Same rules as <see cref="AccountDeletionCode"/>, keyed by the purpose and
    /// the address instead of a user: only a SHA-256 hash is kept in the cache, the code lives <see cref="Lifetime"/>, a new one
    /// can be asked for once per <see cref="ResendInterval"/> (a new one replaces the old), and <see cref="MaxFailedAttempts"/>
    /// wrong tries throw it away. Checking it before the last step (the code screen) doesn't use it up; the last step does.
    /// </summary>
    internal static class EmailCode
    {
        public const int MaxFailedAttempts = 5;

        public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(10);

        public static readonly TimeSpan ResendInterval = TimeSpan.FromSeconds(60);

        public enum Purpose
        {
            Register,
            ResetPassword,
            Login,
        }

        public enum CheckResult
        {
            Valid,
            Missing,
            Wrong,
            TooManyAttempts,
        }

        /// <summary>The address as the codes are keyed: trimmed, lower case.</summary>
        public static string Normalize(string email) => email.Trim().ToLowerInvariant();

        /// <summary>A new code, or null when one was sent to this address for this purpose less than <see cref="ResendInterval"/> ago.</summary>
        public static async Task<string?> IssueAsync(ICacheProvider cache, Purpose purpose, string email)
        {
            var resendKey = Key("Sent", purpose, email);
            if (await cache.GetAsync<bool>(resendKey))
            {
                return null;
            }

            var code = RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6", CultureInfo.InvariantCulture);
            await cache.SetAsync(Key("Code", purpose, email), new StoredCode { CodeHash = Hash(code), Attempts = 0 }
                , new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = Lifetime });
            await cache.SetAsync(resendKey, true, new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = ResendInterval });
            return code;
        }

        /// <summary>Checks the code. With <paramref name="useUp"/> a valid one is removed; a wrong one counts either way, and the
        /// last allowed wrong try throws the code away.</summary>
        public static async Task<CheckResult> CheckAsync(ICacheProvider cache, Purpose purpose, string email, string? code, bool useUp)
        {
            var key = Key("Code", purpose, email);
            var stored = await cache.GetAsync<StoredCode>(key);
            if (stored?.CodeHash is null)
            {
                return CheckResult.Missing;
            }

            var normalized = code?.Replace(" ", string.Empty, StringComparison.Ordinal) ?? string.Empty;
            if (!CryptographicOperations.FixedTimeEquals(Convert.FromHexString(stored.CodeHash), Convert.FromHexString(Hash(normalized))))
            {
                stored.Attempts++;
                if (stored.Attempts >= MaxFailedAttempts)
                {
                    await cache.RemoveAsync(key);
                    return CheckResult.TooManyAttempts;
                }

                await cache.SetAsync(key, stored, new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = Lifetime });
                return CheckResult.Wrong;
            }

            if (useUp)
            {
                await cache.RemoveAsync(key);
            }

            return CheckResult.Valid;
        }

        /// <summary>The user-facing message for a code that didn't pass.</summary>
        public static string Message(CheckResult result) => result switch
        {
            CheckResult.Wrong => "The code is not correct. Check the email and try again.",
            CheckResult.TooManyAttempts => "Too many wrong codes. Ask for a new code.",
            _ => "The code has expired. Ask for a new code.",
        };

        private static string Key(string kind, Purpose purpose, string email) => $"EmailCode{kind}_{purpose}_{Normalize(email)}";

        private static string Hash(string code) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(code)));

        internal sealed class StoredCode
        {
            public string? CodeHash { get; set; }

            public int Attempts { get; set; }
        }
    }
}
