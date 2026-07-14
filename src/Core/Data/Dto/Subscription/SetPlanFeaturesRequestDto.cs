namespace GamaEdtech.Data.Dto.Subscription
{
    public sealed class SetPlanFeaturesRequestDto
    {
        public required long SubscriptionPlanId { get; set; }

        /// <summary>Replace-all semantics: the plan's feature set becomes exactly this list.</summary>
        public required IEnumerable<PlanFeatureItemDto> Features { get; set; }
    }

    public sealed class PlanFeatureItemDto
    {
        public required int FeatureId { get; set; }
        public required int Limit { get; set; }
    }
}
