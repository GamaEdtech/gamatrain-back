namespace GamaEdtech.Presentation.Api.Areas.Admin.Controllers
{
    using Asp.Versioning;

    using GamaEdtech.Application.Interface;
    using GamaEdtech.Common.Core;
    using GamaEdtech.Common.Data;
    using GamaEdtech.Common.Identity;
    using GamaEdtech.Data.Dto.ExamAdmin;
    using GamaEdtech.Domain.Enumeration;

    using Microsoft.AspNetCore.Mvc;

    /// <summary>
    /// Who took which exam, and the result (<c>ExamAttempts</c>). Admins only and not delegable: it shows participants' names and
    /// emails. See docs/business/exams-and-content.md, "Exam participation (admin)".
    /// </summary>
    [Common.DataAnnotation.Area(nameof(Admin), "Admin")]
    [Route("api/v{version:apiVersion}/[area]/exam-participations")]
    [ApiVersion("1.0")]
    [Permission(Roles = [nameof(Role.Admin)])]
    public class ExamParticipationsController(Lazy<ILogger<ExamParticipationsController>> logger, Lazy<IExamAdminService> examAdminService)
        : ApiControllerBase<ExamParticipationsController>(logger)
    {
        /// <summary>Attempts, newest first: <c>examId</c>, <c>userId</c>, <c>status</c> (0 started, 1 finished), <c>from</c> and
        /// <c>to</c> (start date, <c>to</c> exclusive), <c>text</c> (participant's name, user name or email), <c>page</c>,
        /// <c>pageSize</c> (max 100). Also the totals of all matching attempts.</summary>
        [HttpGet(""), Produces<ApiResponse<ExamParticipationListDto>>()]
        public async Task<IActionResult<ExamParticipationListDto>> GetParticipations([FromQuery] long? examId, [FromQuery] long? userId,
            [FromQuery] byte? status, [FromQuery] DateTimeOffset? from, [FromQuery] DateTimeOffset? to, [FromQuery] string? text,
            [FromQuery] int page = 1, [FromQuery] int pageSize = 20)
        {
            try
            {
                var result = await examAdminService.Value.GetParticipationsAsync(new()
                {
                    ExamId = examId,
                    UserId = userId,
                    Status = status,
                    From = from,
                    ToExclusive = to,
                    Text = text,
                    Page = page,
                    PageSize = pageSize,
                });
                return Ok<ExamParticipationListDto>(new(result.Errors) { Data = result.Data });
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return Ok<ExamParticipationListDto>(new(new Error { Message = exc.Message }));
            }
        }
    }
}
