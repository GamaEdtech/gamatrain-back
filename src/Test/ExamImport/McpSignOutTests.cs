namespace GamaEdtech.Test.ExamImport
{
    using System.Reflection;
    using System.Security.Claims;
    using System.Text.Json;

    using GamaEdtech.Application.Interface;
    using GamaEdtech.Application.Service;
    using GamaEdtech.Common.Caching;
    using GamaEdtech.Common.Data;
    using GamaEdtech.Common.Identity;

    using Microsoft.AspNetCore.DataProtection;
    using Microsoft.AspNetCore.Http;
    using Microsoft.Extensions.Configuration;
    using Microsoft.Extensions.Logging.Abstractions;

    using Xunit;

    using static GamaEdtech.Common.Core.Constants;

    using Void = GamaEdtech.Common.Data.Void;

    // Signing out deny-lists the connection's gama-api token: its access token and its figure upload links are refused
    // from then on, even when gama-api doesn't confirm the logout, and a deny-list that can't be read refuses everything.
    public class McpSignOutTests
    {
        private const string GamaToken = "gama-api.jwt";

        private readonly EphemeralDataProtectionProvider dataProtection = new();
        private readonly Dictionary<string, object?> cache = new(StringComparer.Ordinal);

        [Fact]
        public async Task SignedOutConnectionIsRefused()
        {
            var service = Service();
            var accessToken = AccessToken();
            Assert.NotNull(await service.VerifyAccessTokenAsync(accessToken));
            Assert.False(await service.IsSignedOutAsync(GamaToken));

            var signedOut = await service.SignOutAsync(accessToken);

            Assert.Equal(OperationResult.Succeeded, signedOut.OperationResult);
            Assert.Null(await service.VerifyAccessTokenAsync(accessToken));
            Assert.True(await service.IsSignedOutAsync(GamaToken));
        }

        [Fact]
        public async Task UnconfirmedLogoutIsReportedButStillSignsOut()
        {
            var service = Service(logoutSucceeds: false);
            var accessToken = AccessToken();

            var signedOut = await service.SignOutAsync(accessToken);

            Assert.Equal(OperationResult.Failed, signedOut.OperationResult);
            Assert.Equal("logoutFailed", signedOut.Errors?.FirstOrDefault().Reference);
            Assert.Null(await service.VerifyAccessTokenAsync(accessToken));
        }

        [Fact]
        public async Task UnreadableDenyListFailsClosed()
        {
            var service = Service(cacheDown: true);
            Assert.True(await service.IsSignedOutAsync(GamaToken));
            Assert.Null(await service.VerifyAccessTokenAsync(AccessToken()));
        }

        private McpAuthorizationService Service(bool logoutSucceeds = true, bool cacheDown = false) => new(
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
            new(() => Fake.Create<IIdentityService>(new()
            {
                ["LegacyLogoutAsync"] = _ => Task.FromResult(logoutSucceeds
                    ? new ResultData<Void>(OperationResult.Succeeded)
                    : new ResultData<Void>(OperationResult.Failed) { Errors = [new() { Message = "HTTP 502" }] }),
            })),
            new(() => Fake.Create<ITokenService>(new()
            {
                ["VerifyLegacyTokenAsync"] = _ => Task.FromResult<VerifyTokenResponse?>(new() { Claims = [new Claim(ClaimTypes.NameIdentifier, "1")] }),
            })));

        /// <summary>An MCP access token as the token endpoint issues it, carrying <see cref="GamaToken"/>.</summary>
        private string AccessToken() => dataProtection.CreateProtector("GamaEdtech.Mcp.AccessToken").ToTimeLimitedDataProtector()
            .Protect(JsonSerializer.Serialize(new { GamaToken, ClientId = "client" }), DateTimeOffset.UtcNow.AddDays(1));

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
