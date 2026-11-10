namespace GamaEdtech.Data.Dto.Location
{
    using GamaEdtech.Domain.Enumeration;

    /// <summary>A place matching a typed name, with the country and state it belongs to.</summary>
    public sealed class LocationSearchResultDto
    {
        public int Id { get; set; }
        public string? Title { get; set; }
        public LocationType? LocationType { get; set; }
        public int? StateId { get; set; }
        public string? StateTitle { get; set; }
        public int? CountryId { get; set; }
        public string? CountryTitle { get; set; }
    }
}
