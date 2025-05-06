namespace GamaEdtech.Data.Dto.School
{
    using System.Collections.Generic;

    using GamaEdtech.Data.Dto.Tag;
    using GamaEdtech.Domain.Enumeration;

    using NetTopologySuite.Geometries;

    public sealed class SchoolDto
    {
        public long Id { get; set; }
        public string? Name { get; set; }
        public string? LocalName { get; set; }
        public SchoolType? SchoolType { get; set; }
        public string? ZipCode { get; set; }
        public string? Address { get; set; }
        public string? LocalAddress { get; set; }
        public Point? Coordinates { get; set; }
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
        public long? OsmId { get; set; }
        public IEnumerable<TagDto>? Tags { get; set; }
    }
}
