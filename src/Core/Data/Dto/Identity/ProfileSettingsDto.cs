namespace GamaEdtech.Data.Dto.Identity
{
    using GamaEdtech.Data.Dto.Experience;
    using GamaEdtech.Domain.Enumeration;

    public sealed class ProfileSettingsDto
    {
        public string? TimeZoneId { get; set; }
        public string? FirstName { get; set; }
        public string? LastName { get; set; }
        public int? CountryId { get; set; }
        public int? CityId { get; set; }
        public int? StateId { get; set; }
        public long? SchoolId { get; set; }
        public string? ReferralId { get; set; }
        public GenderType? Gender { get; set; }
        public int? Board { get; set; }
        public int? Grade { get; set; }

        /// <summary>Teacher/student signal - 5 = Teacher, 6 = Student. See ApplicationUser.Group's doc comment / docs/business/identity-and-access.md. Not the same concept as the Teacher/Student values on Role.</summary>
        public int? Group { get; set; }
        public long? CoreId { get; set; }
        public string? AvatarUri { get; set; }
        public string? UserName { get; set; }
        public string? WalletId { get; set; }
        public bool ProfileUpdated { get; set; }
        public Role? Roles { get; set; }
        public ProfileVisibility ProfileVisibility { get; set; }
        public string? Biography { get; set; }
        public IEnumerable<string?>? Skills { get; set; }
        public string? CurrentStatusSentence { get; set; }
        public UserRateLevel UserRateLevel { get; set; }
        public IEnumerable<ExperienceDto>? Experiences { get; set; }
        public string? Handle { get; set; }
        public DateTimeOffset? OrphanDate { get; set; }
    }
}
