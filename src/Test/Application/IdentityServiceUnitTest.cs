namespace GamaEdtech.Test.Application
{
    using System;
    using System.Threading.Tasks;

    using GamaEdtech.Application.Interface;
    using GamaEdtech.Application.Service;
    using GamaEdtech.Common.Caching;
    using GamaEdtech.Common.Data;
    using GamaEdtech.Presentation.Api.Controllers;
    using GamaEdtech.Presentation.ViewModel.Identity;

    using Microsoft.Extensions.DependencyInjection;
    using Microsoft.Extensions.Logging;

    using Xunit;

    /// <summary>
    /// Signing up (identities/register): since 2026-11-18 it needs the code emailed by register/code, which proves the address,
    /// and answers this backend's token.
    /// </summary>
    public class IdentityControllerUnitTest : TestBase
    {

        private readonly Lazy<ILogger<IdentitiesController>> logger;
        private readonly Lazy<IIdentityService> lazyIdentityService;

        public IdentityControllerUnitTest()
        {
            logger = Services.Value!.GetRequiredService<Lazy<ILogger<IdentitiesController>>>();
            lazyIdentityService = Services.Value!.GetRequiredService<Lazy<IIdentityService>>();
        }

        private IdentitiesController GetController() =>
            new(logger, lazyIdentityService, Services.Value!.GetRequiredService<Lazy<IReferralService>>()
                , Services.Value!.GetRequiredService<Lazy<GamaEdtech.Common.Mvc.Routing.IEndpointDataSource>>()
                , Services.Value!.GetRequiredService<Lazy<IProfileProgressService>>());

        // The code register/code would email, put straight in the cache.
        private static async Task<string> IssueCodeAsync(string email) =>
            (await EmailCode.IssueAsync(Services.Value!.GetRequiredService<ICacheProvider>(), EmailCode.Purpose.Register, email))!;

        private static ApiResponse<GenerateTokenResponseViewModel> Answer(IActionResult<GenerateTokenResponseViewModel> result) =>
            Assert.IsType<ApiResponse<GenerateTokenResponseViewModel>>(Assert.IsType<OkObjectResult<GenerateTokenResponseViewModel>>(result).Value);

        [Fact]
        public async Task RegisterNormalUserShouldSucceed()
        {
            var email = "testuser@example.com";
            var request = new RegistrationRequestViewModel
            {
                Email = email,
                Code = await IssueCodeAsync(email),
                Password = "ValidPassword123!"
            };

            var answer = Answer(await GetController().Register(request));

            Assert.True(answer.Succeeded);
            Assert.False(string.IsNullOrEmpty(answer.Data?.Token));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("000000")]
        public async Task RegisterWithoutTheEmailedCodeShouldFail(string? code)
        {
            var email = "nocode@example.com";
            var issued = await IssueCodeAsync(email);
            var request = new RegistrationRequestViewModel
            {
                Email = email,
                Code = code == "000000" && issued == "000000" ? "111111" : code,
                Password = "ValidPassword123!"
            };

            var answer = Answer(await GetController().Register(request));

            Assert.False(answer.Succeeded);
        }

        [Theory]
        [InlineData("", "password123!")]
        [InlineData("user@example.com", "")]
        public async Task RegisterEmptyUsernameOrPasswordShouldFail(string email, string password)
        {
            var request = new RegistrationRequestViewModel
            {
                Email = email,
                Code = "123456",
                Password = password
            };

            var answer = Answer(await GetController().Register(request));

            Assert.False(answer.Succeeded);
        }

        [Theory]
        [InlineData("a")]
        [InlineData("abc")]
        [InlineData("12345")]
        public async Task RegisterPasswordTooShortShouldFail(string shortPassword)
        {
            var email = $"short{shortPassword?.Length}@example.com";
            var request = new RegistrationRequestViewModel
            {
                Email = email,
                Code = await IssueCodeAsync(email),
                Password = shortPassword
            };

            var answer = Answer(await GetController().Register(request));

            Assert.False(answer.Succeeded);
            Assert.NotNull(answer.Errors);
            Assert.Contains(answer.Errors,
                e => !string.IsNullOrWhiteSpace(e.Message) &&
                     e.Message.Contains("Passwords must be at least 6 characters.", StringComparison.OrdinalIgnoreCase));
        }

        [Fact]
        public async Task RegisterDuplicateUsernameShouldFail()
        {
            var email = "duplicate@example.com";
            var request = new RegistrationRequestViewModel
            {
                Email = email,
                Code = await IssueCodeAsync(email),
                Password = "Password123!"
            };
            Assert.True(Answer(await GetController().Register(request)).Succeeded);

            // An address with an account gets no sign-up code.
            var again = await lazyIdentityService.Value.SendRegistrationCodeAsync(email);

            Assert.NotEqual(GamaEdtech.Common.Core.Constants.OperationResult.Succeeded, again.OperationResult);
            Assert.Contains(again.Errors!, e => e.Message!.Contains("already has an account", StringComparison.OrdinalIgnoreCase));
        }
    }
}
