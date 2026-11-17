namespace GamaEdtech.Test.Application
{
    using System;
    using System.Collections.Generic;
    using System.Reflection;
    using System.Threading.Tasks;

    using GamaEdtech.Application.Service;
    using GamaEdtech.Common.Caching;
    using GamaEdtech.Domain.Entity.Identity;

    using Xunit;

    using static GamaEdtech.Application.Service.AccountDeletionCode;

    /// <summary>
    /// The emailed code that confirms deleting an account (replaced username + password on 2026-11-17, which never checked
    /// that the password belonged to the signed-in account).
    /// </summary>
    public class AccountDeletionCodeTests
    {
        private const long UserId = 42;

        [Fact]
        public async Task RightCodeIsAcceptedOnce()
        {
            var cache = MemoryCache.Create();
            var code = await IssueAsync(cache, UserId);

            Assert.NotNull(code);
            Assert.Matches("^[0-9]{6}$", code);
            Assert.Equal(CheckResult.Valid, await CheckAsync(cache, UserId, code));
            Assert.Equal(CheckResult.Missing, await CheckAsync(cache, UserId, code));
        }

        [Fact]
        public async Task CodeWithSpacesIsAccepted()
        {
            var cache = MemoryCache.Create();
            var code = await IssueAsync(cache, UserId);

            Assert.Equal(CheckResult.Valid, await CheckAsync(cache, UserId, $" {code![..3]} {code[3..]} "));
        }

        [Fact]
        public async Task NoCodeRequestedIsMissing()
        {
            var cache = MemoryCache.Create();

            Assert.Equal(CheckResult.Missing, await CheckAsync(cache, UserId, "123456"));
        }

        [Fact]
        public async Task CodeIsBoundToTheUser()
        {
            var cache = MemoryCache.Create();
            var code = await IssueAsync(cache, UserId);

            Assert.Equal(CheckResult.Missing, await CheckAsync(cache, UserId + 1, code));
            Assert.Equal(CheckResult.Valid, await CheckAsync(cache, UserId, code));
        }

        [Fact]
        public async Task TooManyWrongCodesThrowTheCodeAway()
        {
            var cache = MemoryCache.Create();
            var code = await IssueAsync(cache, UserId);
            var wrong = code == "000000" ? "111111" : "000000";

            for (var i = 1; i < MaxFailedAttempts; i++)
            {
                Assert.Equal(CheckResult.Wrong, await CheckAsync(cache, UserId, wrong));
            }

            Assert.Equal(CheckResult.TooManyAttempts, await CheckAsync(cache, UserId, wrong));
            Assert.Equal(CheckResult.Missing, await CheckAsync(cache, UserId, code));
        }

        [Fact]
        public async Task SecondRequestWithinResendIntervalIsRefused()
        {
            var cache = MemoryCache.Create();

            Assert.NotNull(await IssueAsync(cache, UserId));
            Assert.Null(await IssueAsync(cache, UserId));
        }

        [Theory]
        [InlineData("owner@example.com", "someone", "owner@example.com")]
        [InlineData(null, "legacy@example.com", "legacy@example.com")]
        [InlineData("", "legacy@example.com", "legacy@example.com")]
        [InlineData(null, "not-an-email", null)]
        public void CodeGoesToTheAccountEmail(string? email, string? userName, string? expected) =>
            Assert.Equal(expected, IdentityService.GetDeleteAccountCodeAddress(new ApplicationUser { Email = email, UserName = userName }));

        /// <summary>An in-memory ICacheProvider covering the string-keyed GetAsync/SetAsync/RemoveAsync the code store uses.</summary>
        public class MemoryCache : DispatchProxy
        {
            private readonly Dictionary<string, object?> items = [];

            public static ICacheProvider Create() => Create<ICacheProvider, MemoryCache>();

            protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
            {
                ArgumentNullException.ThrowIfNull(targetMethod);
                ArgumentNullException.ThrowIfNull(args);
                var key = (string)args[0]!;
                switch (targetMethod.Name)
                {
                    case nameof(ICacheProvider.SetAsync):
                        items[key] = args[1];
                        return Task.CompletedTask;
                    case nameof(ICacheProvider.RemoveAsync):
                        _ = items.Remove(key);
                        return Task.CompletedTask;
                    case nameof(ICacheProvider.GetAsync):
                        var itemType = targetMethod.GetGenericArguments()[0];
                        var empty = itemType.IsValueType ? Activator.CreateInstance(itemType) : null;
                        var value = items.TryGetValue(key, out var found) ? found : empty;
                        return typeof(Task).GetMethod(nameof(Task.FromResult))!.MakeGenericMethod(itemType).Invoke(null, [value]);
                    default:
                        throw new NotSupportedException(targetMethod.Name);
                }
            }
        }
    }
}
