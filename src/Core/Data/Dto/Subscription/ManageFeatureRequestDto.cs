namespace GamaEdtech.Data.Dto.Subscription
{
    public sealed class ManageFeatureRequestDto
    {
        public int? Id { get; set; }
        public string? Code { get; set; }
        public string? Name { get; set; }
        public string? Description { get; set; }
        public bool? IsActive { get; set; }
    }
}
