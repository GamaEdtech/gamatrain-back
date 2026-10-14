namespace GamaEdtech.Test.Presentation.Api
{
    using System.Reflection;

    using GamaEdtech.Presentation.Api.Controllers;

    using Microsoft.AspNetCore.Authorization;

    using Xunit;

    /// <summary>
    /// The exam page loads the export prices for every visitor. When the endpoint required sign-in, a guest got a 401,
    /// which opens the login dialog the moment the page loads (2026-10-14). Guests see prices; only "purchased" needs a user.
    /// </summary>
    public class ExamExportPricesAccessTests
    {
        [Fact]
        public void ExportPricesIsOpenToGuests()
        {
            var action = typeof(ExamsController).GetMethod(nameof(ExamsController.GetExportPrices));

            Assert.NotNull(action);
            Assert.NotNull(action!.GetCustomAttribute<AllowAnonymousAttribute>());
        }
    }
}
