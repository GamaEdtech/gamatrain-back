namespace GamaEdtech.Application.Interface
{
    using System.Diagnostics.CodeAnalysis;

    using GamaEdtech.Common.Data;
    using GamaEdtech.Common.DataAnnotation;
    using GamaEdtech.Data.Dto.Referral;

    /// <summary>
    /// Teacher invite program (2026-10-21): a teacher shares an invite link, and earns their locked percent of every
    /// subscription payment (new, renewal, plan switch) made by the users who signed up through it, for life. The money
    /// goes into the same commission ledger and payout flow as download commission. See docs/business/referrals.md.
    /// </summary>
    [Injectable]
    public interface IReferralService
    {
        /// <summary>
        /// Called when a user becomes a teacher: creates their offer (the current percent, valid for the configured days)
        /// unless they already have one. The offer email goes out with the next SendPendingOfferEmailsAsync run. Never throws.
        /// </summary>
        Task<ResultData<bool>> CreateOfferAsync(long userId);

        Task<ResultData<ReferralStatusDto>> GetStatusAsync(long userId);

        /// <summary>Teacher only. Locks the percent (the offer's while it is open, otherwise the current setting) and creates the invite code. Joining again changes nothing.</summary>
        Task<ResultData<ReferralStatusDto>> JoinAsync(long userId);

        Task<ResultData<ListDataSource<InvitedUserDto>>> GetInvitedUsersAsync(long userId, PagingDto? pagingDto);

        /// <summary>
        /// Links a just-created account to the teacher whose code it carries. Silently does nothing for an empty or unknown
        /// code, a teacher who hasn't joined, a self-invite, or an account that already has an inviter. Never throws.
        /// </summary>
        Task<ResultData<bool>> BindInviterAsync(long userId, string? referralCode);

        /// <summary>Legacy OTP register: remembers the code for the email/phone being registered, for the login that creates the account. Never throws.</summary>
        Task<ResultData<bool>> SavePendingReferralAsync([NotNull] string identity, string? referralCode);

        /// <summary>Legacy login that created the account: links the inviter saved by SavePendingReferralAsync for any of these identities. Never throws.</summary>
        Task<ResultData<bool>> BindPendingReferralAsync(long userId, [NotNull] IEnumerable<string?> identities);

        /// <summary>Recurring job: adds the inviter's share for every paid subscription payment of an invited user that doesn't have one yet.</summary>
        Task<ResultData<int>> AccrueSubscriptionCommissionsAsync();

        /// <summary>Recurring job: sends offer emails not sent yet (ReferralOfferEmailTemplate).</summary>
        Task<ResultData<int>> SendPendingOfferEmailsAsync();
    }
}
