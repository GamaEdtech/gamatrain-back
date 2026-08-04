namespace GamaEdtech.Data.Dto.Subscription
{
    /// <summary>Identity only - one entry per feature. Limit/description live one level up on <see cref="PlanFeatureGroupDto"/>, since a group can cover more than one feature.</summary>
    public sealed class PlanFeatureDto
    {
        public int FeatureId { get; set; }
        public string? FeatureCode { get; set; }
        public string? FeatureName { get; set; }
    }
}
