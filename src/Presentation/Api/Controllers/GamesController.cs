namespace GamaEdtech.Presentation.Api.Controllers
{
    using System.Diagnostics.CodeAnalysis;
    using System.Threading.Tasks;

    using Asp.Versioning;

    using GamaEdtech.Application.Interface;
    using GamaEdtech.Common.Core;
    using GamaEdtech.Common.Data;
    using GamaEdtech.Common.Identity;
    using GamaEdtech.Common.Identity.ApiKey;
    using GamaEdtech.Data.Dto.Game;
    using GamaEdtech.Presentation.ViewModel.Game;
    using GamaEdtech.Presentation.ViewModel.Subscription;

    using Microsoft.AspNetCore.Mvc;
    using Microsoft.Extensions.Logging;

    using static GamaEdtech.Common.Core.Constants;

    [Route("api/v{version:apiVersion}/[controller]")]
    [ApiController]
    [ApiVersion("1.0")]
    [ApiVersion("2.0")]
    public class GamesController(Lazy<ILogger<GamesController>> logger, Lazy<IGameService> gameService)
        : ApiControllerBase<GamesController>(logger)
    {
        [HttpGet("easter-egg/fortune-wheel"), Produces(typeof(ApiResponse<CoinsResponseViewModel>))]
        [ApiKey]
        public async Task<IActionResult<CoinsResponseViewModel>> EasterEggFortuneWheel()
        {
            try
            {
                var result = await gameService.Value.EasterEggFortuneWheelAsync();

                return Ok<CoinsResponseViewModel>(new(result.Errors)
                {
                    Data = new()
                    {
                        Coins = result.Data?.Select(t => new CoinViewModel
                        {
                            Id = t.Id,
                            CoinType = t.CoinType,
                            ExpirationTime = t.ExpirationTime,
                        }),
                    }
                });
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return Ok<CoinsResponseViewModel>(new(new Error { Message = exc.Message }));
            }
        }

        [HttpPost("easter-egg/points"), Produces(typeof(ApiResponse<EasterEggPointsResponseViewModel>))]
        [Permission(policy: null)]
        public async Task<IActionResult<EasterEggPointsResponseViewModel>> EasterEggPoints([NotNull][FromBody] EasterEggPointsRequestViewModel request)
        {
            try
            {
                var result = await gameService.Value.EasterEggPointsAsync(new EasterEggPointsRequestDto
                {
                    Id = request.Id.GetValueOrDefault(),
                    UserId = User.UserId(),
                });

                return Ok<EasterEggPointsResponseViewModel>(new(result.Errors)
                {
                    Data = result.OperationResult is not OperationResult.Succeeded ? new() : new()
                    {
                        Points = result.Data,
                    }
                });
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return Ok<EasterEggPointsResponseViewModel>(new(new Error { Message = exc.Message }));
            }
        }

        [HttpPost("test-time"), Produces(typeof(ApiResponse<TestTimeQuizResponseViewModel>))]
        [Permission(policy: null)]
        public async Task<IActionResult<TestTimeQuizResponseViewModel>> TestTimeQuiz([NotNull][FromBody] TestTimeQuizRequestViewModel request)
        {
            try
            {
                var result = await gameService.Value.TestTimeAsync(new()
                {
                    TestId = request.TestId.GetValueOrDefault(),
                    SubmissionId = request.SubmissionId.GetValueOrDefault(),
                    UserId = User.UserId(),
                });

                return Ok<TestTimeQuizResponseViewModel>(new(result.Errors)
                {
                    Data = result.Data is null
                    ? null
                    : new()
                    {
                        Points = result.Data.Points,
                        IsCorrect = result.Data.IsCorrect,
                    },
                });
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return Ok<TestTimeQuizResponseViewModel>(new(new Error { Message = exc.Message }));
            }
        }

        [HttpPost("exams/points"), Produces(typeof(ApiResponse<TestTimeQuizResponseViewModel>))]
        [Permission(policy: null)]
        public async Task<IActionResult<ExamPointsResponseViewModel>> ExamPoints([FromHeader(Name = "SecretKey")] string secretKey, [NotNull][FromBody] ExamPointsRequestViewModel request)
        {
            try
            {
                if (string.IsNullOrEmpty(secretKey))
                {
                    return Ok<ExamPointsResponseViewModel>(new(new Error { Message = "Missing SecretKey" }));
                }

                var result = await gameService.Value.ExamPointsAsync(new()
                {
                    ExamId = request.Id.GetValueOrDefault(),
                    UserId = User.UserId(),
                    SecretKey = secretKey,
                });

                return Ok<ExamPointsResponseViewModel>(new(result.Errors)
                {
                    Data = result.Data is null
                    ? null
                    : new()
                    {
                        Id = result.Data.Id,
                        Points = result.Data.Points,
                    },
                });
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return Ok<ExamPointsResponseViewModel>(new(new Error { Message = exc.Message }));
            }
        }

        [HttpPost("spends"), Produces(typeof(ApiResponse<bool>))]
        [Permission(policy: null)]
        [MapToApiVersion("1.0")]
        public async Task<IActionResult<bool>> SpendPoints([NotNull][FromBody] SpendPointsRequestViewModel request)
        {
            try
            {
                var result = await gameService.Value.SpendPointsAsync(new SpendPointsRequestDto
                {
                    UserId = User.UserId(),
                    Points = request.Points.GetValueOrDefault(),
                    IdentifierId = request.IdentifierId.GetValueOrDefault(),
                    ContentType = request.ContentType!,
                });

                return Ok<bool>(new(result.Errors)
                {
                    Data = result.Data?.Spent == true,
                });
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return Ok<bool>(new(new Error { Message = exc.Message }));
            }
        }

        [HttpPost("spends"), Produces(typeof(ApiResponse<SpendPointsResponseViewModel>))]
        [Permission(policy: null)]
        [MapToApiVersion("2.0")]
        public async Task<IActionResult<SpendPointsResponseViewModel>> SpendPointsV2([NotNull][FromBody] SpendPointsRequestViewModel request)
        {
            try
            {
                var result = await gameService.Value.SpendPointsAsync(new SpendPointsRequestDto
                {
                    UserId = User.UserId(),
                    Points = request.Points.GetValueOrDefault(),
                    IdentifierId = request.IdentifierId.GetValueOrDefault(),
                    ContentType = request.ContentType!,
                });

                return Ok<SpendPointsResponseViewModel>(new(result.Errors)
                {
                    Data = result.Data is null ? null : new()
                    {
                        Spent = result.Data.Spent,
                        PaidBy = result.Data.PaidBy,
                        RemainingQuota = result.Data.RemainingQuota,
                        UpgradeSuggestions = result.Data.UpgradeSuggestions?.Select(t => new UpgradeSuggestionViewModel
                        {
                            SubscriptionPlanId = t.SubscriptionPlanId,
                            Title = t.Title,
                            Limit = t.Limit,
                            Description = t.Description,
                            PooledFeatureCodes = t.PooledFeatureCodes,
                            Highlight = t.Highlight,
                            Prices = t.Prices?.Select(p => new UpgradeSuggestionPriceViewModel
                            {
                                BillingInterval = p.BillingInterval,
                                Currency = p.Currency,
                                CurrencySymbol = p.CurrencySymbol,
                                Price = p.Price,
                                MonthlyEquivalentPrice = p.MonthlyEquivalentPrice,
                                DiscountPercent = p.DiscountPercent,
                            }),
                            Features = t.Features?.Select(f => new PlanFeatureViewModel
                            {
                                FeatureId = f.FeatureId,
                                FeatureCode = f.FeatureCode,
                                FeatureName = f.FeatureName,
                                Limit = f.Limit,
                                Description = f.Description,
                                PooledFeatureCodes = f.PooledFeatureCodes,
                            }),
                        }),
                        AvailableBillingIntervals = result.Data.AvailableBillingIntervals,
                    },
                });
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return Ok<SpendPointsResponseViewModel>(new(new Error { Message = exc.Message }));
            }
        }
    }
}
