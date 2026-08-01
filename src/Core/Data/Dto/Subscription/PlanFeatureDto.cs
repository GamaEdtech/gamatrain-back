namespace GamaEdtech.Data.Dto.Subscription
{
    public sealed class PlanFeatureDto
    {
        public int FeatureId { get; set; }
        public string? FeatureCode { get; set; }
        public string? FeatureName { get; set; }
        public int? Limit { get; set; }
    }
}
