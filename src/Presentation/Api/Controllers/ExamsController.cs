namespace GamaEdtech.Presentation.Api.Controllers
{
    using System.Diagnostics.CodeAnalysis;
    using System.Threading.Tasks;

    using GamaEdtech.Application.Interface;
    using GamaEdtech.Common.Core;
    using GamaEdtech.Common.Data;
    using GamaEdtech.Common.Identity;
    using GamaEdtech.Presentation.ViewModel.Content;
    using GamaEdtech.Presentation.ViewModel.Exam;

    using Microsoft.AspNetCore.Mvc;
    using Microsoft.Extensions.Logging;

    using static GamaEdtech.Common.Core.Constants;

    [Route("api/v{version:apiVersion}/[controller]")]
    [ApiController]
    [Permission(policy: null)]
    public class ExamsController(Lazy<ILogger<ExamsController>> logger, Lazy<IExamService> examService)
        : ApiControllerBase<ExamsController>(logger)
    {
        [HttpGet("export"), Produces(typeof(IActionResult))]
        public async Task<IActionResult> Export([NotNull][FromQuery] ExportExamRequestViewModel request)
        {
            try
            {
                var token = TokenAuthenticationHandler.GetTokenFromHeader(Request);
                if (string.IsNullOrEmpty(token))
                {
                    return Ok<Void>(new(new Error { Message = "Missing Authorization token" }));
                }

                var result = await examService.Value.ExportExamAsync(new()
                {
                    UserId = User.UserId(),
                    SecretKey = token,
                    ExamId = request.Id.GetValueOrDefault(),
                    FileType = request.FileType!,
                    Watermark = request.Watermark,
                    Duration = request.Duration,
                    GoogleDocsCompatible = request.GoogleDocsCompatible.GetValueOrDefault(),
                    Url = $"{Request.Scheme}://{Request.Host.ToString().TrimEnd('/')}",
                });
                if (result.OperationResult is not OperationResult.Succeeded)
                {
                    // A refused charge answers like POST downloads does, so the client's insufficient-balance/upgrade
                    // handling works unchanged; anything else is a plain error.
                    return result.Data?.Charge is { } refused
                        ? Ok<DownloadContentResponseViewModel>(new(result.Errors)
                        {
                            Data = new()
                            {
                                Spent = false,
                                Reason = refused.Reason,
                                CurrentSubscriptionId = refused.CurrentSubscriptionId,
                                CurrentPlanId = refused.CurrentPlanId,
                                CurrentPlanTitle = refused.CurrentPlanTitle,
                                UpgradeSuggestions = UpgradeSuggestionMapper.Map(refused.UpgradeSuggestions),
                                AvailableBillingIntervals = refused.AvailableBillingIntervals,
                            },
                        })
                        : Ok<Void>(new(result.Errors));
                }

                System.Net.Mime.ContentDisposition disposition = new()
                {
                    FileName = $"{result.Data!.FileName}{request.FileType!.Extension}",
                    Inline = false,
                };
                Response.Headers.Append("Content-Disposition", disposition.ToString());
                Response.Headers.Append("X-Content-Type-Options", "nosniff");
                // The charge's outcome, for the client's "spent" feedback (exposed via CORS in Startup).
                Response.Headers.Append("X-Export-Points", result.Data.Points.ToString(System.Globalization.CultureInfo.InvariantCulture));
                Response.Headers.Append("X-Export-Paid-By", result.Data.Charge?.PaidBy?.Name ?? string.Empty);
                Response.Headers.Append("X-Export-Already-Purchased", result.Data.AlreadyPurchased ? "true" : "false");

                return new FileContentResult(result!.Data!.Content!, request.FileType!.ContentType);
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return Ok<Void>(new(new Error { Message = exc.Message }));
            }
        }

        /// <summary>The exam's export price per paid format (Pdf, Word, PowerPoint) for the caller, and which they already
        /// bought -- to show on the download buttons before anything is generated. Prices follow
        /// <c>ExamExportPricing</c>: question count x the format's admin-set multiplier.</summary>
        [HttpGet("export/prices"), Produces(typeof(ApiResponse<ExportPricesResponseViewModel>))]
        public async Task<IActionResult<ExportPricesResponseViewModel>> GetExportPrices([NotNull][FromQuery] ExportPricesRequestViewModel request)
        {
            try
            {
                var result = await examService.Value.GetExportPricesAsync(User.UserId(), request.Id.GetValueOrDefault(), TokenAuthenticationHandler.GetTokenFromHeader(Request));
                return Ok<ExportPricesResponseViewModel>(new(result.Errors)
                {
                    Data = result.Data is null ? null : new()
                    {
                        QuestionCount = result.Data.QuestionCount,
                        Items = result.Data.Items?.Select(t => new ExportPriceViewModel
                        {
                            FileType = t.FileType.Name,
                            Points = t.Points,
                            Purchased = t.Purchased,
                        }),
                    },
                });
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return Ok<ExportPricesResponseViewModel>(new(new Error { Message = exc.Message }));
            }
        }
    }
}
