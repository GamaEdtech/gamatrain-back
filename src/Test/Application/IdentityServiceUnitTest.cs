namespace GamaEdtech.Test.Application
{
    using System;
    using System.Threading.Tasks;

    using GamaEdtech.Application.Interface;
    using GamaEdtech.Common.Data;
    using GamaEdtech.Presentation.Api.Controllers;
    using GamaEdtech.Presentation.ViewModel.Identity;

    using Microsoft.Extensions.DependencyInjection;
    using Microsoft.Extensions.Logging;

    using Xunit;

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
            new(logger, lazyIdentityService, Services.Value!.GetRequiredService<Lazy<IReferralService>>());

        [Fact]
        public async Task RegisterNormalUserShouldSucceed()
        {
            // Arrange
            var controller = GetController();
            var request = new RegistrationRequestViewModel
            {
                Email = "testuser@example.com",
                Password = "ValidPassword123!"
            };

            // Act
            var result = await controller.Register(request);

            // Assert
            var okResult = Assert.IsType<OkObjectResult<Common.Data.Void>>(result);
            var apiResponse = Assert.IsType<ApiResponse<Common.Data.Void>>(okResult.Value);

            Assert.True(apiResponse.Succeeded);
        }

        [Theory]
        [InlineData("", "password123!")]
        [InlineData("user@example.com", "")]
        public async Task RegisterEmptyUsernameOrPasswordShouldFail(string email, string password)
        {
            // Arrange
            var controller = GetController();
            var request = new RegistrationRequestViewModel
            {
                Email = email,
                Password = password
            };

            // Act
            var result = await controller.Register(request);

            // Assert
            var okResult = Assert.IsType<OkObjectResult<Common.Data.Void>>(result);
            var apiResponse = Assert.IsType<ApiResponse<Common.Data.Void>>(okResult.Value);

            Assert.False(apiResponse.Succeeded);
        }

        [Theory]
        [InlineData("a")]
        [InlineData("abc")]
        [InlineData("12345")]
        public async Task RegisterPasswordTooShortShouldFail(string shortPassword)
        {
            // Arrange
            var controller = GetController();
            var request = new RegistrationRequestViewModel
            {
                Email = "user@example.com",
                Password = shortPassword
            };

            // Act
            var result = await controller.Register(request);

            // Assert
            var okResult = Assert.IsType<OkObjectResult<Common.Data.Void>>(result);
            var apiResponse = Assert.IsType<ApiResponse<Common.Data.Void>>(okResult.Value);

            Assert.False(apiResponse.Succeeded);

            // Check the error message
            Assert.NotNull(apiResponse.Errors);
            Assert.Contains(apiResponse.Errors,
                e => !string.IsNullOrWhiteSpace(e.Message) &&
                     e.Message.Contains("Passwords must be at least 6 characters.", StringComparison.OrdinalIgnoreCase));
        }


        [Fact]
        public async Task RegisterDuplicateUsernameShouldFail()
        {
            var controller = GetController();
            var request = new RegistrationRequestViewModel
            {
                Email = "duplicate@example.com",
                Password = "Password123!"
            };

            // First call should succeed
            var firstResult = await controller.Register(request);
            var firstOk = Assert.IsType<OkObjectResult<Common.Data.Void>>(firstResult);
            var firstApiResponse = Assert.IsType<ApiResponse<Common.Data.Void>>(firstOk.Value);
            Assert.True(firstApiResponse.Succeeded);

            // Act: try again with same username
            var secondResult = await controller.Register(request);

            var secondOk = Assert.IsType<OkObjectResult<Common.Data.Void>>(secondResult);
            var secondApiResponse = Assert.IsType<ApiResponse<Common.Data.Void>>(secondOk.Value);

            Assert.False(secondApiResponse.Succeeded);

            // Null check before Assert.Contains
            Assert.NotNull(secondApiResponse.Errors);
            Assert.Contains(secondApiResponse.Errors,
                e => e.Message != null && e.Message.Contains("Email 'duplicate@example.com' is already taken.", StringComparison.OrdinalIgnoreCase));
        }
    }
}
