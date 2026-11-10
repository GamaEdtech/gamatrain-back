namespace GamaEdtech.Data.Dto.School
{
    using GamaEdtech.Domain.Enumeration;

    /// <summary>Counts behind the school list's filters, for the schools matching the current filters.</summary>
    public sealed class SchoolFacetsDto
    {
        public int Total { get; set; }
        public int WithImage { get; set; }
        public int WithRating { get; set; }
        public int WithTuition { get; set; }
        public IEnumerable<SchoolTypeFacetDto>? SchoolTypes { get; set; }
        public IEnumerable<SchoolTagFacetDto>? Tags { get; set; }

        /// <summary>The level below the current location filter: countries, states of a country or cities of a state.</summary>
        public SchoolRegionLevel? RegionLevel { get; set; }
        public IEnumerable<SchoolRegionFacetDto>? Regions { get; set; }
    }

    public sealed class SchoolTypeFacetDto
    {
        public SchoolType? SchoolType { get; set; }
        public int Count { get; set; }
    }

    public sealed class SchoolTagFacetDto
    {
        public long Id { get; set; }
        public string? Name { get; set; }
        public string? Icon { get; set; }
        public int Count { get; set; }
    }

    public sealed class SchoolRegionFacetDto
    {
        public int Id { get; set; }
        public string? Title { get; set; }
        public int Count { get; set; }
    }

    public enum SchoolRegionLevel
    {
        Country,
        State,
        City,
    }
}
