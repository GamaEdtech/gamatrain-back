namespace GamaEdtech.Data.Dto.Content
{
    public sealed class CommissionBalanceDto
    {
        /// <summary>Lifetime sum of every accrued ContentOwnerCommission row.</summary>
        public decimal TotalEarnedUsd { get; set; }

        /// <summary>Sum of Paid payouts.</summary>
        public decimal PaidOutUsd { get; set; }

        /// <summary>Sum of open (Pending/Approved) payouts - reserved, not yet transferred.</summary>
        public decimal ReservedUsd { get; set; }

        /// <summary>What can still be requested, rounded down to whole cents.</summary>
        public decimal AvailableUsd { get; set; }

        /// <summary>ApplicationSettingsDto.ContentOwnerCommissionPayoutThresholdUsd: the minimum a request may be for.</summary>
        public decimal PayoutThresholdUsd { get; set; }

        /// <summary>The owner's open request, if any - only one may be open at a time.</summary>
        public long? OpenPayoutId { get; set; }
    }
}
