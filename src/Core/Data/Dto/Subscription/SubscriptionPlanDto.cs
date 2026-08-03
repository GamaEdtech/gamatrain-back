namespace GamaEdtech.Data.Dto.Subscription
{
    using NetTopologySuite.Geometries;

    public sealed class SubscriptionPlanDto
    {
        public long Id { get; set; }
        public string? Title { get; set; }
        public Polygon? Polygon { get; set; }
        public bool IsActive { get; set; }
        public bool Highlight { get; set; }
        public IEnumerable<SubscriptionPlanPriceDto>? Prices { get; set; }
        public IEnumerable<PlanFeatureDto>? Features { get; set; }
    }
}
