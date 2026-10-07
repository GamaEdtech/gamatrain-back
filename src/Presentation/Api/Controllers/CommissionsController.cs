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
    using GamaEdtech.Domain.Enumeration;
    using GamaEdtech.Domain.Specification;
    using GamaEdtech.Domain.Specification.Content;
    using GamaEdtech.Presentation.ViewModel.Content;

    using Hangfire;

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
    public class CommissionsController(Lazy<ILogger<CommissionsController>> logger, Lazy<IContentDeliveryService> contentDeliveryService
        , Lazy<ICommissionPayoutService> commissionPayoutService)
        : ApiControllerBase<CommissionsController>(logger)
    {
        /// <summary>Report of the current user's own accrued commissions (every accrual, paid out or not - see balance for what's left).</summary>
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

        /// <summary>The current user's commission balance: earned, paid out, reserved by an open payout request, and available to request.</summary>
        [HttpGet("balance"), Produces(typeof(ApiResponse<CommissionBalanceResponseViewModel>))]
        [Permission(policy: null)]
        public async Task<IActionResult<CommissionBalanceResponseViewModel>> GetBalance()
        {
            try
            {
                var result = await commissionPayoutService.Value.GetBalanceAsync(User.UserId());

                return Ok<CommissionBalanceResponseViewModel>(new(result.Errors)
                {
                    Data = result.Data is null ? null : new()
                    {
                        TotalEarnedUsd = result.Data.TotalEarnedUsd,
                        PaidOutUsd = result.Data.PaidOutUsd,
                        ReservedUsd = result.Data.ReservedUsd,
                        AvailableUsd = result.Data.AvailableUsd,
                        PayoutThresholdUsd = result.Data.PayoutThresholdUsd,
                        OpenPayoutId = result.Data.OpenPayoutId,
                    },
                });
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return Ok<CommissionBalanceResponseViewModel>(new(new Error { Message = exc.Message }));
            }
        }

        /// <summary>The current user's own payout requests, newest first.</summary>
        [HttpGet("payouts"), Produces(typeof(ApiResponse<ListDataSource<CommissionPayoutResponseViewModel>>))]
        [Permission(policy: null)]
        public async Task<IActionResult<ListDataSource<CommissionPayoutResponseViewModel>>> GetPayouts([NotNull, FromQuery] CommissionPayoutsListRequestViewModel request)
        {
            try
            {
                ISpecification<CommissionPayout> specification = new UserIdEqualsSpecification<CommissionPayout, long>(User.UserId());
                if (request.Status is not null)
                {
                    specification = specification.And(new PayoutStatusEqualsSpecification(request.Status));
                }

                var result = await commissionPayoutService.Value.GetPayoutsAsync(new ListRequestDto<CommissionPayout>
                {
                    PagingDto = request.PagingDto,
                    Specification = specification,
                });

                return Ok<ListDataSource<CommissionPayoutResponseViewModel>>(new(result.Errors)
                {
                    Data = result.Data.List is null ? new() : new()
                    {
                        List = result.Data.List.Select(MapPayout),
                        TotalRecordsCount = result.Data.TotalRecordsCount,
                    },
                });
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return Ok<ListDataSource<CommissionPayoutResponseViewModel>>(new(new Error { Message = exc.Message }));
            }
        }

        /// <summary>Request a payout of (part of) the available balance. Returns the new request's id.</summary>
        [HttpPost("payouts"), Produces(typeof(ApiResponse<long>))]
        [Permission(policy: null)]
        public async Task<IActionResult> RequestPayout([NotNull, FromBody] RequestCommissionPayoutRequestViewModel request)
        {
            try
            {
                var result = await commissionPayoutService.Value.RequestPayoutAsync(new RequestCommissionPayoutRequestDto
                {
                    UserId = User.UserId(),
                    AmountUsd = request.AmountUsd,
                    Method = request.Method ?? PayoutMethod.StripeConnect,
                    Destination = request.Destination,
                });

                if (result.OperationResult is Constants.OperationResult.Succeeded)
                {
                    _ = BackgroundJob.Enqueue<ICommissionPayoutService>(t => t.SendPayoutRequestedEmailAsync(result.Data));
                }

                return Ok(new ApiResponse<long>(result.Errors) { Data = result.Data });
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return Ok(new ApiResponse<long> { Errors = [new() { Message = exc.Message }] });
            }
        }

        /// <summary>The current user's Stripe payout account: whether it exists and whether Stripe accepts payouts to it yet (read live from Stripe).</summary>
        [HttpGet("payout-account"), Produces(typeof(ApiResponse<PayoutAccountResponseViewModel>))]
        [Permission(policy: null)]
        public async Task<IActionResult<PayoutAccountResponseViewModel>> GetPayoutAccount()
        {
            try
            {
                var result = await commissionPayoutService.Value.GetPayoutAccountAsync(User.UserId());

                return Ok<PayoutAccountResponseViewModel>(new(result.Errors)
                {
                    Data = result.Data is null ? null : new()
                    {
                        HasAccount = result.Data.HasAccount,
                        Country = result.Data.Country,
                        DetailsSubmitted = result.Data.DetailsSubmitted,
                        PayoutsEnabled = result.Data.PayoutsEnabled,
                    },
                });
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return Ok<PayoutAccountResponseViewModel>(new(new Error { Message = exc.Message }));
            }
        }

        /// <summary>
        /// Start (or continue) Stripe payout setup: creates the user's Stripe Express account on first use and returns a
        /// short-lived Stripe onboarding link to redirect to. Stripe sends the user back to returnUrl with ?stripe=return.
        /// </summary>
        [HttpPost("payout-account/onboarding"), Produces(typeof(ApiResponse<string>))]
        [Permission(policy: null)]
        public async Task<IActionResult> CreatePayoutOnboardingLink([NotNull, FromBody] PayoutOnboardingRequestViewModel request)
        {
            try
            {
                var result = await commissionPayoutService.Value.CreatePayoutOnboardingLinkAsync(new PayoutOnboardingRequestDto
                {
                    UserId = User.UserId(),
                    Country = request.Country,
                    ReturnUrl = request.ReturnUrl,
                });

                return Ok(new ApiResponse<string>(result.Errors) { Data = result.Data });
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return Ok(new ApiResponse<string> { Errors = [new() { Message = exc.Message }] });
            }
        }

        /// <summary>Cancel the current user's own payout request while it is still pending.</summary>
        [HttpPatch("payouts/{payoutId:long}/cancel"), Produces(typeof(ApiResponse<bool>))]
        [Permission(policy: null)]
        public async Task<IActionResult> CancelPayout([FromRoute] long payoutId)
        {
            try
            {
                var result = await commissionPayoutService.Value.CancelPayoutAsync(User.UserId(), payoutId);

                return Ok(new ApiResponse<bool>(result.Errors) { Data = result.Data });
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return Ok(new ApiResponse<bool> { Errors = [new() { Message = exc.Message }] });
            }
        }

        internal static CommissionPayoutResponseViewModel MapPayout(CommissionPayoutDto t) => new()
        {
            Id = t.Id,
            UserId = t.UserId,
            UserFirstName = t.UserFirstName,
            UserLastName = t.UserLastName,
            AmountUsd = t.AmountUsd,
            Destination = t.Destination,
            Status = t.Status,
            Method = t.Method,
            CreationDate = t.CreationDate,
            ApprovedByUserId = t.ApprovedByUserId,
            ApprovedByFullName = t.ApprovedByFullName,
            ApprovalDate = t.ApprovalDate,
            PaidByUserId = t.PaidByUserId,
            PaidByFullName = t.PaidByFullName,
            PaidDate = t.PaidDate,
            TransferReference = t.TransferReference,
            RejectedByUserId = t.RejectedByUserId,
            RejectedByFullName = t.RejectedByFullName,
            RejectionDate = t.RejectionDate,
            RejectionReason = t.RejectionReason,
            CancellationDate = t.CancellationDate,
        };
    }
}
