namespace GamaEdtech.Data.Dto.School
{
    public sealed class SchoolsDto
    {
        public long Id { get; set; }
        public string? Name { get; set; }
        public string? LocalName { get; set; }
        public string? DefaultImageUri { get; set; }
        public int? CountryRank { get; set; }
        public int? StateRank { get; set; }
        public int? CityRank { get; set; }
        public double? Rating { get; set; }
    }
}
