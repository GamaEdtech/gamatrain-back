namespace GamaEdtech.Application.Interface
{
    using System.Diagnostics.CodeAnalysis;

    using GamaEdtech.Common.Data;
    using GamaEdtech.Common.DataAnnotation;
    using GamaEdtech.Data.Dto.Content;
    using GamaEdtech.Domain.Entity;
    using GamaEdtech.Domain.Enumeration;

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

        /// <summary>
        /// Pending -> Approved. Needs the admin's 2FA code; an admin can't decide on their own payout. For a StripeConnect
        /// payout it also sends the Stripe Transfer and goes straight to Paid (returned status); if Stripe refuses, the
        /// payout goes back to Pending and the error is returned.
        /// </summary>
        Task<ResultData<PayoutStatus>> ApprovePayoutAsync([NotNull] ReviewCommissionPayoutRequestDto requestDto);

        /// <summary>Pending (or Approved, Manual only) -> Rejected, which releases the reserved amount back to the available balance. Needs a reason and the admin's 2FA code.</summary>
        Task<ResultData<bool>> RejectPayoutAsync([NotNull] ReviewCommissionPayoutRequestDto requestDto);

        /// <summary>
        /// Approved -> Paid. Manual: the admin confirms the money was transferred, with the transfer's reference. StripeConnect:
        /// finishes an approval that stopped half-way (sends the transfer or finds the one already sent). Needs the admin's 2FA code.
        /// </summary>
        Task<ResultData<bool>> MarkPayoutPaidAsync([NotNull] ReviewCommissionPayoutRequestDto requestDto);

        /// <summary>The owner's Stripe payout account, refreshed from Stripe.</summary>
        Task<ResultData<PayoutAccountDto>> GetPayoutAccountAsync(long userId);

        /// <summary>Creates the owner's Stripe Express account if needed and returns a link to Stripe's hosted onboarding.</summary>
        Task<ResultData<string>> CreatePayoutOnboardingLinkAsync([NotNull] PayoutOnboardingRequestDto requestDto);

        /// <summary>Background job after a request is created: CommissionPayoutRequestedEmailTemplate to the owner. Never throws.</summary>
        Task SendPayoutRequestedEmailAsync(long payoutId);

        /// <summary>Background job after a payout is marked paid: CommissionPayoutPaidEmailTemplate to the owner. Never throws.</summary>
        Task SendPayoutPaidEmailAsync(long payoutId);
    }
}
