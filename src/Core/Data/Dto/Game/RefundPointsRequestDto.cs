namespace GamaEdtech.Data.Dto.Game
{
    using GamaEdtech.Domain.Enumeration;

    /// <summary>Reverses a <see cref="SpendPointsRequestDto"/> charge that was already applied - e.g. content that was paid for but then failed to actually deliver.</summary>
    public sealed class RefundPointsRequestDto
    {
        public required long UserId { get; set; }
        public required long Points { get; set; }
        public required long IdentifierId { get; set; }
        public required ContentType ContentType { get; set; }

        /// <summary>Mirrors <see cref="SpendPointsRequestDto.QuotaAmount"/> - the amount to credit back when <see cref="PaidBy"/> is <see cref="SpendSource.SubscriptionQuota"/>. Ignored otherwise.</summary>
        public int QuotaAmount { get; set; } = 1;

        /// <summary>Which side the original charge actually drew from - from the original <see cref="SpendPointsResponseDto.PaidBy"/>, so the refund reverses the same side.</summary>
        public required SpendSource PaidBy { get; set; }

        /// <summary>Required when <see cref="PaidBy"/> is <see cref="SpendSource.SubscriptionQuota"/> - which subscription's bucket to credit back, from the original <see cref="SpendPointsResponseDto.CurrentSubscriptionId"/>. Ignored otherwise.</summary>
        public long? UserSubscriptionId { get; set; }
    }
}
