namespace GamaEdtech.Presentation.Api.Controllers
{
    using System.Diagnostics.CodeAnalysis;
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
        // 50 MB of file plus room for the form around it.
        private const long MaxUploadRequestBytes = (50L * 1024 * 1024) + (1024 * 1024);

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

        /// <summary>Uploads one of a paper's files, as gama-api's <c>POST upload</c>: <c>[{"file": {"name": key}}]</c>, the key the
        /// add or edit names the file by. <c>kind=pdf</c> marks the question PDF (it also gets its thumbnail). Pdf, Word, and
        /// the extra files' kinds, up to 50 MB.</summary>
        [HttpPost("mine/uploads"), Produces<ApiResponse<JsonNode>>()]
        [Permission(policy: null)]
        [RequestSizeLimit(MaxUploadRequestBytes)]
        [RequestFormLimits(MultipartBodyLengthLimit = MaxUploadRequestBytes)]
        public Task<IActionResult<JsonNode>> Upload([NotNull] IFormFile file, [FromQuery] string? kind) =>
            AnswerAsync(async token =>
            {
                using MemoryStream content = new();
                await file.CopyToAsync(content);
                return await teacherPastPaperService.Value.UploadAsync(new()
                {
                    UserId = User.UserId(),
                    Token = token,
                    FileName = file.FileName,
                    ContentType = string.IsNullOrEmpty(file.ContentType) ? "application/octet-stream" : file.ContentType,
                    Content = content.ToArray(),
                    Kind = kind,
                });
            });

        /// <summary>Adds a paper for the caller, as gama-api's <c>POST tests</c> (its fields, files by their upload keys); it waits
        /// for review. Answers <c>{"id"}</c>.</summary>
        [HttpPost("mine"), Produces<ApiResponse<JsonNode>>()]
        [Permission(policy: null)]
        public Task<IActionResult<JsonNode>> Add([FromBody] JsonObject? body) =>
            AnswerAsync(token => teacherPastPaperService.Value.AddAsync(new() { UserId = User.UserId(), Token = token, Body = body }));

        /// <summary>Edits the caller's paper, as gama-api's <c>PUT tests/{id}</c>. Answers <c>{"id", "status", "editMode"}</c>, and
        /// <c>ignoredFields</c>/<c>message</c> when a limited edit left fields out or the paper goes back to review.</summary>
        [HttpPut("mine/{id:long}"), Produces<ApiResponse<JsonNode>>()]
        [Permission(policy: null)]
        public Task<IActionResult<JsonNode>> Edit([FromRoute] long id, [FromBody] JsonObject? body) =>
            AnswerAsync(token => teacherPastPaperService.Value.EditAsync(new() { UserId = User.UserId(), Token = token, Id = id, Body = body }));

        /// <summary>Deletes the caller's paper (<c>type</c> <c>all</c>, the default) or one of its files (<c>word</c>,
        /// <c>answer</c>, or <c>extra</c> with the extra file's id as <c>id</c>), as gama-api's <c>DELETE tests/{id}[/{type}]</c>,
        /// until it is past review. The PDF is only replaced.</summary>
        [HttpDelete("mine/{id:long}/{type?}"), Produces<ApiResponse<JsonNode>>()]
        [Permission(policy: null)]
        public Task<IActionResult<JsonNode>> Delete([FromRoute] long id, [FromRoute] string? type) =>
            AnswerAsync(token => teacherPastPaperService.Value.DeleteAsync(new() { UserId = User.UserId(), Token = token, Id = id, FileType = type }));

        private async Task<IActionResult<T>> AnswerAsync<T>(Func<string, Task<ResultData<T>>> call)
        {
            try
            {
                Response.Headers.CacheControl = "private,no-store";
                var token = TokenAuthenticationHandler.GetTokenFromHeader(Request);
                if (string.IsNullOrEmpty(token))
                {
                    return Ok<T>(new(new Error { Message = "Missing Authorization token" }));
                }

                var result = await call(token);
                return Ok<T>(new(result.Errors) { Data = result.Data });
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return Ok<T>(new(new Error { Message = exc.Message }));
            }
        }
    }
}
