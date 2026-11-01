namespace GamaEdtech.Presentation.Api.Controllers
{
    using System.Text.Json.Nodes;

    using Asp.Versioning;

    using GamaEdtech.Application.Interface;
    using GamaEdtech.Common.Core;
    using GamaEdtech.Common.Data;
    using GamaEdtech.Common.Identity;

    using Microsoft.AspNetCore.Authorization;
    using Microsoft.AspNetCore.Cors;
    using Microsoft.AspNetCore.Mvc;

    /// <summary>gama-api's public search for the site's search page, cached here and by Cloudflare. The same for every
    /// visitor: no token or cookie is read, and the CORS answer doesn't depend on the origin, so a cached copy suits all.</summary>
    [Route("api/v{version:apiVersion}/[controller]")]
    [ApiVersion("1.0")]
    [Permission(policy: null)]
    [AllowAnonymous]
    [EnableCors(Startup.PublicCorsPolicy)]
    public class SearchController(Lazy<ILogger<SearchController>> logger, Lazy<ISearchService> searchService)
        : ApiControllerBase<SearchController>(logger)
    {
        /// <summary>gama-api's <c>search</c>, with its query parameters and its <c>data</c> as it sends them.</summary>
        [HttpGet, Produces<ApiResponse<JsonObject>>()]
        public Task<IActionResult<JsonObject>> Search() => SearchAsync(typesStats: false);

        /// <summary>gama-api's <c>search/typesstats</c>: how many results each content type has.</summary>
        [HttpGet("typesstats"), Produces<ApiResponse<JsonObject>>()]
        public Task<IActionResult<JsonObject>> GetTypesStats() => SearchAsync(typesStats: true);

        private async Task<IActionResult<JsonObject>> SearchAsync(bool typesStats)
        {
            try
            {
                var result = await searchService.Value.SearchAsync(Request.Query, typesStats);

                // Cloudflare keeps a result for 5 minutes and browsers for 1; a failure is never cached.
                Response.Headers.CacheControl = result.Data is null ? "no-store" : "public,max-age=60,s-maxage=300";
                return Ok<JsonObject>(new(result.Errors) { Data = result.Data });
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                Response.Headers.CacheControl = "no-store";
                return Ok<JsonObject>(new(new Error { Message = exc.Message }));
            }
        }
    }
}
