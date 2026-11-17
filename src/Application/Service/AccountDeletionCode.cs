namespace GamaEdtech.Application.Service
{
    using System.Globalization;
    using System.Security.Cryptography;
    using System.Text;

    using GamaEdtech.Common.Caching;

    using Microsoft.Extensions.Caching.Distributed;

    /// <summary>
    /// The one-time code a signed-in user gets by email to confirm deleting their account (IdentityService.
    /// SendDeleteAccountCodeAsync / InitializeDeletingAccountAsync). Same rules as the admin 2FA setup email code
    /// (TwoFactorService): only a SHA-256 hash is kept in the cache, the code lives <see cref="Lifetime"/>, works once,
    /// a new one can be asked for once per <see cref="ResendInterval"/>, and <see cref="MaxFailedAttempts"/> wrong tries
    /// throw it away, since 6 digits alone can be guessed.
    /// </summary>
    internal static class AccountDeletionCode
    {
        public const int MaxFailedAttempts = 5;
        public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(10);
        public static readonly TimeSpan ResendInterval = TimeSpan.FromSeconds(60);

        public enum CheckResult
        {
            Valid,
            Missing,
            Wrong,
            TooManyAttempts,
        }

        /// <summary>A new code, or null when one was sent less than <see cref="ResendInterval"/> ago.</summary>
        public static async Task<string?> IssueAsync(ICacheProvider cache, long userId)
        {
            var resendKey = ResendKey(userId);
            if (await cache.GetAsync<bool>(resendKey))
            {
                return null;
            }

            var code = RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6", CultureInfo.InvariantCulture);
            await cache.SetAsync(CodeKey(userId), new StoredCode { CodeHash = Hash(code), Attempts = 0 }
                , new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = Lifetime });
            await cache.SetAsync(resendKey, true, new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = ResendInterval });
            return code;
        }

        /// <summary>Checks the code; a valid one is used up, and the last allowed wrong try throws the code away.</summary>
        public static async Task<CheckResult> CheckAsync(ICacheProvider cache, long userId, string? code)
        {
            var key = CodeKey(userId);
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

            await cache.RemoveAsync(key);
            return CheckResult.Valid;
        }

        private static string CodeKey(long userId) => $"DeleteAccountCode_{userId.ToString(CultureInfo.InvariantCulture)}";

        private static string ResendKey(long userId) => $"DeleteAccountCodeSent_{userId.ToString(CultureInfo.InvariantCulture)}";

        private static string Hash(string code) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(code)));

        internal sealed class StoredCode
        {
            public string? CodeHash { get; set; }

            public int Attempts { get; set; }
        }
    }
}
