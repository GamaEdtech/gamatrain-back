namespace GamaEdtech.Test.ExamImport
{
    using System.Security.Claims;
    using System.Text.Json;

    using GamaEdtech.Application.Interface;
    using GamaEdtech.Application.Service;
    using GamaEdtech.Common.Caching;
    using GamaEdtech.Common.Data;
    using GamaEdtech.Common.Identity;
    using GamaEdtech.Data.Dto.Identity;
    using GamaEdtech.Data.Dto.Mcp;

    using Microsoft.AspNetCore.DataProtection;
    using Microsoft.AspNetCore.Http;
    using Microsoft.Extensions.Configuration;
    using Microsoft.Extensions.Logging.Abstractions;

    using Xunit;

    using static GamaEdtech.Common.Core.Constants;

    using Fake = GamaEdtech.Test.ExamImport.McpSignOutTests.Fake;

    // The MCP sign-in checks this backend's password and keeps the bearer token it mints as it is ({userId}|{token}): the
    // first native version prefixed the id again, so every teacher was refused.
    public class McpSignInTests
    {
        private readonly EphemeralDataProtectionProvider dataProtection = new();

        [Fact]
        public async Task ATeacherGetsACode()
        {
            var result = await Service(role: "Teacher").SignInAsync(Request());

            Assert.Equal(OperationResult.Succeeded, result.OperationResult);
            Assert.Contains("code=", result.Data!.RedirectUri!.Query, StringComparison.Ordinal);
        }

        [Fact]
        public async Task AStudentIsRefused()
        {
            var result = await Service(role: "Student").SignInAsync(Request());

            Assert.Equal("access_denied", result.Errors!.Single().Reference);
        }

        private McpSignInRequestDto Request() => new()
        {
            Identity = "teacher@example.com",
            Password = "secret",
            Request = dataProtection.CreateProtector("GamaEdtech.Mcp.AuthorizationRequest").ToTimeLimitedDataProtector()
                .Protect(JsonSerializer.Serialize(new McpAuthorizationRequestDto
                {
                    ClientId = "client",
                    RedirectUri = "https://client.example/callback",
                    CodeChallenge = "challenge",
                    CodeChallengeMethod = "S256",
                }), DateTimeOffset.UtcNow.AddMinutes(10)),
        };

        private McpAuthorizationService Service(string role) => new(
            new(() => null!),
            new(() => new HttpContextAccessor()),
            new(() => null!),
            new(() => NullLogger<McpAuthorizationService>.Instance),
            new(() => new ConfigurationBuilder().Build()),
            new(() => dataProtection),
            new(() => Fake.Create<ICacheProvider>(new() { ["SetAsync"] = _ => Task.CompletedTask })),
            new(() => Fake.Create<IIdentityService>(new()
            {
                ["AuthenticateAsync"] = _ => Task.FromResult(new ResultData<AuthenticationResponseDto>(OperationResult.Succeeded) { Data = new() { User = new() { Id = 14 } } }),
                ["GenerateUserTokenAsync"] = _ => Task.FromResult(new ResultData<GenerateUserTokenResponseDto>(OperationResult.Succeeded)
                {
                    Data = new() { UserId = 14, Token = "14|token", ExpirationTime = DateTimeOffset.UtcNow.AddDays(10) },
                }),
            })),
            new(() => Fake.Create<ITokenService>(new()
            {
                ["VerifyTokenAsync"] = args => Task.FromResult<VerifyTokenResponse?>(args[0] is VerifyTokenRequest { UserId: "14", Token: "token" }
                    ? new() { Claims = [new Claim(ClaimTypes.NameIdentifier, "14"), new Claim(ClaimTypes.Role, role)] }
                    : null),
            })));
    }
}
