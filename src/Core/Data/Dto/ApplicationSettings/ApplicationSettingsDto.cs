namespace GamaEdtech.Data.Dto.ApplicationSettings
{
    public sealed class ApplicationSettingsDto
    {
        public int GridPageSize { get; set; } = 10;
        public string? DefaultTimeZoneId { get; set; }
        public int SchoolContributionPoints { get; set; }
        public int SchoolImageContributionPoints { get; set; }
        public int SchoolCommentContributionPoints { get; set; }
        public int PostContributionPoints { get; set; }
        public int SchoolIssuesContributionPoints { get; set; }
    }
}
