namespace GamaEdtech.Data.Dto.Identity
{
    using GamaEdtech.Data.Dto.Experience;
    using GamaEdtech.Domain.Enumeration;

    public sealed class PublicProfileResponseDto
    {
        public string? FirstName { get; set; }
        public string? LastName { get; set; }
        public string? AvatarUri { get; set; }
        public long ProfileView { get; set; }
        public DateTimeOffset? RegistrationDate { get; set; }
        public OnlineStatus OnlineStatus { get; set; }
        public string? Biography { get; set; }
        public IEnumerable<string?>? Skills { get; set; }
        public IEnumerable<ExperienceDto>? Experiences { get; set; }
        public string? CurrentStatusSentence { get; set; }
        public UserRateLevel UserRateLevel { get; set; }
        public DateTimeOffset? OrphanDate { get; set; }
    }
}
