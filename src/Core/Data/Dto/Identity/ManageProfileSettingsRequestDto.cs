namespace GamaEdtech.Data.Dto.Identity
{
    using GamaEdtech.Domain.Enumeration;

    using Microsoft.AspNetCore.Http;

    public sealed class ManageProfileSettingsRequestDto
    {
        public required long UserId { get; set; }
        public int? CityId { get; set; }
        public long? SchoolId { get; set; }
        public string? FirstName { get; set; }
        public string? LastName { get; set; }
        public GenderType? Gender { get; set; }
        public int? Board { get; set; }
        public int? Grade { get; set; }
        public int? Group { get; set; }
        public long? CoreId { get; set; }
        public IFormFile? Avatar { get; set; }
        public string? WalletId { get; set; }
        public ProfileVisibility? ProfileVisibility { get; set; }
        public string? Biography { get; set; }
        public IEnumerable<string?>? Skills { get; set; }
        public string? CurrentStatusSentence { get; set; }
        public string? Handle { get; set; }
    }
}
