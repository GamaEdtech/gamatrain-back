namespace GamaEdtech.Presentation.ViewModel.Content
{
    public sealed class CommissionBalanceResponseViewModel
    {
        /// <summary>Lifetime commission accrued.</summary>
        public decimal TotalEarnedUsd { get; set; }

        /// <summary>Already transferred (Paid payouts).</summary>
        public decimal PaidOutUsd { get; set; }

        /// <summary>Held by the open (Pending/Approved) payout request.</summary>
        public decimal ReservedUsd { get; set; }

        /// <summary>What can be requested now, in whole cents.</summary>
        public decimal AvailableUsd { get; set; }

        /// <summary>The minimum amount of a payout request.</summary>
        public decimal PayoutThresholdUsd { get; set; }

        /// <summary>The open request, if any - only one at a time.</summary>
        public long? OpenPayoutId { get; set; }
    }
}
