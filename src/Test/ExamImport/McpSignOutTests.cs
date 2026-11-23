namespace GamaEdtech.Test.ExamImport
{
    using System.Reflection;
    using System.Security.Claims;
    using System.Text.Json;

    using GamaEdtech.Application.Interface;
    using GamaEdtech.Application.Service;
    using GamaEdtech.Common.Caching;
    using GamaEdtech.Common.Identity;

    using Microsoft.AspNetCore.DataProtection;
    using Microsoft.AspNetCore.Http;
    using Microsoft.Extensions.Configuration;
    using Microsoft.Extensions.Logging.Abstractions;

    using Xunit;

    using static GamaEdtech.Common.Core.Constants;

    // Signing out deny-lists the connection's bearer token: its access token and its figure upload links are refused from
    // then on, and a deny-list that can't be read refuses everything. An access token from before the native sign-in (it
    // carried a gama-api token) is refused, so the client signs in again.
    public class McpSignOutTests
    {
        private const string UserToken = "1|token";

        private readonly EphemeralDataProtectionProvider dataProtection = new();
        private readonly Dictionary<string, object?> cache = new(StringComparer.Ordinal);

        [Fact]
        public async Task SignedOutConnectionIsRefused()
        {
            var service = Service();
            var accessToken = AccessToken();
            Assert.NotNull(await service.VerifyAccessTokenAsync(accessToken));
            Assert.False(await service.IsSignedOutAsync(UserToken));

            var signedOut = await service.SignOutAsync(accessToken);

            Assert.Equal(OperationResult.Succeeded, signedOut.OperationResult);
            Assert.Null(await service.VerifyAccessTokenAsync(accessToken));
            Assert.True(await service.IsSignedOutAsync(UserToken));
        }

        [Fact]
        public async Task AccessTokenFromBeforeTheSwitchIsRefused()
        {
            var service = Service();
            var old = dataProtection.CreateProtector("GamaEdtech.Mcp.AccessToken").ToTimeLimitedDataProtector()
                .Protect(JsonSerializer.Serialize(new { GamaToken = "gama-api.jwt", ClientId = "client" }), DateTimeOffset.UtcNow.AddDays(1));

            Assert.Null(await service.VerifyAccessTokenAsync(old));
        }

        [Fact]
        public async Task UnreadableDenyListFailsClosed()
        {
            var service = Service(cacheDown: true);
            Assert.True(await service.IsSignedOutAsync(UserToken));
            Assert.Null(await service.VerifyAccessTokenAsync(AccessToken()));
        }

        private McpAuthorizationService Service(bool cacheDown = false) => new(
            new(() => null!),
            new(() => new HttpContextAccessor()),
            new(() => null!),
            new(() => NullLogger<McpAuthorizationService>.Instance),
            new(() => new ConfigurationBuilder().Build()),
            new(() => dataProtection),
            new(() => Fake.Create<ICacheProvider>(new()
            {
                ["SetAsync"] = args =>
                {
                    cache[(string)args[0]!] = args[1];
                    return Task.CompletedTask;
                },
                ["GetAsync"] = args => cacheDown
                    ? throw new InvalidOperationException("The cache is down.")
                    : Task.FromResult(cache.TryGetValue((string)args[0]!, out var value) && value is true),
            })),
            new(() => Fake.Create<IIdentityService>([])),
            new(() => Fake.Create<ITokenService>(new()
            {
                ["VerifyTokenAsync"] = args => Task.FromResult<VerifyTokenResponse?>(args[0] is VerifyTokenRequest { UserId: "1", Token: "token" }
                    ? new() { Claims = [new Claim(ClaimTypes.NameIdentifier, "1")] }
                    : null),
            })));

        /// <summary>An MCP access token as the token endpoint issues it, carrying <see cref="UserToken"/>.</summary>
        private string AccessToken() => dataProtection.CreateProtector("GamaEdtech.Mcp.AccessToken").ToTimeLimitedDataProtector()
            .Protect(JsonSerializer.Serialize(new { UserToken, ClientId = "client" }), DateTimeOffset.UtcNow.AddDays(1));

        /// <summary>Implementations of an interface whose methods, by name, are the given handlers.</summary>
        public class Fake : DispatchProxy
        {
            private Dictionary<string, Func<object?[], object?>> handlers = [];

            public static T Create<T>(Dictionary<string, Func<object?[], object?>> handlers)
                where T : class
            {
                var fake = Create<T, Fake>();
                ((Fake)(object)fake).handlers = handlers;
                return fake;
            }

            protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
            {
                ArgumentNullException.ThrowIfNull(targetMethod);
                return handlers.TryGetValue(targetMethod.Name, out var handler) ? handler(args ?? []) : throw new NotSupportedException(targetMethod.Name);
            }
        }
    }
}
