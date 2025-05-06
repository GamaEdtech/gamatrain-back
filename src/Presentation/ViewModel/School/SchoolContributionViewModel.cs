namespace GamaEdtech.Presentation.ViewModel.School
{
    using System.Collections.Generic;
    using System.Text.Json.Serialization;

    using GamaEdtech.Common.Converter;
    using GamaEdtech.Domain.Enumeration;

    public sealed class SchoolContributionViewModel
    {
        public string? Name { get; set; }

        public string? LocalName { get; set; }

        [JsonConverter(typeof(EnumerationConverter<SchoolType, byte>))]
        public SchoolType? SchoolType { get; set; }

        public int? StateId { get; set; }

        public string? ZipCode { get; set; }

        public string? Address { get; set; }

        public double? Latitude { get; set; }

        public double? Longitude { get; set; }

        public string? WebSite { get; set; }

        public string? LocalAddress { get; set; }

        public int? CityId { get; set; }

        public int? CountryId { get; set; }

        public string? Email { get; set; }

        public string? FaxNumber { get; set; }

        public string? PhoneNumber { get; set; }

        public string? Quarter { get; set; }

        public IEnumerable<long>? Tags { get; set; }
    }
}
