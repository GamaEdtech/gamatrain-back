namespace GamaEdtech.Data.Dto.Subscription
{
    /// <summary>Input of <c>ISubscriptionQuotaService.RefundQuotaAsync</c>: which consumption to reverse and on which bucket.</summary>
    public sealed class RefundQuotaRequestDto
    {
        public required long UserId { get; set; }
        public required long UserSubscriptionId { get; set; }
        public required string FeatureCode { get; set; }
        public required int Amount { get; set; }

        /// <summary>The content item the charge was for (mirrors the charge's own IdentifierId).</summary>
        public long? IdentifierId { get; set; }

        /// <summary>
        /// The charge row being reversed. When null, the latest unrefunded charge on the same bucket, feature and
        /// <see cref="IdentifierId"/> is linked instead (the automatic refund after a failed download, which never
        /// learns its charge's log id).
        /// </summary>
        public long? RefundOfLogId { get; set; }

        /// <summary>The admin issuing the refund; null for an automatic one.</summary>
        public long? RefundedByUserId { get; set; }
    }
}
