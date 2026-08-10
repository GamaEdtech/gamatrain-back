namespace GamaEdtech.Presentation.Api.Controllers
{
    using System.Diagnostics.CodeAnalysis;
    using System.Linq;

    using Asp.Versioning;

    using GamaEdtech.Application.Interface;
    using GamaEdtech.Common.Core;
    using GamaEdtech.Common.Data;
    using GamaEdtech.Common.Identity;
    using GamaEdtech.Data.Dto.Content;
    using GamaEdtech.Presentation.ViewModel.Content;
    using GamaEdtech.Presentation.ViewModel.Game;
    using GamaEdtech.Presentation.ViewModel.Subscription;

    using Microsoft.AspNetCore.Mvc;
    using Microsoft.Extensions.Logging;

    /// <summary>
    /// Resolves downloadable content from external sources (gama-api's legacy PastPaper,
    /// Multimedia, and Exam content today). Combines the source lookup, the downloader's charge
    /// (quota-then-points, only when the source reports a price), and the content owner's
    /// commission accrual (only when the source reports an owner) into a single call - see
    /// docs/business/content-delivery.md.
    /// </summary>
    [Route("api/v{version:apiVersion}/[controller]")]
    [ApiController]
    [ApiVersion("1.0")]
    public class DownloadsController(Lazy<ILogger<DownloadsController>> logger, Lazy<IContentDeliveryService> contentDeliveryService)
        : ApiControllerBase<DownloadsController>(logger)
    {
        [HttpPost, Produces(typeof(ApiResponse<DownloadContentResponseViewModel>))]
        [Permission(policy: null)]
        public async Task<IActionResult<DownloadContentResponseViewModel>> Download([NotNull][FromBody] DownloadContentRequestViewModel request)
        {
            try
            {
                var token = TokenAuthenticationHandler.GetTokenFromHeader(Request);
                if (string.IsNullOrEmpty(token))
                {
                    return Ok<DownloadContentResponseViewModel>(new(new Error { Message = "Missing Authorization token" }));
                }

                var result = await contentDeliveryService.Value.DownloadContentAsync(new DownloadContentRequestDto
                {
                    UserId = User.UserId(),
                    Token = token,
                    Id = request.Id.GetValueOrDefault(),
                    ContentType = request.ContentType!,
                    FileType = request.FileType,
                    ExtraId = request.ExtraId,
                });

                return Ok<DownloadContentResponseViewModel>(new(result.Errors)
                {
                    Data = result.Data is null ? null : new()
                    {
                        Url = result.Data.Url,
                        Name = result.Data.Name,
                        Spent = result.Data.Spent,
                        PaidBy = result.Data.PaidBy,
                        UpgradeSuggestions = result.Data.UpgradeSuggestions?.Select(t => new UpgradeSuggestionViewModel
                        {
                            Id = t.Id,
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
                            FeatureGroups = t.FeatureGroups?.Select(g => new PlanFeatureGroupViewModel
                            {
                                Features = g.Features.Select(f => new PlanFeatureViewModel
                                {
                                    FeatureId = f.FeatureId,
                                    FeatureCode = f.FeatureCode,
                                    FeatureName = f.FeatureName,
                                }),
                                Limit = g.Limit,
                                Description = g.Description,
                            }),
                        }),
                        AvailableBillingIntervals = result.Data.AvailableBillingIntervals,
                    },
                });
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return Ok<DownloadContentResponseViewModel>(new(new Error { Message = exc.Message }));
            }
        }
    }
}
