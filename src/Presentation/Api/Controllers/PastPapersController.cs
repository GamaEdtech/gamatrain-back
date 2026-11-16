namespace GamaEdtech.Presentation.Api.Controllers
{
    using System.Text.Json.Nodes;

    using Asp.Versioning;

    using GamaEdtech.Application.Interface;
    using GamaEdtech.Common.Core;
    using GamaEdtech.Common.Data;
    using GamaEdtech.Common.Identity;

    using Microsoft.AspNetCore.Mvc;

    /// <summary>
    /// A teacher's own past papers, answered as gama-api answers them (<c>data</c> as it sends it), from gama-api or this
    /// backend as <c>Migration:Routes:PastPapers.Teacher</c> says. Per user, so never cached. See docs/business/past-papers.md,
    /// "A teacher's own papers".
    /// </summary>
    [Route("api/v{version:apiVersion}/[controller]")]
    [ApiController]
    [ApiVersion("1.0")]
    public class PastPapersController(Lazy<ILogger<PastPapersController>> logger, Lazy<ITeacherPastPaperService> teacherPastPaperService)
        : ApiControllerBase<PastPapersController>(logger)
    {
        /// <summary>The caller's papers, newest first (gama-api's <c>GET tests</c> for a teacher: <c>page</c>, <c>perpage</c> up
        /// to 50, and exact filters <c>section</c>, <c>base</c>, <c>lesson</c>, <c>test_type</c>, <c>status</c>...).</summary>
        [HttpGet("mine"), Produces<ApiResponse<JsonObject>>()]
        [Permission(policy: null)]
        public Task<IActionResult<JsonObject>> GetMyPapers() =>
            AnswerAsync(token => teacherPastPaperService.Value.GetPapersAsync(new() { UserId = User.UserId(), Token = token, Query = Request.QueryString.Value }));

        /// <summary>One paper for the edit form (gama-api's <c>GET tests/{id}?for=edit</c>): any status for its owner, with
        /// <c>edit</c> saying what the owner may change. Counts no view.</summary>
        [HttpGet("mine/{id:long}"), Produces<ApiResponse<JsonObject>>()]
        [Permission(policy: null)]
        public Task<IActionResult<JsonObject>> GetMyPaper([FromRoute] long id) =>
            AnswerAsync(token => teacherPastPaperService.Value.GetPaperAsync(new() { UserId = User.UserId(), Token = token, Id = id }));

        private async Task<IActionResult<JsonObject>> AnswerAsync(Func<string, Task<ResultData<JsonObject>>> call)
        {
            try
            {
                Response.Headers.CacheControl = "private,no-store";
                var token = TokenAuthenticationHandler.GetTokenFromHeader(Request);
                if (string.IsNullOrEmpty(token))
                {
                    return Ok<JsonObject>(new(new Error { Message = "Missing Authorization token" }));
                }

                var result = await call(token);
                return Ok<JsonObject>(new(result.Errors) { Data = result.Data });
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return Ok<JsonObject>(new(new Error { Message = exc.Message }));
            }
        }
    }
}
