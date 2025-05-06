namespace GamaEdtech.Common.Localization
{
    using System;
    using System.Diagnostics.CodeAnalysis;
    using System.Globalization;
    using System.Threading.Tasks;

    using GamaEdtech.Common.Core;
    using GamaEdtech.Common.Core.Extensions.Collections;

    using Microsoft.AspNetCore.Http;
    using Microsoft.AspNetCore.Localization;

    public class RouteValueRequestCultureProvider(CultureInfo[] cultures) : IRequestCultureProvider
    {
        private readonly CultureInfo[] cultures = cultures;

        public Task<ProviderCultureResult?> DetermineProviderCultureResult([NotNull] HttpContext httpContext)
        {
            var path = httpContext.Request.Path;

            if (string.IsNullOrEmpty(path))
            {
                return Task.FromResult<ProviderCultureResult?>(new ProviderCultureResult(Constants.DefaultLanguageCode));
            }

            var routeValues = httpContext.Request.Path.Value?.Split('/');
            if (routeValues is null || routeValues.Length <= 1)
            {
                return Task.FromResult<ProviderCultureResult?>(new ProviderCultureResult(Constants.DefaultLanguageCode));
            }

            var exists = cultures.Exists(t => t.Name.Equals(routeValues[1], StringComparison.OrdinalIgnoreCase));
            return !exists
                ? Task.FromResult<ProviderCultureResult?>(new ProviderCultureResult(Constants.DefaultLanguageCode))
                : Task.FromResult<ProviderCultureResult?>(new ProviderCultureResult(routeValues[1]));
        }
    }
}
