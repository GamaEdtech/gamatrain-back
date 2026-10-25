namespace GamaEdtech.Presentation.Api.Controllers
{
    using System.Diagnostics.CodeAnalysis;

    using Asp.Versioning;

    using GamaEdtech.Application.Interface;
    using GamaEdtech.Common.Core;
    using GamaEdtech.Common.Data;
    using GamaEdtech.Common.Identity;
    using GamaEdtech.Data.Dto.ExamAnalysis;
    using GamaEdtech.Presentation.ViewModel.ExamAnalysis;

    using Microsoft.AspNetCore.Mvc;
    using Microsoft.Extensions.Localization;

    [Route("api/v{version:apiVersion}/[controller]")]
    [ApiController]
    [ApiVersion("1.0")]
    [Permission(policy: null)]
    public class ExamAnalysesController(Lazy<ILogger<ExamAnalysesController>> logger, Lazy<IExamAnalysisService> examAnalysisService,
        Lazy<IStringLocalizer<ExamAnalysesController>> localizer)
        : LocalizableApiControllerBase<ExamAnalysesController>(logger, localizer)
    {
        [HttpPost, Produces<ApiResponse<ExamAnalysisResponseViewModel>>()]
        public async Task<IActionResult<ExamAnalysisResponseViewModel>> Prepare([NotNull, FromBody] PrepareExamAnalysisRequestViewModel request)
        {
            try
            {
                var token = TokenAuthenticationHandler.GetTokenFromHeader(Request);
                if (string.IsNullOrEmpty(token))
                {
                    return Ok<ExamAnalysisResponseViewModel>(new(new Error { Message = Localizer.Value["MissingAuthorizationToken"] }));
                }

                var result = await examAnalysisService.Value.PrepareAsync(new()
                {
                    UserId = User.UserId(),
                    ExamId = request.ExamId.GetValueOrDefault(),
                    SecretKey = token,
                    Questions = request.Questions!.Select(question => new ExamAnalysisQuestionDto
                    {
                        Question = question.Question!,
                        Answers = question.Answers!,
                        UserAnswer = question.UserAnswer.GetValueOrDefault(),
                        TrueAnswer = question.TrueAnswer.GetValueOrDefault(),
                    }).ToArray(),
                });

                return Ok<ExamAnalysisResponseViewModel>(new(result.Errors)
                {
                    Data = Map(result.Data),
                });
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return Ok<ExamAnalysisResponseViewModel>(new(new Error { Message = Localizer.Value["OperationFailed"] }));
            }
        }

        private static ExamAnalysisResponseViewModel? Map(PrepareExamAnalysisResponseDto? response) => response is null
            ? null
            : new()
            {
                Id = response.Analysis?.Id,
                ExamId = response.Analysis?.ExamId,
                Result = response.Analysis?.Result,
                Points = response.Analysis?.Points,
                PaymentSucceeded = response.PaymentSucceeded,
                PaidBy = response.Charge?.PaidBy,
                RemainingQuota = response.Charge?.RemainingQuota,
                Reason = response.Charge?.Reason,
                CurrentSubscriptionId = response.Charge?.CurrentSubscriptionId,
                CurrentPlanId = response.Charge?.CurrentPlanId,
                CurrentPlanTitle = response.Charge?.CurrentPlanTitle,
                UpgradeSuggestions = UpgradeSuggestionMapper.Map(response.Charge?.UpgradeSuggestions),
                AvailableBillingIntervals = response.Charge?.AvailableBillingIntervals,
            };
    }
}
