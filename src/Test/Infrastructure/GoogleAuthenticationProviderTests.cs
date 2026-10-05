namespace GamaEdtech.Test.Infrastructure
{
    using System;
    using System.Collections.Generic;
    using System.Text;
    using System.Threading.Tasks;

    using GamaEdtech.Domain.Entity.Identity;
    using GamaEdtech.Domain.Enumeration;
    using GamaEdtech.Infrastructure.Provider.Authentication;

    using Microsoft.AspNetCore.Identity;
    using Microsoft.Extensions.Configuration;
    using Microsoft.Extensions.Localization;
    using Microsoft.Extensions.Logging.Abstractions;

    using Xunit;

    using static GamaEdtech.Common.Core.Constants;

    // tokens/google used to accept any token whose issuer claim said Google, without checking the signature, so a
    // hand-written token naming any email signed in as that user. Each forged token below must be rejected before any
    // user lookup: the SignInManager here throws if it is ever touched. None of these cases need the network.
    public class GoogleAuthenticationProviderTests
    {
        private const string ClientId = "test-client.apps.googleusercontent.com";

        private static GoogleAuthenticationProvider CreateProvider(string? clientId = ClientId)
        {
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?> { ["Authentication:Google:ClientId"] = clientId })
                .Build();

            return new(
                new(() => NullLogger<GoogleAuthenticationProvider>.Instance),
                new Lazy<SignInManager<ApplicationUser>>(() => throw new InvalidOperationException("a rejected token must never reach the user lookup")),
                new(() => new KeyLocalizer()),
                new(() => configuration));
        }

        private static string Base64Url(string json) =>
            Convert.ToBase64String(Encoding.UTF8.GetBytes(json)).TrimEnd('=').Replace('+', '-').Replace('/', '_');

        private static string ForgedToken(string alg, string signature)
        {
            var exp = DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeSeconds();
            var header = Base64Url($$"""{"alg":"{{alg}}","typ":"JWT"}""");
            var payload = Base64Url($$"""{"iss":"https://accounts.google.com","aud":"{{ClientId}}","email":"victim@example.com","email_verified":true,"exp":{{exp}}}""");
            return $"{header}.{payload}.{signature}";
        }

        public static TheoryData<string> ForgedTokens =>
        [
            ForgedToken("none", string.Empty),
            ForgedToken("HS256", Base64Url("not-a-real-signature")),
            "not-a-jwt",
            string.Empty,
        ];

        [Theory]
        [MemberData(nameof(ForgedTokens))]
        public async Task ForgedTokenIsRejected(string token)
        {
            var result = await CreateProvider().AuthenticateAsync(new() { Username = token, AuthenticationProvider = AuthenticationProvider.Google });

            Assert.Equal(OperationResult.NotValid, result.OperationResult);
            Assert.Null(result.Data);
        }

        [Fact]
        public async Task MissingClientIdRejectsEverything()
        {
            var result = await CreateProvider(clientId: null).AuthenticateAsync(new() { Username = ForgedToken("none", string.Empty), AuthenticationProvider = AuthenticationProvider.Google });

            Assert.Equal(OperationResult.NotValid, result.OperationResult);
        }

        private sealed class KeyLocalizer : IStringLocalizer<GoogleAuthenticationProvider>
        {
            public LocalizedString this[string name] => new(name, name);

            public LocalizedString this[string name, params object[] arguments] => new(name, name);

            public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures) => [];
        }
    }
}
