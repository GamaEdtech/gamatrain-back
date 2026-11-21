namespace GamaEdtech.Presentation.Api.Areas.Admin.Controllers
{
    using System.Diagnostics.CodeAnalysis;
    using System.Text.Json.Nodes;

    using Asp.Versioning;

    using GamaEdtech.Application.Interface;
    using GamaEdtech.Common.Core;
    using GamaEdtech.Common.Data;
    using GamaEdtech.Common.Identity;
    using GamaEdtech.Domain.Enumeration;
    using GamaEdtech.Presentation.ViewModel.PastPaper;

    using Microsoft.AspNetCore.Mvc;

    /// <summary>
    /// The admin panel's past papers, from this backend's copy: the list, a paper, adding one for a teacher, editing, deleting,
    /// reviewing and scoring. Changes are refused (<c>notNative</c>) while <c>Migration:WriteRoutes:PastPapers.Teacher</c> is
    /// not <c>Native</c>. Answers are in gama-api's shape, as the teacher's. See docs/business/past-papers.md, "Admin review".
    /// </summary>
    [Common.DataAnnotation.Area(nameof(Admin), "Admin")]
    [Route("api/v{version:apiVersion}/[area]/[controller]")]
    [ApiVersion("1.0")]
    [Permission(Roles = [nameof(Role.Admin)])]
    [DelegablePermission]
    public class PastPapersController(Lazy<ILogger<PastPapersController>> logger, Lazy<IPastPaperAdminService> pastPaperAdminService)
        : ApiControllerBase<PastPapersController>(logger)
    {
        // 50 MB of file plus room for the form around it.
        private const long MaxUploadRequestBytes = (50L * 1024 * 1024) + (1024 * 1024);

        /// <summary>Every paper that is not deleted, newest first: <c>page</c>, <c>perpage</c> (max 50), <c>status</c>,
        /// <c>section</c>, <c>base</c>, <c>lesson</c>, <c>test_type</c>, <c>id</c>, <c>uid</c> (owner), <c>refScore</c>,
        /// <c>title</c>. <c>{list, num}</c>; each item also has <c>user_</c> and <c>owner_name</c>.</summary>
        [HttpGet(""), Produces<ApiResponse<JsonObject>>()]
        public Task<IActionResult<JsonObject>> GetPapers() =>
            AnswerAsync(() => pastPaperAdminService.Value.GetPapersAsync(User.UserId(), Request.QueryString.Value));

        /// <summary>One paper in any status, with full edit rights (<c>edit</c>) and its review note.</summary>
        [HttpGet("{id:long}"), Produces<ApiResponse<JsonObject>>()]
        public Task<IActionResult<JsonObject>> GetPaper([FromRoute] long id) =>
            AnswerAsync(() => pastPaperAdminService.Value.GetPaperAsync(User.UserId(), id));

        /// <summary>A short signed link to a paper's file in any status, for reviewing it: <c>type</c> is <c>pdf</c>, <c>word</c>,
        /// <c>answer</c> or <c>extra</c> (with <c>extraId</c>, from the paper's <c>files.extra</c>). Nothing is charged or counted.
        /// Answers <c>{"url"}</c>.</summary>
        [HttpGet("{id:long}/files/{type}"), Produces<ApiResponse<JsonObject>>()]
        public Task<IActionResult<JsonObject>> GetFileLink([FromRoute] long id, [FromRoute] string type, [FromQuery] long? extraId) =>
            AnswerAsync(() => pastPaperAdminService.Value.GetFileLinkAsync(id, type, extraId));

        /// <summary>Uploads a file for a paper (as the teacher's upload); the add or edit names it by the key.</summary>
        [HttpPost("uploads"), Produces<ApiResponse<JsonNode>>()]
        [RequestSizeLimit(MaxUploadRequestBytes)]
        [RequestFormLimits(MultipartBodyLengthLimit = MaxUploadRequestBytes)]
        public Task<IActionResult<JsonNode>> Upload([NotNull] IFormFile file, [FromQuery] string? kind) =>
            AnswerAsync(async () =>
            {
                using MemoryStream content = new();
                await file.CopyToAsync(content);
                return await pastPaperAdminService.Value.UploadAsync(new()
                {
                    UserId = User.UserId(),
                    Token = string.Empty,
                    FileName = file.FileName,
                    ContentType = string.IsNullOrEmpty(file.ContentType) ? "application/octet-stream" : file.ContentType,
                    Content = content.ToArray(),
                    Kind = kind,
                });
            });

        /// <summary>Adds a paper for a teacher: the teacher's fields plus <c>uid</c> (the teacher's user id) and optionally
        /// <c>price_multiplier</c>. It waits for review.</summary>
        [HttpPost(""), Produces<ApiResponse<JsonNode>>()]
        public Task<IActionResult<JsonNode>> Add([FromBody] JsonObject? body) =>
            AnswerAsync(() => pastPaperAdminService.Value.AddAsync(new() { UserId = User.UserId(), Token = string.Empty, Body = body }));

        /// <summary>Edits any field of a paper (and <c>price_multiplier</c>); its status stays.</summary>
        [HttpPut("{id:long}"), Produces<ApiResponse<JsonNode>>()]
        public Task<IActionResult<JsonNode>> Edit([FromRoute] long id, [FromBody] JsonObject? body) =>
            AnswerAsync(() => pastPaperAdminService.Value.EditAsync(new() { UserId = User.UserId(), Token = string.Empty, Id = id, Body = body }));

        /// <summary>Deletes a paper (hides it) or one of its files, in any status.</summary>
        [HttpDelete("{id:long}/{type?}"), Produces<ApiResponse<JsonNode>>()]
        public Task<IActionResult<JsonNode>> Delete([FromRoute] long id, [FromRoute] string? type) =>
            AnswerAsync(() => pastPaperAdminService.Value.DeleteAsync(new() { UserId = User.UserId(), Token = string.Empty, Id = id, FileType = type }));

        /// <summary>Reviews a paper: its new status and, unless it is confirmed, notes for the teacher.</summary>
        [HttpPut("{id:long}/review"), Produces<ApiResponse<JsonObject>>()]
        public Task<IActionResult<JsonObject>> Review([FromRoute] long id, [NotNull, FromBody] ReviewPastPaperRequestViewModel request) =>
            AnswerAsync(() => pastPaperAdminService.Value.ReviewAsync(new() { Id = id, Status = request.Status.GetValueOrDefault(), Notes = request.Notes }));

        /// <summary>Sets a paper's referee score (0 to 5).</summary>
        [HttpPut("{id:long}/score"), Produces<ApiResponse<JsonObject>>()]
        public Task<IActionResult<JsonObject>> SetScore([FromRoute] long id, [NotNull, FromBody] PastPaperScoreRequestViewModel request) =>
            AnswerAsync(() => pastPaperAdminService.Value.SetScoreAsync(id, request.Score.GetValueOrDefault()));

        private async Task<IActionResult<T>> AnswerAsync<T>(Func<Task<ResultData<T>>> call)
        {
            try
            {
                var result = await call();
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
