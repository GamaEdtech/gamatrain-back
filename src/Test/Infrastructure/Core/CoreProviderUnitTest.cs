namespace GamaEdtech.Test.Infrastructure.Core
{
    using GamaEdtech.Infrastructure.Interface;

    using Microsoft.Extensions.DependencyInjection;

    using Xunit;

    using static GamaEdtech.Common.Core.Constants;

    public class CoreProviderUnitTest : TestBase
    {
        [Fact]
        public async Task ValidateTestAsync()
        {
            var service = Services.Value!.GetRequiredService<IExamSource>();
            var response = await service.ValidateTestAsync(new()
            {
                TestId = 1,
                SubmissionId = 1,
                UserId = 1,
            });

            Assert.Equal(OperationResult.Succeeded, response.OperationResult);
        }
    }
}
