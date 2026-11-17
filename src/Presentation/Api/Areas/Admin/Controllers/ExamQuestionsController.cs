namespace GamaEdtech.Presentation.Api.Areas.Admin.Controllers
{
    using System.Diagnostics.CodeAnalysis;

    using Asp.Versioning;

    using GamaEdtech.Application.Interface;
    using GamaEdtech.Common.Core;
    using GamaEdtech.Common.Data;
    using GamaEdtech.Common.Identity;
    using GamaEdtech.Data.Dto.ExamAdmin;
    using GamaEdtech.Domain.Enumeration;
    using GamaEdtech.Presentation.ViewModel.ExamAdmin;

    using Microsoft.AspNetCore.Mvc;

    /// <summary>
    /// The admin panel's question bank, on this backend's copy: the list, one question for the edit form, adding, editing, status
    /// and deleting one no exam uses. Changes are refused (<c>notNative</c>) until <c>Migration:WriteRoutes:Exams</c> is
    /// <c>Native</c>. See docs/business/exams-migration.md, "Admin".
    /// </summary>
    [Common.DataAnnotation.Area(nameof(Admin), "Admin")]
    [Route("api/v{version:apiVersion}/[area]/[controller]")]
    [ApiVersion("1.0")]
    [Permission(Roles = [nameof(Role.Admin)])]
    [DelegablePermission]
    public class ExamQuestionsController(Lazy<ILogger<ExamQuestionsController>> logger, Lazy<IExamAdminService> examAdminService)
        : ApiControllerBase<ExamQuestionsController>(logger)
    {
        /// <summary>Questions, latest change first: <c>status</c>, <c>boardId</c>, <c>subjectId</c>, <c>topicId</c>, <c>type</c>,
        /// <c>ownerUserId</c>, <c>text</c> (the question, code, or the id), <c>page</c>, <c>pageSize</c> (max 100).</summary>
        [HttpGet(""), Produces<ApiResponse<ExamQuestionAdminListDto>>()]
        public Task<IActionResult<ExamQuestionAdminListDto>> GetQuestions([FromQuery] byte? status, [FromQuery] int? boardId, [FromQuery] int? subjectId,
            [FromQuery] int? topicId, [FromQuery] byte? type, [FromQuery] long? ownerUserId, [FromQuery] string? text,
            [FromQuery] int page = 1, [FromQuery] int pageSize = 20) =>
            AnswerAsync(() => examAdminService.Value.GetQuestionsAsync(new()
            {
                Status = status,
                BoardId = boardId,
                SubjectId = subjectId,
                TopicId = topicId,
                Type = type,
                OwnerUserId = ownerUserId,
                Text = text,
                Page = page,
                PageSize = pageSize,
            }));

        [HttpGet("{id:long}"), Produces<ApiResponse<ExamQuestionAdminDto>>()]
        public Task<IActionResult<ExamQuestionAdminDto>> GetQuestion([FromRoute] long id) => AnswerAsync(() => examAdminService.Value.GetQuestionAsync(id));

        /// <summary>Adds a question owned by the caller (waiting unless a status is sent); answers its id.</summary>
        [HttpPost(""), Produces<ApiResponse<long>>()]
        public Task<IActionResult<long>> CreateQuestion([NotNull, FromBody] ExamQuestionRequestViewModel request) =>
            AnswerAsync(() => examAdminService.Value.CreateQuestionAsync(User.UserId(), ToDto(request)));

        [HttpPut("{id:long}"), Produces<ApiResponse<bool>>()]
        public Task<IActionResult<bool>> UpdateQuestion([FromRoute] long id, [NotNull, FromBody] ExamQuestionRequestViewModel request) =>
            AnswerAsync(() => examAdminService.Value.UpdateQuestionAsync(id, ToDto(request)));

        [HttpPut("{id:long}/status"), Produces<ApiResponse<bool>>()]
        public Task<IActionResult<bool>> SetQuestionStatus([FromRoute] long id, [NotNull, FromBody] ExamStatusRequestViewModel request) =>
            AnswerAsync(() => examAdminService.Value.SetQuestionStatusAsync(id, request.Status.GetValueOrDefault()));

        /// <summary>Deletes a question no exam uses.</summary>
        [HttpDelete("{id:long}"), Produces<ApiResponse<bool>>()]
        public Task<IActionResult<bool>> DeleteQuestion([FromRoute] long id) => AnswerAsync(() => examAdminService.Value.DeleteQuestionAsync(id));

        private static ExamQuestionEditDto ToDto(ExamQuestionRequestViewModel request) => new()
        {
            BoardId = request.BoardId,
            GradeId = request.GradeId,
            SubjectId = request.SubjectId,
            CoreTopicIds = request.CoreTopicIds,
            Level = request.Level,
            Type = request.Type,
            Question = request.Question,
            AnswerA = request.AnswerA,
            AnswerB = request.AnswerB,
            AnswerC = request.AnswerC,
            AnswerD = request.AnswerD,
            AnswerFull = request.AnswerFull,
            TrueAnswer = request.TrueAnswer,
            Resource = request.Resource,
            Status = request.Status,
        };

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
