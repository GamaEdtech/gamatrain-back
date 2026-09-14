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
    using GamaEdtech.Domain.Entity;
    using GamaEdtech.Domain.Enumeration;
    using GamaEdtech.Domain.Specification.Content;
    using GamaEdtech.Presentation.ViewModel.Content;

    using Microsoft.AspNetCore.Mvc;
    using Microsoft.Extensions.Logging;

    /// <summary>
    /// Reports on accrued ContentOwnerCommission rows. Deliberately its own controller, not nested
    /// under DownloadsController - commissions are earned via a Reason (currently only
    /// ContentDownload), and Reason/Source are kept separate specifically so a future commission
    /// event (e.g. viewing content, exam participation, or any other event type) doesn't have to be
    /// shaped as a "download" at the API surface, even though downloads are the only reason today.
    /// See docs/business/content-delivery.md.
    /// </summary>
    [Route("api/v{version:apiVersion}/[controller]")]
    [ApiController]
    [ApiVersion("1.0")]
    public class CommissionsController(Lazy<ILogger<CommissionsController>> logger, Lazy<IContentDeliveryService> contentDeliveryService)
        : ApiControllerBase<CommissionsController>(logger)
    {
        /// <summary>Report of the current user's own accrued commissions. No paid/payout state exists yet - see ContentOwnerCommission.</summary>
        [HttpGet, Produces(typeof(ApiResponse<ListDataSource<ContentOwnerCommissionListResponseViewModel>>))]
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

        /// <summary>
        /// Same shape as TransactionsController.GetStatistics: the current user's own accrued commission,
        /// bucketed by day-of-week (max 7-day window) or month-of-year (max 12-month window), plus the
        /// total across the whole requested range.
        /// </summary>
        [HttpGet("statistics"), Produces(typeof(ApiResponse<CommissionStatisticsResponseViewModel>))]
        [Permission(policy: null)]
        public async Task<IActionResult<CommissionStatisticsResponseViewModel>> GetStatistics([NotNull, FromQuery] CommissionStatisticsRequestViewModel request)
        {
            try
            {
                var now = DateTime.Now;
                if (!request.EndDate.HasValue)
                {
                    request.EndDate = DateOnly.FromDateTime(now);
                }

                if (!request.StartDate.HasValue)
                {
                    request.StartDate = request.Period == Period.DayOfWeek
                        ? request.EndDate.Value.AddDays(-6)
                        : request.EndDate.Value.AddMonths(-11);
                }

                var result = await contentDeliveryService.Value.GetCommissionStatisticsAsync(new()
                {
                    UserId = User.UserId(),
                    Period = request.Period,
                    StartDate = request.StartDate.GetValueOrDefault(),
                    EndDate = request.EndDate.GetValueOrDefault(),
                });

                return Ok<CommissionStatisticsResponseViewModel>(new(result.Errors)
                {
                    Data = result.Data is null ? null : new()
                    {
                        Statistics = result.Data.Statistics.Select(t => new CommissionStatisticsBucketResponseViewModel
                        {
                            Name = t.Name,
                            AmountUsd = t.AmountUsd,
                            Points = t.Points,
                        }),
                        TotalAmountUsd = result.Data.TotalAmountUsd,
                        TotalPoints = result.Data.TotalPoints,
                    },
                });
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return Ok<CommissionStatisticsResponseViewModel>(new(new Error { Message = exc.Message }));
            }
        }
    }
}
