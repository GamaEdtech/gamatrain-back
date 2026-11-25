namespace GamaEdtech.Presentation.Api.Controllers
{
    using System.Diagnostics.CodeAnalysis;
    using System.Threading.Tasks;

    using GamaEdtech.Application.Interface;
    using GamaEdtech.Common.Core;
    using GamaEdtech.Common.Data;
    using GamaEdtech.Common.Identity;
    using GamaEdtech.Data.Dto.Exam;
    using GamaEdtech.Data.Dto.ExamAnalysis;
    using GamaEdtech.Presentation.ViewModel.Content;
    using GamaEdtech.Presentation.ViewModel.Exam;
    using GamaEdtech.Presentation.ViewModel.ExamAnalysis;

    using Microsoft.AspNetCore.Authentication;
    using Microsoft.AspNetCore.Authorization;
    using GamaEdtech.Presentation.ViewModel.ExamReview;

    using Microsoft.AspNetCore.Mvc;
    using Microsoft.Extensions.Localization;
    using Microsoft.Extensions.Logging;

    using static GamaEdtech.Common.Core.Constants;

    [Route("api/v{version:apiVersion}/[controller]")]
    [ApiController]
    [Permission(policy: null)]
    public class ExamsController(Lazy<ILogger<ExamsController>> logger, Lazy<IExamService> examService,
        Lazy<IExamAnalysisService> examAnalysisService, Lazy<IStringLocalizer<ExamsController>> localizer, Lazy<IGameService> gameService,
        Lazy<IExamReviewService> examReviewService)
        : LocalizableApiControllerBase<ExamsController>(logger, localizer)
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
                var userId = User.UserId();
                var result = await examService.Value.GetExamPageAsync(id, TokenAuthenticationHandler.GetTokenFromHeader(Request), userId > 0 ? userId : null);
                return Ok<ExamPageResponseViewModel>(new(result.Errors) { Data = result.Data is null ? null : ToViewModel(result.Data) });
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return Ok<ExamPageResponseViewModel>(new(new Error { Message = exc.Message }));
            }
        }

        /// <summary>The exam's reviews: the rating summary (average, count, 1-5 breakdown) of the published ones and their comments,
        /// newest first (<c>page</c>, <c>pageSize</c> up to 50). Public; with a token also the caller's own review (any status) and
        /// whether they may write one (they finished the exam). See docs/business/exams-and-content.md, "Exam reviews".</summary>
        [HttpGet("{id:long}/reviews"), Produces(typeof(ApiResponse<ExamReviewsResponseViewModel>))]
        [AllowAnonymous]
        public async Task<IActionResult<ExamReviewsResponseViewModel>> GetReviews([FromRoute] long id, [FromQuery] int page = 1, [FromQuery] int pageSize = 10)
        {
            try
            {
                // [AllowAnonymous] skips the token scheme: authenticate explicitly, so a signed-in caller also gets their own review.
                var authentication = await HttpContext.AuthenticateAsync(PermissionConstants.TokenAuthenticationScheme);
                var userId = authentication.Succeeded ? authentication.Principal.UserId() : 0;
                if (userId > 0)
                {
                    Response.Headers.CacheControl = "private,no-store";
                }

                var result = await examReviewService.Value.GetReviewsAsync(id, userId > 0 ? userId : null, page, pageSize);
                ExamReviewsResponseViewModel? data = null;
                if (result.Data is { } reviews)
                {
                    data = new()
                    {
                        Average = reviews.Average,
                        Count = reviews.Count,
                        Distribution = reviews.Distribution,
                        Items = [.. reviews.Items.Select(ToViewModel)],
                        CommentCount = reviews.CommentCount,
                        Mine = reviews.Mine is null ? null : ToViewModel(reviews.Mine),
                        CanReview = reviews.CanReview,
                    };
                }

                return Ok<ExamReviewsResponseViewModel>(new(result.Errors) { Data = data });
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return Ok<ExamReviewsResponseViewModel>(new(new Error { Message = exc.Message }));
            }
        }

        /// <summary>Saves the caller's review (a 1-5 rating and an optional comment), public at once, only after finishing the
        /// exam (<c>notParticipated</c> otherwise). It can be changed for 30 minutes after posting (<c>editWindowClosed</c>).</summary>
        [HttpPost("{id:long}/reviews"), Produces(typeof(ApiResponse<ExamReviewViewModel>))]
        public async Task<IActionResult<ExamReviewViewModel>> SaveReview([FromRoute] long id, [NotNull, FromBody] SaveExamReviewRequestViewModel request)
        {
            try
            {
                Response.Headers.CacheControl = "private,no-store";
                var result = await examReviewService.Value.SaveReviewAsync(new()
                {
                    UserId = User.UserId(),
                    ExamId = id,
                    Rating = request.Rating.GetValueOrDefault(),
                    Comment = request.Comment,
                });
                return Ok<ExamReviewViewModel>(new(result.Errors) { Data = result.Data is null ? null : ToViewModel(result.Data) });
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return Ok<ExamReviewViewModel>(new(new Error { Message = exc.Message }));
            }
        }


        private static ExamReviewViewModel ToViewModel(Data.Dto.ExamReview.ExamReviewItemDto item) => new()
        {
            Id = item.Id,
            Name = item.Name,
            AvatarUrl = item.AvatarUrl,
            Rating = item.Rating,
            Comment = item.Comment,
            Status = item.Status.Name,
            RejectionComment = item.RejectionComment,
            Date = item.Date,
            EditableUntil = item.EditableUntil,
        };

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

        /// <summary>The exam results page in one call: the caller's own gama-api <c>exams/result/{id}</c> as gama-api sends it
        /// (<c>result</c>), with their AI analysis attached (<c>analysis</c>: the saved text, or what getting one costs).
        /// Use this instead of gama-api's endpoint directly. Never charges; getting the analysis is
        /// <c>POST exams/{id}/analysis</c>.</summary>
        [HttpGet("{id:long}/result"), Produces(typeof(ApiResponse<ExamResultPageResponseViewModel>))]
        public async Task<IActionResult<ExamResultPageResponseViewModel>> GetExamResult([FromRoute] long id)
        {
            try
            {
                var token = TokenAuthenticationHandler.GetTokenFromHeader(Request);
                if (string.IsNullOrEmpty(token))
                {
                    return Ok<ExamResultPageResponseViewModel>(new(new Error { Message = Localizer.Value["MissingAuthorizationToken"] }));
                }

                var result = await examAnalysisService.Value.GetResultPageAsync(User.UserId(), id, token);
                return Ok<ExamResultPageResponseViewModel>(new(result.Errors)
                {
                    Data = result.Data is null ? null : new()
                    {
                        Result = result.Data.Result,
                        Analysis = MapAnalysisStatus(result.Data.Analysis),
                    },
                });
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return Ok<ExamResultPageResponseViewModel>(new(new Error { Message = Localizer.Value["OperationFailed"] }));
            }
        }

        /// <summary>
        /// Submits the caller's exam <paramref name="id"/> to gama-api (the exam page calls this instead of gama-api's
        /// <c>exams/end</c>) and records it here with its points and the countdown's time spent, so the result shows the time
        /// actually spent (gama-api's own figure is wall-clock and often 3.5 hours off). The body goes to gama-api unchanged.
        /// <c>id</c> in the response is the result to open, also when gama-api refuses an exam already submitted.
        /// See docs/business/exams-and-content.md, "Time spent on an exam".
        /// </summary>
        [HttpPost("{id:long}/end"), Produces(typeof(ApiResponse<ExamEndResponseViewModel>))]
        public async Task<IActionResult<ExamEndResponseViewModel>> EndExam([FromRoute] long id, [NotNull, FromBody] ExamEndRequestViewModel request)
        {
            try
            {
                var token = TokenAuthenticationHandler.GetTokenFromHeader(Request);
                if (string.IsNullOrEmpty(token))
                {
                    return Ok<ExamEndResponseViewModel>(new(new Error { Message = Localizer.Value["MissingAuthorizationToken"] }));
                }

                System.Text.Json.Nodes.JsonObject body = new()
                {
                    ["startID"] = request.StartId is { } startId ? System.Text.Json.Nodes.JsonNode.Parse(startId.GetRawText()) : null,
                    ["answers"] = request.Answers is { } answers ? System.Text.Json.Nodes.JsonNode.Parse(answers.GetRawText()) : null,
                };
                var result = await gameService.Value.EndExamAsync(User.UserId(), new() { ExamId = id, SecretKey = token, Body = body }, request.TimeSpentSeconds);
                return Ok<ExamEndResponseViewModel>(new(result.Errors) { Data = result.Data is null ? null : new() { Id = result.Data } });
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return Ok<ExamEndResponseViewModel>(new(new Error { Message = Localizer.Value["OperationFailed"] }));
            }
        }

        /// <summary>Starts the caller's attempt at exam <paramref name="id"/>, or resumes their unfinished one (gama-api's
        /// <c>exams/start/{id}</c>): <c>startID</c>, the exam, its questions without their answers and <c>remainedSeconds</c>.
        /// From whichever side writes results (<c>WriteRoutes:Exams.Games</c>). See docs/business/exams-migration.md.</summary>
        [HttpGet("{id:long}/start"), Produces(typeof(ApiResponse<System.Text.Json.Nodes.JsonObject>))]
        public Task<IActionResult<System.Text.Json.Nodes.JsonObject>> StartExam([FromRoute] long id)
            => ExamDataAsync(token => examService.Value.StartExamAsync(User.UserId(), id, token));

        /// <summary>The caller's own exam results, newest first (gama-api's <c>exams/results</c>; <c>page</c>, <c>perpage</c>).</summary>
        [HttpGet("results"), Produces(typeof(ApiResponse<System.Text.Json.Nodes.JsonObject>))]
        public Task<IActionResult<System.Text.Json.Nodes.JsonObject>> GetMyResults()
            => ExamDataAsync(token => examService.Value.GetUserResultsAsync(User.UserId(), token, Request.QueryString.Value));

        /// <summary>Who took exam <paramref name="id"/>, for its owner (gama-api's <c>exams/participants/{id}</c>; <c>page</c>,
        /// <c>perpage</c>, <c>status</c>).</summary>
        [HttpGet("{id:long}/participants"), Produces(typeof(ApiResponse<System.Text.Json.Nodes.JsonObject>))]
        public Task<IActionResult<System.Text.Json.Nodes.JsonObject>> GetParticipants([FromRoute] long id)
            => ExamDataAsync(token => examService.Value.GetParticipantsAsync(User.UserId(), id, token, Request.QueryString.Value));

        // The caller's own exam data: the gama-api token is passed on for the legacy side; the native side uses the user.
        private async Task<IActionResult<System.Text.Json.Nodes.JsonObject>> ExamDataAsync(Func<string?, Task<ResultData<System.Text.Json.Nodes.JsonObject>>> call)
        {
            try
            {
                var result = await call(TokenAuthenticationHandler.GetTokenFromHeader(Request));
                Response.Headers.CacheControl = "private,no-store";
                return Ok<System.Text.Json.Nodes.JsonObject>(new(result.Errors) { Data = result.Data });
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return Ok<System.Text.Json.Nodes.JsonObject>(new(new Error { Message = Localizer.Value["OperationFailed"] }));
            }
        }

        /// <summary>The caller's one-time AI analysis of their own result for exam <paramref name="id"/>, paid once (quota of the
        /// ExamAnalysis feature, then points) and returned free after that. Everything in the prompt comes from gama-api's
        /// result for the caller; the request has no body. See docs/business/exams-and-content.md, "Paid exam analysis".</summary>
        [HttpPost("{id:long}/analysis"), Produces(typeof(ApiResponse<ExamAnalysisResponseViewModel>))]
        public async Task<IActionResult<ExamAnalysisResponseViewModel>> AnalyzeExam([FromRoute] long id)
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
                    ExamId = id,
                    SecretKey = token,
                });

                return Ok<ExamAnalysisResponseViewModel>(new(result.Errors)
                {
                    Data = MapAnalysis(result.Data),
                });
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return Ok<ExamAnalysisResponseViewModel>(new(new Error { Message = Localizer.Value["OperationFailed"] }));
            }
        }

        private static ExamAnalysisStatusResponseViewModel? MapAnalysisStatus(ExamAnalysisStatusDto? status) => status is null
            ? null
            : new()
            {
                Id = status.Analysis?.Id,
                Result = status.Analysis?.Result,
                Points = status.Points,
                Purchased = status.Purchased,
            };

        private static ExamAnalysisResponseViewModel? MapAnalysis(PrepareExamAnalysisResponseDto? response) => response is null
            ? null
            : new()
            {
                Id = response.Analysis?.Id,
                ExamId = response.Analysis?.ExamId,
                Result = response.Analysis?.Result,
                Points = response.Analysis?.Points,
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
