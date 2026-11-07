namespace GamaEdtech.Data.Dto.Identity
{
    using System.Diagnostics.CodeAnalysis;

    using GamaEdtech.Common.DataAccess.Entities;

    public sealed class ApplicationUserDto : Common.Mapping.IRegister, IEnablable
    {
        public long Id { get; set; }
        public string? UserName { get; set; }
        public string? SecurityStamp { get; set; }
        public string? Email { get; set; }
        public bool EmailConfirmed { get; set; }
        public string? PhoneNumber { get; set; }
        public bool PhoneNumberConfirmed { get; set; }
        public string? ReferralId { get; set; }
        public DateTimeOffset? RegistrationDate { get; set; }
        public bool Enabled { get; set; }
        public string? FirstName { get; set; }
        public string? LastName { get; set; }
        public IEnumerable<string>? Roles { get; set; }

        public void Register([NotNull] Common.Mapping.TypeAdapterConfig config) => _ = config.ForType<ApplicationUserDto, Domain.Entity.Identity.ApplicationUser>();
    }
}
