namespace GamaEdtech.Domain.Enumeration
{
    using GamaEdtech.Common.Data.Enumeration;
    using GamaEdtech.Common.DataAnnotation;

    /// <summary>
    /// What kind of event earned a ContentOwnerCommission row - deliberately separate from
    /// ContentSource (which external system served a download). ContentDownload rows carry the
    /// download-only columns (Source, ContentType, ExternalContentId, ExternalFileType, ExternalExtraId);
    /// ReferralSubscription rows (2026-10-21) leave those null and carry PaymentId instead. See
    /// docs/business/referrals.md.
    /// </summary>
    public sealed class CommissionReason : Enumeration<CommissionReason, byte>
    {
        [Display]
        public static readonly CommissionReason ContentDownload = new(nameof(ContentDownload), 0);

        /// <summary>A teacher's share of a subscription payment (new, renewal or plan switch) made by a user they invited.</summary>
        [Display]
        public static readonly CommissionReason ReferralSubscription = new(nameof(ReferralSubscription), 1);

        public CommissionReason()
        {
        }

        public CommissionReason(string name, byte value) : base(name, value)
        {
        }
    }
}
