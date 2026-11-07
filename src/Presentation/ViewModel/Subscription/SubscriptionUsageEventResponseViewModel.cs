namespace GamaEdtech.Presentation.ViewModel.Subscription
{
    public sealed class SubscriptionUsageEventResponseViewModel
    {
        public long Id { get; set; }

        public long UserId { get; set; }

        public string? UserEmail { get; set; }

        public long UserSubscriptionId { get; set; }

        public long SubscriptionPlanId { get; set; }

        public string? PlanTitle { get; set; }

        public int FeatureId { get; set; }

        public string? FeatureCode { get; set; }

        public string? FeatureName { get; set; }

        public int Amount { get; set; }

        public long? IdentifierId { get; set; }

        /// <summary>On a refund row: the charge it reverses (null for an unmatched or older refund).</summary>
        public long? RefundOfLogId { get; set; }

        /// <summary>On a charge row: its refund, if refunded. A charge is refundable while this is null.</summary>
        public long? RefundLogId { get; set; }

        public DateTimeOffset CreationDate { get; set; }
    }
}
