namespace GamaEdtech.Presentation.Api.Controllers
{
    using System.Diagnostics.CodeAnalysis;

    using Asp.Versioning;

    using GamaEdtech.Application.Interface;
    using GamaEdtech.Common.Core;
    using GamaEdtech.Common.Data;
    using GamaEdtech.Common.DataAccess.Specification;
    using GamaEdtech.Common.DataAccess.Specification.Impl;
    using GamaEdtech.Common.Identity;
    using GamaEdtech.Data.Dto.Content;
    using GamaEdtech.Domain.Entity;
    using GamaEdtech.Domain.Specification.Content;
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

                return Ok<DownloadContentResponseViewModel>(new(result.Errors)
                {
                    Data = result.Data is null ? null : new()
                    {
                        Url = result.Data.Url,
                        Name = result.Data.Name,
                        Spent = result.Data.Spent,
                        PaidBy = result.Data.PaidBy,
                    },
                });
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return Ok<DownloadContentResponseViewModel>(new(new Error { Message = exc.Message }));
            }
        }

        /// <summary>Report of the current user's own accrued content-owner commissions. No paid/payout state exists yet - see ContentOwnerCommission.</summary>
        [HttpGet("commissions"), Produces(typeof(ApiResponse<ListDataSource<ContentOwnerCommissionListResponseViewModel>>))]
        [Permission(policy: null)]
        public async Task<IActionResult<ListDataSource<ContentOwnerCommissionListResponseViewModel>>> GetCommissions([NotNull, FromQuery] ContentOwnerCommissionsListRequestViewModel request)
        {
            try
            {
                ISpecification<ContentOwnerCommission> specification = new OwnerUserIdEqualsSpecification(User.UserId());

                if (request.StartDate.HasValue || request.EndDate.HasValue)
                {
                    specification = specification.And(new CreationDateBetweenSpecification<ContentOwnerCommission>(request.StartDate, request.EndDate));
                }

                var result = await contentDeliveryService.Value.GetContentOwnerCommissionsAsync(new ListRequestDto<ContentOwnerCommission>
                {
                    PagingDto = request.PagingDto,
                    Specification = specification,
                });

                return Ok<ListDataSource<ContentOwnerCommissionListResponseViewModel>>(new(result.Errors)
                {
                    Data = result.Data.List is null ? new() : new()
                    {
                        List = result.Data.List.Select(t => new ContentOwnerCommissionListResponseViewModel
                        {
                            Id = t.Id,
                            OwnerUserId = t.OwnerUserId,
                            OwnerFirstName = t.OwnerFirstName,
                            OwnerLastName = t.OwnerLastName,
                            DownloaderUserId = t.DownloaderUserId,
                            Reason = t.Reason,
                            Source = t.Source,
                            ContentType = t.ContentType,
                            ExternalContentId = t.ExternalContentId,
                            ExternalFileType = t.ExternalFileType,
                            ExternalExtraId = t.ExternalExtraId,
                            Points = t.Points,
                            CommissionPercent = t.CommissionPercent,
                            AmountUsd = t.AmountUsd,
                            CreationDate = t.CreationDate,
                        }),
                        TotalRecordsCount = result.Data.TotalRecordsCount,
                    }
                });
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return Ok<ListDataSource<ContentOwnerCommissionListResponseViewModel>>(new(new Error { Message = exc.Message }));
            }
        }
    }
}
