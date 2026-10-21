namespace GamaEdtech.Presentation.Api.Controllers
{
    using System.Diagnostics.CodeAnalysis;

    using Asp.Versioning;

    using GamaEdtech.Application.Interface;
    using GamaEdtech.Common.Core;
    using GamaEdtech.Common.Data;
    using GamaEdtech.Common.Identity;
    using GamaEdtech.Data.Dto.Referral;
    using GamaEdtech.Presentation.ViewModel.Referral;

    using Microsoft.AspNetCore.Mvc;

    using static GamaEdtech.Common.Core.Constants;

    /// <summary>
    /// The teacher invite program (2026-10-21): a teacher joins, shares any page with ?invite=CODE, and earns their locked
    /// percent of every subscription payment made by the users who sign up through it. See docs/business/referrals.md.
    /// </summary>
    [Route("api/v{version:apiVersion}/[controller]")]
    [ApiVersion("1.0")]
    [Permission(policy: null)]
    public class ReferralController(Lazy<ILogger<ReferralController>> logger, Lazy<IIdentityService> referralService, Lazy<IReferralService> inviteService)
        : ApiControllerBase<ReferralController>(logger)
    {
        [HttpPost("generate"), Produces(typeof(ApiResponse<ReferralReponseViewModel>))]
        public async Task<IActionResult<ReferralReponseViewModel>> GenerateRefferalId()
        {
            try
            {
                var result = await referralService.Value.GenerateReferralUserAsync();

                return Ok<ReferralReponseViewModel>(new(result.Errors)
                {
                    Data = result.OperationResult is not OperationResult.Succeeded ? new() : new()
                    {
                        ReferralId = result.Data,
                    }
                });
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return Ok<ReferralReponseViewModel>(new(new Error { Message = exc.Message }));
            }
        }

        /// <summary>The caller's invite program status: offer, locked percent, invite code and totals.</summary>
        [HttpGet, Produces(typeof(ApiResponse<ReferralStatusResponseViewModel>))]
        public async Task<IActionResult<ReferralStatusResponseViewModel>> GetStatus()
        {
            try
            {
                var result = await inviteService.Value.GetStatusAsync(User.UserId());
                return Ok<ReferralStatusResponseViewModel>(new(result.Errors)
                {
                    Data = result.OperationResult is OperationResult.Succeeded && result.Data is not null ? Map(result.Data) : null,
                });
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return Ok<ReferralStatusResponseViewModel>(new(new Error { Message = exc.Message }));
            }
        }

        /// <summary>Teachers only. Locks the percent for life and creates the invite code; returns the new status. Joining again changes nothing.</summary>
        [HttpPost("join"), Produces(typeof(ApiResponse<ReferralStatusResponseViewModel>))]
        public async Task<IActionResult<ReferralStatusResponseViewModel>> Join()
        {
            try
            {
                var result = await inviteService.Value.JoinAsync(User.UserId());
                return Ok<ReferralStatusResponseViewModel>(new(result.Errors)
                {
                    Data = result.OperationResult is OperationResult.Succeeded && result.Data is not null ? Map(result.Data) : null,
                });
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return Ok<ReferralStatusResponseViewModel>(new(new Error { Message = exc.Message }));
            }
        }

        /// <summary>The users the caller invited, newest first, with what each has earned the caller.</summary>
        [HttpGet("invited"), Produces(typeof(ApiResponse<ListDataSource<InvitedUserResponseViewModel>>))]
        public async Task<IActionResult<ListDataSource<InvitedUserResponseViewModel>>> GetInvitedUsers([NotNull, FromQuery] InvitedUsersRequestViewModel request)
        {
            try
            {
                var result = await inviteService.Value.GetInvitedUsersAsync(User.UserId(), request.PagingDto);
                return Ok<ListDataSource<InvitedUserResponseViewModel>>(new(result.Errors)
                {
                    Data = result.OperationResult is not OperationResult.Succeeded || result.Data.List is null ? new() : new()
                    {
                        List = result.Data.List.Select(t => new InvitedUserResponseViewModel
                        {
                            DisplayName = t.DisplayName,
                            JoinedDate = t.JoinedDate,
                            Subscribed = t.Subscribed,
                            PaymentsCount = t.PaymentsCount,
                            EarnedUsd = t.EarnedUsd,
                        }),
                        TotalRecordsCount = result.Data.TotalRecordsCount,
                    },
                });
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return Ok<ListDataSource<InvitedUserResponseViewModel>>(new(new Error { Message = exc.Message }));
            }
        }

        private static ReferralStatusResponseViewModel Map(ReferralStatusDto t) => new()
        {
            IsTeacher = t.IsTeacher,
            Joined = t.Joined,
            Percent = t.Percent,
            JoinPercent = t.JoinPercent,
            OfferPercent = t.OfferPercent,
            OfferExpirationDate = t.OfferExpirationDate,
            OfferOpen = t.OfferOpen,
            JoinedDate = t.JoinedDate,
            ReferralCode = t.ReferralCode,
            InvitedCount = t.InvitedCount,
            SubscribedCount = t.SubscribedCount,
            EarnedUsd = t.EarnedUsd,
        };
    }
}
