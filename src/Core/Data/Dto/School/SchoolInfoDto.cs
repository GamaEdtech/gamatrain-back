namespace GamaEdtech.Data.Dto.School
{
    using NetTopologySuite.Geometries;

    public sealed class SchoolInfoDto
    {
        public long Id { get; set; }
        public string? Name { get; set; }
        public Point? Coordinates { get; set; }
        public string? CityTitle { get; set; }
        public string? CountryTitle { get; set; }
        public string? StateTitle { get; set; }
        public bool HasWebSite { get; set; }
        public bool HasPhoneNumber { get; set; }
        public bool HasEmail { get; set; }
        public DateTimeOffset LastModifyDate { get; set; }
        public double? Score { get; set; }
        public double? ReviewScore => Score.HasValue ? Score.Value * 5 / 550 : null;
        public double? Distance { get; set; }
        public string? DefaultImageUri { get; set; }
        public int? CountryRank { get; set; }
        public int? StateRank { get; set; }
        public int? CityRank { get; set; }
    }
}
