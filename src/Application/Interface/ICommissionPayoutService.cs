namespace GamaEdtech.Application.Interface
{
    using System.Diagnostics.CodeAnalysis;

    using GamaEdtech.Common.Data;
    using GamaEdtech.Common.DataAnnotation;
    using GamaEdtech.Data.Dto.Content;
    using GamaEdtech.Domain.Entity;

    /// <summary>
    /// Paying content owners their accrued commission (2026-10-07): the owner requests, an admin approves, an admin
    /// confirms the money was transferred. Every admin decision requires the admin's authenticator code
    /// (ITwoFactorService) and is recorded on the CommissionPayout row. See docs/business/content-delivery.md, "Payouts".
    /// </summary>
    [Injectable]
    public interface ICommissionPayoutService
    {
        Task<ResultData<CommissionBalanceDto>> GetBalanceAsync(long userId);

        Task<ResultData<ListDataSource<CommissionPayoutDto>>> GetPayoutsAsync(ListRequestDto<CommissionPayout>? requestDto = null);

        /// <summary>Opens a request for (part of) the available balance. At most one open request per owner; the amount must reach the payout threshold.</summary>
        Task<ResultData<long>> RequestPayoutAsync([NotNull] RequestCommissionPayoutRequestDto requestDto);

        /// <summary>The owner withdraws their own request while it is still Pending.</summary>
        Task<ResultData<bool>> CancelPayoutAsync(long userId, long payoutId);

        /// <summary>Pending -> Approved. Needs the admin's 2FA code; an admin can't decide on their own payout.</summary>
        Task<ResultData<bool>> ApprovePayoutAsync([NotNull] ReviewCommissionPayoutRequestDto requestDto);

        /// <summary>Pending/Approved -> Rejected, which releases the reserved amount back to the available balance. Needs a reason and the admin's 2FA code.</summary>
        Task<ResultData<bool>> RejectPayoutAsync([NotNull] ReviewCommissionPayoutRequestDto requestDto);

        /// <summary>Approved -> Paid: the admin confirms the money was transferred, with the transfer's reference. Needs the admin's 2FA code.</summary>
        Task<ResultData<bool>> MarkPayoutPaidAsync([NotNull] ReviewCommissionPayoutRequestDto requestDto);
    }
}
