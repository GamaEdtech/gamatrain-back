namespace GamaEdtech.Data.Dto.Content
{
    /// <summary>One admin decision on a payout (approve / reject / mark paid).</summary>
    public sealed class ReviewCommissionPayoutRequestDto
    {
        public long PayoutId { get; set; }

        /// <summary>The admin making the decision - recorded on the payout row.</summary>
        public long AdminUserId { get; set; }

        /// <summary>The admin's current authenticator (TOTP) code - every payout decision requires one.</summary>
        public string? TwoFactorCode { get; set; }

        /// <summary>Reject only.</summary>
        public string? RejectionReason { get; set; }

        /// <summary>Mark-paid only: the bank/PayPal transaction id of the actual transfer.</summary>
        public string? TransferReference { get; set; }
    }
}
