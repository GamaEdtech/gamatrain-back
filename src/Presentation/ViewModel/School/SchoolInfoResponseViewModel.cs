namespace GamaEdtech.Presentation.ViewModel.School
{
    using System.Text.Json.Serialization;

    using GamaEdtech.Common.Converter;
    using GamaEdtech.Domain.Enumeration;

    public sealed class SchoolInfoResponseViewModel
    {
        public long Id { get; set; }

        public string? Name { get; set; }

        public string? Slug { get; set; }

        [JsonPropertyName("lat")]
        public double? Latitude { get; set; }

        [JsonPropertyName("long")]
        public double? Longitude { get; set; }

        public string? CityTitle { get; set; }

        public string? CountryTitle { get; set; }

        public string? StateTitle { get; set; }

        public bool HasWebsite { get; set; }

        public bool HasPhone { get; set; }

        public bool HasEmail { get; set; }

        public bool HasLocation { get; set; }

        public DateTimeOffset LastModifyDate { get; set; }

        public double? Rating { get; set; }

        public string? DefaultImageUri { get; set; }

        public double? Distance { get; set; }

        public int? CountryRank { get; set; }

        public int? StateRank { get; set; }

        public int? CityRank { get; set; }

        [JsonConverter(typeof(EnumerationConverter<SchoolType, byte>))]
        public SchoolType? SchoolType { get; set; }

        /// <summary>How many parents rated the school (the count behind <see cref="Rating"/>).</summary>
        public int ReviewCount { get; set; }

        /// <summary>The school's facility tags (see <c>GET tags/School</c>), e.g. Transport Services for a school bus.</summary>
        public IEnumerable<long>? TagIds { get; set; }
    }
}
