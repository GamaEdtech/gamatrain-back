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

    using static GamaEdtech.Common.Core.Constants;

    /// <summary>gama-api's public lists and paper data for the site's search, subject directory and paper pages, cached here and by Cloudflare. The same for every
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
        public Task<IActionResult<JsonObject>> Search() => SearchAsync(() => searchService.Value.SearchAsync(Request.Query, typesStats: false));

        /// <summary>gama-api's <c>search/typesstats</c>: how many results each content type has.</summary>
        [HttpGet("typesstats"), Produces<ApiResponse<JsonObject>>()]
        public Task<IActionResult<JsonObject>> GetTypesStats() => SearchAsync(() => searchService.Value.SearchAsync(Request.Query, typesStats: true));

        /// <summary>gama-api's <c>tests/search</c>: the subject directory's paper list (and its other resources).</summary>
        [HttpGet("papers"), Produces<ApiResponse<JsonObject>>()]
        public Task<IActionResult<JsonObject>> SearchPapers() => SearchAsync(() => searchService.Value.SearchPapersAsync(Request.Query));

        /// <summary>gama-api's <c>tests/{id}</c>: one past paper's public data for its page. Fetched anonymously, so it holds nothing per
        /// user.</summary>
        [HttpGet("papers/{id:long}"), Produces<ApiResponse<JsonObject>>()]
        public Task<IActionResult<JsonObject>> GetPaper([FromRoute] long id) => SearchAsync(() => searchService.Value.GetPaperAsync(id));

        /// <summary>The home page's counts for a board and grade in one answer: <c>paper</c>, <c>studyMaterials</c>, <c>exam</c>,
        /// <c>tutorial</c>, <c>teacher</c> and <c>school</c>. A count that couldn't be fetched is missing.</summary>
        [HttpGet("homestats"), Produces<ApiResponse<JsonObject>>()]
        public Task<IActionResult<JsonObject>> GetHomeStats([FromQuery] string? section, [FromQuery(Name = "base")] string? grade)
            => SearchAsync(() => searchService.Value.GetHomeStatsAsync(section, grade));

        private async Task<IActionResult<JsonObject>> SearchAsync(Func<Task<ResultData<JsonObject>>> search)
        {
            try
            {
                var result = await search();

                // Cloudflare keeps a result for 5 minutes and browsers for 1; a failure (or a partial answer) is never cached.
                Response.Headers.CacheControl = result.Data is null || result.OperationResult != OperationResult.Succeeded
                    ? "no-store"
                    : "public,max-age=60,s-maxage=300";
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
