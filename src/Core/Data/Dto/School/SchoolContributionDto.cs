namespace GamaEdtech.Data.Dto.School
{
    using GamaEdtech.Domain.Enumeration;

    public sealed class SchoolContributionDto
    {
        public string? Name { get; set; }
        public string? LocalName { get; set; }
        public SchoolType? SchoolType { get; set; }
        public string? ZipCode { get; set; }
        public string? Address { get; set; }
        public string? LocalAddress { get; set; }
        public double? Latitude { get; set; }
        public double? Longitude { get; set; }
        public int? CityId { get; set; }
        public int? CountryId { get; set; }
        public int? StateId { get; set; }
        public string? WebSite { get; set; }
        public string? FaxNumber { get; set; }
        public string? PhoneNumber { get; set; }
        public string? Email { get; set; }
        public string? Quarter { get; set; }
        public IEnumerable<long>? Tags { get; set; }
        public IEnumerable<int>? Boards { get; set; }
        public long? DefaultImageId { get; set; }
        public decimal? Tuition { get; set; }
        public string? Description { get; set; }
        public SchoolCommentContributionDto? Comment { get; set; }
        public string? ImageFileId { get; set; }
        public bool IsDefault { get; set; }
        public IEnumerable<SchoolLocalizedValueDto>? LocalizedValues { get; set; }

        /// <summary>The school as it was when this was submitted: a confirmed contribution has already changed the live record.</summary>
        public SchoolDto? OldValues { get; set; }
    }
}
