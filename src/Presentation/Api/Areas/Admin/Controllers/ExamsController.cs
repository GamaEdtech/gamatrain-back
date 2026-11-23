namespace GamaEdtech.Presentation.Api.Areas.Admin.Controllers
{
    using System.Diagnostics.CodeAnalysis;

    using Asp.Versioning;

    using GamaEdtech.Application.Interface;
    using GamaEdtech.Common.Core;
    using GamaEdtech.Common.Data;
    using GamaEdtech.Common.Data.Enumeration;
    using GamaEdtech.Common.Identity;
    using GamaEdtech.Data.Dto.ExamAdmin;
    using GamaEdtech.Data.Dto.ExamReview;
    using GamaEdtech.Domain.Enumeration;
    using GamaEdtech.Presentation.ViewModel.ExamAdmin;
    using GamaEdtech.Presentation.ViewModel.ExamReview;

    using Microsoft.AspNetCore.Mvc;

    /// <summary>
    /// The admin panel's exams, on this backend's copy: the list, one exam with its questions in order, its settings, status,
    /// delete and question order. Changes are refused (<c>notNative</c>) until <c>Migration:WriteRoutes:Exams</c> is
    /// <c>Native</c>. See docs/business/exams-migration.md, "Admin".
    /// </summary>
    [Common.DataAnnotation.Area(nameof(Admin), "Admin")]
    [Route("api/v{version:apiVersion}/[area]/[controller]")]
    [ApiVersion("1.0")]
    [Permission(Roles = [nameof(Role.Admin)])]
    [DelegablePermission]
    public class ExamsController(Lazy<ILogger<ExamsController>> logger, Lazy<IExamAdminService> examAdminService, Lazy<IExamReviewService> examReviewService)
        : ApiControllerBase<ExamsController>(logger)
    {
        /// <summary>Exams, latest change first: <c>status</c>, <c>boardId</c>, <c>gradeId</c>, <c>subjectId</c>, <c>examTypeId</c>,
        /// <c>ownerUserId</c>, <c>text</c> (title, code, or the id), <c>combination</c> (1 albums, 2 own, 3 mixed), <c>open</c>
        /// (can be taken now), <c>year</c>, <c>includeDeleted</c>, <c>page</c>, <c>pageSize</c> (max 100).</summary>
        [HttpGet(""), Produces<ApiResponse<ExamAdminListDto>>()]
        public Task<IActionResult<ExamAdminListDto>> GetExams([FromQuery] byte? status, [FromQuery] int? boardId, [FromQuery] int? gradeId,
            [FromQuery] int? subjectId, [FromQuery] int? examTypeId, [FromQuery] long? ownerUserId, [FromQuery] string? text,
            [FromQuery] byte? combination, [FromQuery] bool? open, [FromQuery] int? year,
            [FromQuery] bool includeDeleted = false, [FromQuery] int page = 1, [FromQuery] int pageSize = 20) =>
            AnswerAsync(() => examAdminService.Value.GetExamsAsync(new()
            {
                Status = status,
                BoardId = boardId,
                GradeId = gradeId,
                SubjectId = subjectId,
                ExamTypeId = examTypeId,
                OwnerUserId = ownerUserId,
                Text = text,
                IncludeDeleted = includeDeleted,
                Combination = combination,
                Open = open,
                Year = year,
                Page = page,
                PageSize = pageSize,
            }));

        [HttpGet("{id:long}"), Produces<ApiResponse<ExamAdminDto>>()]
        public Task<IActionResult<ExamAdminDto>> GetExam([FromRoute] long id) => AnswerAsync(() => examAdminService.Value.GetExamAsync(id));

        [HttpPut("{id:long}"), Produces<ApiResponse<bool>>()]
        public Task<IActionResult<bool>> UpdateExam([FromRoute] long id, [NotNull, FromBody] ExamEditRequestViewModel request) =>
            AnswerAsync(() => examAdminService.Value.UpdateExamAsync(id, new()
            {
                Title = request.Title,
                BoardId = request.BoardId,
                GradeId = request.GradeId,
                SubjectId = request.SubjectId,
                CoreTopicIds = request.CoreTopicIds,
                ExamTypeId = request.ExamTypeId,
                Level = request.Level,
                DurationMinutes = request.DurationMinutes,
                ScoreType = request.ScoreType,
                EduYear = request.EduYear,
                EduMonth = request.EduMonth,
                StartDate = request.StartDate,
                EndDate = request.EndDate,
            }));

        [HttpPut("{id:long}/status"), Produces<ApiResponse<bool>>()]
        public Task<IActionResult<bool>> SetExamStatus([FromRoute] long id, [NotNull, FromBody] ExamStatusRequestViewModel request) =>
            AnswerAsync(() => examAdminService.Value.SetExamStatusAsync(id, request.Status.GetValueOrDefault()));

        /// <summary>Replaces the exam's questions with these, in this order.</summary>
        [HttpPut("{id:long}/questions"), Produces<ApiResponse<bool>>()]
        public Task<IActionResult<bool>> SetExamQuestions([FromRoute] long id, [NotNull, FromBody] ExamQuestionsRequestViewModel request) =>
            AnswerAsync(() => examAdminService.Value.SetExamQuestionsAsync(id, request.QuestionIds ?? []));

        /// <summary>Hides the exam (its results stay).</summary>
        [HttpDelete("{id:long}"), Produces<ApiResponse<bool>>()]
        public Task<IActionResult<bool>> DeleteExam([FromRoute] long id) => AnswerAsync(() => examAdminService.Value.DeleteExamAsync(id));

        /// <summary>Exam reviews, latest change first: <c>status</c> (Confirmed = shown, Rejected = hidden), <c>examId</c>,
        /// <c>withComment</c>, <c>page</c>, <c>pageSize</c> (max 100). See docs/business/exams-and-content.md, "Exam reviews".</summary>
        [HttpGet("reviews"), Produces<ApiResponse<ExamReviewAdminListDto>>()]
        public Task<IActionResult<ExamReviewAdminListDto>> GetReviews([FromQuery] string? status, [FromQuery] long? examId, [FromQuery] bool? withComment,
            [FromQuery] int page = 1, [FromQuery] int pageSize = 20)
        {
            Status? reviewStatus = null;
            if (!string.IsNullOrWhiteSpace(status))
            {
                reviewStatus = status.TryGetFromNameOrValue<Status, byte>(out var parsed) ? parsed : null;
                if (reviewStatus is null)
                {
                    return Task.FromResult<IActionResult<ExamReviewAdminListDto>>(Ok<ExamReviewAdminListDto>(new(new Error { Message = "Unknown status.", Reference = "status" })));
                }
            }

            return AnswerAsync(() => examReviewService.Value.GetAdminReviewsAsync(new()
            {
                Status = reviewStatus,
                ExamId = examId,
                WithComment = withComment,
                Page = page,
                PageSize = pageSize,
            }));
        }

        /// <summary>Hides a bad review (<c>confirm</c> false, with an optional note for its author) or shows it again (true).</summary>
        [HttpPut("reviews/{id:long}/status"), Produces<ApiResponse<bool>>()]
        public Task<IActionResult<bool>> ModerateReview([FromRoute] long id, [NotNull, FromBody] ExamReviewModerationRequestViewModel request) =>
            AnswerAsync(() => examReviewService.Value.ModerateAsync(id, request.Confirm.GetValueOrDefault(), request.RejectionComment));

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
