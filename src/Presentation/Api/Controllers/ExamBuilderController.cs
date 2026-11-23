namespace GamaEdtech.Presentation.Api.Controllers
{
    using System.Diagnostics.CodeAnalysis;

    using Asp.Versioning;

    using GamaEdtech.Application.Interface;
    using GamaEdtech.Common.Core;
    using GamaEdtech.Common.Data;
    using GamaEdtech.Common.Identity;
    using GamaEdtech.Data.Dto.ExamBuilder;
    using GamaEdtech.Data.Dto.ExamImport;
    using GamaEdtech.Domain.Enumeration;
    using GamaEdtech.Presentation.ViewModel.ExamBuilder;

    using Microsoft.AspNetCore.Mvc;

    using Void = Common.Data.Void;

    /// <summary>
    /// A teacher building exams (the website's test maker, the exam bots): their draft, its questions, publishing, and the
    /// question bank to pick from. Teachers and admins only; everything acts for the signed-in user. Answered from gama-api or
    /// this backend as <c>Migration:Routes:Exams.TestMaker</c> (and <c>WriteRoutes</c>) say. Per user, never cached. See
    /// docs/business/exams-migration.md, "Exam builder API".
    /// </summary>
    [Route("api/v{version:apiVersion}/[controller]")]
    [ApiController]
    [ApiVersion("1.0")]
    [Permission(Roles = [nameof(Role.Teacher), nameof(Role.Admin)])]
    public class ExamBuilderController(Lazy<ILogger<ExamBuilderController>> logger, Lazy<IExamBuilderService> examBuilderService)
        : ApiControllerBase<ExamBuilderController>(logger)
    {
        private const int MaxImageBytes = 5 * 1024 * 1024;

        /// <summary>The caller's unpublished draft (a teacher has one at a time), or null.</summary>
        [HttpGet("current"), Produces<ApiResponse<long?>>()]
        public Task<IActionResult<long?>> GetCurrentDraft() =>
            AnswerAsync(token => examBuilderService.Value.GetCurrentDraftAsync(User.UserId(), token));

        /// <summary>The caller's exams, newest first; <c>statuses</c> (gama-api's: 6 draft, 7 published, 1 confirmed...) filters.</summary>
        [HttpGet("exams"), Produces<ApiResponse<ListDataSource<ExamImportDraftDto>>>()]
        public Task<IActionResult<ListDataSource<ExamImportDraftDto>>> GetExams([FromQuery] int[]? statuses, [FromQuery] int page = 1, [FromQuery] int pageSize = 20) =>
            AnswerAsync(token => examBuilderService.Value.GetExamsAsync(User.UserId(), token, statuses ?? [], page, pageSize));

        [HttpGet("exams/{id:long}"), Produces<ApiResponse<ExamImportDraftDto>>()]
        public Task<IActionResult<ExamImportDraftDto>> GetExam([FromRoute] long id) =>
            AnswerAsync(token => examBuilderService.Value.GetExamAsync(User.UserId(), token, id));

        /// <summary>Creates the caller's draft exam (<c>alreadyInProgress</c> while they have one). Its id.</summary>
        [HttpPost("exams"), Produces<ApiResponse<long>>()]
        public Task<IActionResult<long>> CreateExam([NotNull, FromBody] ExamBuilderExamRequestViewModel request) =>
            AnswerAsync(token => examBuilderService.Value.SaveExamAsync(ExamRequest(token, null, request)));

        [HttpPut("exams/{id:long}"), Produces<ApiResponse<long>>()]
        public Task<IActionResult<long>> EditExam([FromRoute] long id, [NotNull, FromBody] ExamBuilderExamRequestViewModel request) =>
            AnswerAsync(token => examBuilderService.Value.SaveExamAsync(ExamRequest(token, id, request)));

        /// <summary>Hides the caller's exam (its results stay).</summary>
        [HttpDelete("exams/{id:long}"), Produces<ApiResponse<Void>>()]
        public Task<IActionResult<Void>> DeleteExam([FromRoute] long id) =>
            AnswerAsync(token => examBuilderService.Value.DeleteExamAsync(User.UserId(), token, id));

        /// <summary>The exam's questions in order, with their options, answers and images.</summary>
        [HttpGet("exams/{id:long}/questions"), Produces<ApiResponse<IEnumerable<ExamImportDraftQuestionDto>>>()]
        public Task<IActionResult<IEnumerable<ExamImportDraftQuestionDto>>> GetExamQuestions([FromRoute] long id) =>
            AnswerAsync(token => examBuilderService.Value.GetExamQuestionsAsync(User.UserId(), token, id));

        /// <summary>Replaces the exam's whole question list, in this order.</summary>
        [HttpPut("exams/{id:long}/questions"), Produces<ApiResponse<Void>>()]
        public Task<IActionResult<Void>> SetExamQuestions([FromRoute] long id, [NotNull, FromBody] ExamBuilderQuestionsRequestViewModel request) =>
            AnswerAsync(token => examBuilderService.Value.SetExamQuestionsAsync(User.UserId(), token, id, request.QuestionIds));

        /// <summary>Publishes the caller's exam (public at once; an admin confirming it publishes its own questions too).</summary>
        [HttpPost("exams/{id:long}/publish"), Produces<ApiResponse<Void>>()]
        public Task<IActionResult<Void>> PublishExam([FromRoute] long id) =>
            AnswerAsync(token => examBuilderService.Value.PublishExamAsync(User.UserId(), token, id));

        /// <summary>The question bank: published questions and the caller's own, newest first, with the correct option.</summary>
        [HttpGet("questions"), Produces<ApiResponse<ListDataSource<ExamBankQuestionDto>>>()]
        public Task<IActionResult<ListDataSource<ExamBankQuestionDto>>> SearchQuestions([FromQuery] int? boardId, [FromQuery] int? gradeId,
            [FromQuery] int? subjectId, [FromQuery] int? topicId, [FromQuery] int page = 1, [FromQuery] int pageSize = 20) =>
            AnswerAsync(token => examBuilderService.Value.SearchBankAsync(new()
            {
                SecretKey = token,
                UserId = User.UserId(),
                BoardId = boardId,
                GradeId = gradeId,
                SubjectId = subjectId,
                TopicId = topicId,
                Page = page,
                PageSize = pageSize,
            }));

        /// <summary>Adds a question, the caller's, waiting for review. Its id.</summary>
        [HttpPost("questions"), Produces<ApiResponse<long>>()]
        public Task<IActionResult<long>> CreateQuestion([NotNull, FromBody] ExamBuilderQuestionRequestViewModel request) =>
            AnswerAsync(token => examBuilderService.Value.SaveQuestionAsync(QuestionRequest(token, null, request)));

        [HttpPut("questions/{id:long}"), Produces<ApiResponse<long>>()]
        public Task<IActionResult<long>> EditQuestion([FromRoute] long id, [NotNull, FromBody] ExamBuilderQuestionRequestViewModel request) =>
            AnswerAsync(token => examBuilderService.Value.SaveQuestionAsync(QuestionRequest(token, id, request)));

        /// <summary>Deletes the caller's question, unless a published exam uses it or it has answers (<c>inUse</c>).</summary>
        [HttpDelete("questions/{id:long}"), Produces<ApiResponse<Void>>()]
        public Task<IActionResult<Void>> DeleteQuestion([FromRoute] long id) =>
            AnswerAsync(token => examBuilderService.Value.DeleteQuestionAsync(User.UserId(), token, id));

        /// <summary>Stores a question image (PNG, JPEG, GIF or WebP, up to 5 MB); the key a question's image fields take.</summary>
        [HttpPost("uploads"), Produces<ApiResponse<string>>()]
        [RequestSizeLimit(MaxImageBytes + (64 * 1024))]
        public Task<IActionResult<string>> Upload([NotNull] IFormFile file) =>
            AnswerAsync(async token =>
            {
                using MemoryStream content = new();
                await file.CopyToAsync(content);
                return await examBuilderService.Value.UploadImageAsync(new()
                {
                    SecretKey = token,
                    UserId = User.UserId(),
                    FileName = file.FileName,
                    ContentType = string.IsNullOrEmpty(file.ContentType) ? "application/octet-stream" : file.ContentType,
                    Content = content.ToArray(),
                });
            });

        private SaveExamRequestDto ExamRequest(string token, long? id, ExamBuilderExamRequestViewModel request) => new()
        {
            SecretKey = token,
            UserId = User.UserId(),
            ExamId = id,
            BoardId = request.BoardId,
            GradeId = request.GradeId,
            CourseId = request.CourseId,
            SubjectId = request.SubjectId,
            PaperId = request.PaperId,
            DurationMinutes = request.DurationMinutes,
            Title = request.Title ?? string.Empty,
            NegativeMarking = request.NegativeMarking,
            Level = request.Level,
            Year = request.Year,
            SessionMonth = request.SessionMonth,
            PastPaperId = request.PastPaperId,
        };

        private SaveExamTestRequestDto QuestionRequest(string token, long? id, ExamBuilderQuestionRequestViewModel request) => new()
        {
            SecretKey = token,
            UserId = User.UserId(),
            Id = id,
            BoardId = request.BoardId,
            GradeId = request.GradeId,
            CourseId = request.CourseId,
            SubjectId = request.SubjectId,
            TopicId = request.TopicId,
            Level = request.Level,
            Type = request.Type ?? string.Empty,
            QuestionHtml = request.QuestionHtml ?? string.Empty,
            OptionsHtml = request.OptionsHtml,
            CorrectOption = request.CorrectOption,
            AnswerHtml = request.AnswerHtml,
            Resource = request.Resource,
            ImageOptions = request.ImageOptions,
            QuestionFile = request.QuestionFile,
            OptionFiles = request.OptionFiles,
            AnswerFile = request.AnswerFile,
        };

        private async Task<IActionResult<T>> AnswerAsync<T>(Func<string, Task<ResultData<T>>> call)
        {
            try
            {
                Response.Headers.CacheControl = "private,no-store";
                var result = await call(TokenAuthenticationHandler.GetTokenFromHeader(Request) ?? string.Empty);
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
