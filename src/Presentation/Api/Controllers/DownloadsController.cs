namespace GamaEdtech.Presentation.Api.Controllers
{
    using System.Diagnostics.CodeAnalysis;

    using Asp.Versioning;

    using GamaEdtech.Application.Interface;
    using GamaEdtech.Common.Core;
    using GamaEdtech.Common.Data;
    using GamaEdtech.Common.Identity;
    using GamaEdtech.Data.Dto.Content;
    using GamaEdtech.Presentation.ViewModel.Content;

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

                if (result.Data?.LegacyAuthRejected == true)
                {
                    // gama-api rejected the caller's own forwarded legacy token while resolving the download -
                    // same failure shape, and same scoped exception to this API's usual "always 200, check
                    // succeeded/errors" convention, as IdentitiesController.GetDashboard - see
                    // DownloadContentResponseDto.LegacyAuthRejected. Any charge already made was already
                    // reversed before this response was built.
                    return Unauthorized<DownloadContentResponseViewModel>(new(new Error { Message = "Legacy session no longer valid" }));
                }

                return Ok<DownloadContentResponseViewModel>(new(result.Errors)
                {
                    Data = result.Data is null ? null : new()
                    {
                        Url = result.Data.Url,
                        Name = result.Data.Name,
                        Spent = result.Data.Spent,
                        PaidBy = result.Data.PaidBy,
                        Reason = result.Data.Reason,
                        CurrentSubscriptionId = result.Data.CurrentSubscriptionId,
                        CurrentPlanId = result.Data.CurrentPlanId,
                        CurrentPlanTitle = result.Data.CurrentPlanTitle,
                        UpgradeSuggestions = UpgradeSuggestionMapper.Map(result.Data.UpgradeSuggestions),
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

        /// <summary>Which of a past paper's pdf/word/answer files the caller already owns (gama-api's own paid flags), so the paper
        /// page can mark them owned before a click. Per user, so never cached; the paper's public data comes from the cached
        /// <c>Search/papers/{id}</c> instead.</summary>
        [HttpGet("papers/{id:long}/owned"), Produces(typeof(ApiResponse<PaperOwnershipResponseViewModel>))]
        [Permission(policy: null)]
        public async Task<IActionResult<PaperOwnershipResponseViewModel>> GetPaperOwnership([FromRoute] long id)
        {
            try
            {
                Response.Headers.CacheControl = "private,no-store";
                var token = TokenAuthenticationHandler.GetTokenFromHeader(Request);
                if (string.IsNullOrEmpty(token))
                {
                    return Ok<PaperOwnershipResponseViewModel>(new(new Error { Message = "Missing Authorization token" }));
                }

                var result = await contentDeliveryService.Value.GetPaperOwnershipAsync(token, id);
                if (result.Data?.LegacyAuthRejected == true)
                {
                    // Same scoped exception to the "always 200" convention as Download above.
                    return Unauthorized<PaperOwnershipResponseViewModel>(new(new Error { Message = "Legacy session no longer valid" }));
                }

                return Ok<PaperOwnershipResponseViewModel>(new(result.Errors)
                {
                    Data = result.Data is null ? null : new()
                    {
                        Pdf = result.Data.Pdf,
                        Word = result.Data.Word,
                        Answer = result.Data.Answer,
                    },
                });
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return Ok<PaperOwnershipResponseViewModel>(new(new Error { Message = exc.Message }));
            }
        }
    }
}
