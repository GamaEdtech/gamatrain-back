namespace GamaEdtech.Data.Dto.School
{
    using GamaEdtech.Domain.Enumeration;

    public sealed class SchoolContributionDto
    {
        public long Id { get; set; }
        public string? Name { get; set; }
        public string? LocalName { get; set; }
        public SchoolType? SchoolType { get; set; }
        public string? ZipCode { get; set; }
        public string? Address { get; set; }
        public string? LocalAddress { get; set; }
        public double? Latitude { get; set; }
        public double? Longitude { get; set; }
        public int? CityId { get; set; }
        public string? CityTitle { get; set; }
        public int? CountryId { get; set; }
        public string? CountryTitle { get; set; }
        public int? StateId { get; set; }
        public string? StateTitle { get; set; }
        public string? WebSite { get; set; }
        public string? FaxNumber { get; set; }
        public string? PhoneNumber { get; set; }
        public string? Email { get; set; }
        public string? Quarter { get; set; }
        public IEnumerable<long>? Tags { get; set; }
    }
}
