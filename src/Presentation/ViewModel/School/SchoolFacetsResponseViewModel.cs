namespace GamaEdtech.Presentation.ViewModel.School
{
    using System.Text.Json.Serialization;

    using GamaEdtech.Common.Converter;
    using GamaEdtech.Domain.Enumeration;

    public sealed class SchoolFacetsResponseViewModel
    {
        public int Total { get; set; }

        public int WithImage { get; set; }

        public int WithRating { get; set; }

        public int WithTuition { get; set; }

        public IEnumerable<SchoolTypeFacetViewModel>? SchoolTypes { get; set; }

        public IEnumerable<SchoolTagFacetViewModel>? Tags { get; set; }

        /// <summary><c>country</c>, <c>state</c> or <c>city</c>: what <see cref="Regions"/> lists.</summary>
        public string? RegionLevel { get; set; }

        public IEnumerable<SchoolRegionFacetViewModel>? Regions { get; set; }
    }

    public sealed class SchoolTypeFacetViewModel
    {
        [JsonConverter(typeof(EnumerationConverter<SchoolType, byte>))]
        public SchoolType? SchoolType { get; set; }

        public int Count { get; set; }
    }

    public sealed class SchoolTagFacetViewModel
    {
        public long Id { get; set; }

        public string? Name { get; set; }

        public string? Icon { get; set; }

        public int Count { get; set; }
    }

    public sealed class SchoolRegionFacetViewModel
    {
        public int Id { get; set; }

        public string? Title { get; set; }

        public int Count { get; set; }
    }
}
