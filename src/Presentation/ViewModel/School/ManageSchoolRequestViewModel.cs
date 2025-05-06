namespace GamaEdtech.Presentation.ViewModel.School
{
    using GamaEdtech.Common.Converter;
    using System.Text.Json.Serialization;

    using GamaEdtech.Common.DataAnnotation;

    using GamaEdtech.Domain.Enumeration;
    using System.Collections.Generic;

    public sealed class ManageSchoolRequestViewModel
    {
        [Display]
        [Required]
        public string? Name { get; set; }

        [Display]
        public string? LocalName { get; set; }

        [Display]
        [Required]
        [JsonConverter(typeof(EnumerationConverter<SchoolType, byte>))]
        public SchoolType? SchoolType { get; set; }

        [Display]
        public int? StateId { get; set; }

        [Display]
        public string? ZipCode { get; set; }

        [Display]
        public string? Address { get; set; }

        [Display]
        public double? Latitude { get; set; }

        [Display]
        public double? Longitude { get; set; }

        [Display]
        public string? WebSite { get; set; }

        [Display]
        public string? LocalAddress { get; set; }

        [Display]
        public int? CityId { get; set; }

        [Display]
        public int? CountryId { get; set; }

        [Display]
        public string? Email { get; set; }

        [Display]
        public string? FaxNumber { get; set; }

        [Display]
        public string? PhoneNumber { get; set; }

        [Display]
        public string? Quarter { get; set; }

        [Display]
        public long? OsmId { get; set; }

        [Display]
        public IEnumerable<long> Tags { get; set; }
    }
}
