namespace GamaEdtech.Presentation.Api.Controllers
{
    using System.Diagnostics.CodeAnalysis;
    using System.Threading.Tasks;

    using GamaEdtech.Application.Interface;
    using GamaEdtech.Common.Core;
    using GamaEdtech.Common.Data;
    using GamaEdtech.Common.Identity;
    using GamaEdtech.Data.Dto.Exam;
    using GamaEdtech.Presentation.ViewModel.Content;
    using GamaEdtech.Presentation.ViewModel.Exam;

    using Microsoft.AspNetCore.Authentication;
    using Microsoft.AspNetCore.Authorization;
    using Microsoft.AspNetCore.Mvc;
    using Microsoft.Extensions.Logging;

    using static GamaEdtech.Common.Core.Constants;

    [Route("api/v{version:apiVersion}/[controller]")]
    [ApiController]
    [Permission(policy: null)]
    public class ExamsController(Lazy<ILogger<ExamsController>> logger, Lazy<IExamService> examService)
        : ApiControllerBase<ExamsController>(logger)
    {
        /// <summary>Everything the public exam page shows, in one call: gama-api's exam details as it sends them (<c>exam</c>),
        /// the first few questions without their answers (<c>sampleQuestions</c>) and the past paper it was made from
        /// (<c>paper</c>), so search engines see real content. Public; a token is optional and only adds the caller's own
        /// fields to <c>exam</c>.</summary>
        [HttpGet("{id:long}"), Produces(typeof(ApiResponse<ExamPageResponseViewModel>))]
        [AllowAnonymous]
        public async Task<IActionResult<ExamPageResponseViewModel>> GetExamPage([FromRoute] long id)
        {
            try
            {
                var result = await examService.Value.GetExamPageAsync(id, TokenAuthenticationHandler.GetTokenFromHeader(Request));
                return Ok<ExamPageResponseViewModel>(new(result.Errors) { Data = result.Data is null ? null : ToViewModel(result.Data) });
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return Ok<ExamPageResponseViewModel>(new(new Error { Message = exc.Message }));
            }
        }

        private static ExamPageResponseViewModel ToViewModel(ExamPageDto page)
        {
            ExamPaperLinkViewModel? paper = null;
            if (page.Paper is not null)
            {
                paper = new() { Id = page.Paper.Id, Title = page.Paper.Title, Slug = page.Paper.Slug, IsPastPaper = page.Paper.IsPastPaper };
            }

            return new()
            {
                Exam = page.Exam,
                SampleQuestions = page.SampleQuestions.Select(t => new ExamSampleQuestionViewModel
                {
                    Id = t.Id,
                    Code = t.Code,
                    Question = t.Question,
                    Image = t.Image,
                    Options = t.Options.Select(o => new ExamSampleOptionViewModel { Key = o.Key, Text = o.Text, Image = o.Image }),
                }),
                Paper = paper,
            };
        }

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

                Response.Headers.Append("X-Content-Type-Options", "nosniff");
                // The charge's outcome, for the client's "spent" feedback (exposed via CORS in Startup).
                Response.Headers.Append("X-Export-Points", result.Data!.Points.ToString(System.Globalization.CultureInfo.InvariantCulture));
                Response.Headers.Append("X-Export-Paid-By", result.Data.Charge?.PaidBy?.Name ?? string.Empty);
                Response.Headers.Append("X-Export-Already-Purchased", result.Data.AlreadyPurchased ? "true" : "false");

                // FileDownloadName writes Content-Disposition as `attachment; filename="<ASCII fallback>"; filename*=UTF-8''<name>`
                // (RFC 6266), which browsers decode. System.Net.Mime.ContentDisposition, used here before, is the email one:
                // for a non-ASCII title (e.g. the em dash in MCP-imported titles) it wrote an RFC 2047 =?utf-8?B?...?= name,
                // which browsers save literally, without the extension.
                return new FileContentResult(result.Data.Content!, request.FileType!.ContentType)
                {
                    FileDownloadName = $"{result.Data.FileName}{request.FileType.Extension}",
                };
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return Ok<Void>(new(new Error { Message = exc.Message }));
            }
        }

        /// <summary>The exam's export price per paid format (Pdf, Word, PowerPoint) for the caller, and which they already
        /// bought -- to show on the download buttons before anything is generated. Prices follow
        /// <c>ExamExportPricing</c>: question count x the format's admin-set multiplier. Open to guests, who see the
        /// prices with nothing marked purchased; requiring sign-in made the exam page open the login dialog on load.</summary>
        [HttpGet("export/prices"), Produces(typeof(ApiResponse<ExportPricesResponseViewModel>))]
        [AllowAnonymous]
        public async Task<IActionResult<ExportPricesResponseViewModel>> GetExportPrices([NotNull][FromQuery] ExportPricesRequestViewModel request)
        {
            try
            {
                // [AllowAnonymous] skips the token scheme, so User is empty even for a signed-in caller: authenticate
                // explicitly. A valid token gets that user's purchases; no token or an expired one is a guest.
                var authentication = await HttpContext.AuthenticateAsync(PermissionConstants.TokenAuthenticationScheme);
                var userId = authentication.Succeeded ? authentication.Principal.UserId() : 0;

                var result = await examService.Value.GetExportPricesAsync(userId, request.Id.GetValueOrDefault(), TokenAuthenticationHandler.GetTokenFromHeader(Request));
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
