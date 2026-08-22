namespace GamaEdtech.Domain.Entity.Identity
{
    using System.Collections.Generic;
    using System.Diagnostics.CodeAnalysis;

    using GamaEdtech.Common.Data;
    using GamaEdtech.Common.Data.Enumeration;
    using GamaEdtech.Common.DataAccess.Entities;
    using GamaEdtech.Common.DataAnnotation;
    using GamaEdtech.Common.DataAnnotation.Schema;
    using GamaEdtech.Domain.Enumeration;

    using Microsoft.AspNetCore.Identity;
    using Microsoft.EntityFrameworkCore;
    using Microsoft.EntityFrameworkCore.Metadata.Builders;

    [Table(nameof(ApplicationUser))]
    [Audit((int)Common.Core.Constants.EntityType.ApplicationUser)]
    public class ApplicationUser : IdentityUser<long>, IEntity<ApplicationUser, long>, IEnablable
    {
        public const long DefaultUserId = 1;

        public ApplicationUser()
        {
            UserRoles = [];
            UserLogins = [];
            UserClaims = [];
            UserTokens = [];

            SecurityStamp = Guid.NewGuid().ToString();
            ConcurrencyStamp = Guid.NewGuid().ToString();
        }

        public ApplicationUser(string userName)
            : this() => UserName = userName;

        [System.ComponentModel.DataAnnotations.Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        [Column(nameof(Id), DataType.Long)]
        [Required]
        public override long Id { get; set; }

        [Column(nameof(UserName), DataType.UnicodeString)]
        [StringLength(256)]
        [Required]
        public override string? UserName { get; set; }

        [Column(nameof(NormalizedUserName), DataType.UnicodeString)]
        [StringLength(256)]
        [Required]
        public override string? NormalizedUserName { get; set; }

        [Column(nameof(Email), DataType.UnicodeString)]
        [StringLength(256)]
        public override string? Email { get; set; }

        [Column(nameof(NormalizedEmail), DataType.UnicodeString)]
        [StringLength(256)]
        public override string? NormalizedEmail { get; set; }

        [Column(nameof(EmailConfirmed), DataType.Boolean)]
        [Required]
        public override bool EmailConfirmed { get; set; }

        [Column(nameof(PasswordHash), DataType.UnicodeString)]
        [StringLength(512)]
        public override string? PasswordHash { get; set; }

        [Column(nameof(SecurityStamp), DataType.String)]
        [StringLength(50)]
        [Required]
        [AuditIgnore]
        public override string? SecurityStamp { get; set; }

        [Column(nameof(ConcurrencyStamp), DataType.String)]
        [StringLength(50)]
        [Required]
        [AuditIgnore]
        public override string? ConcurrencyStamp { get; set; }

        [Column(nameof(PhoneNumber), DataType.String)]
        [StringLength(50)]
        public override string? PhoneNumber { get; set; }

        [Column(nameof(PhoneNumberConfirmed), DataType.Boolean)]
        [Required]
        public override bool PhoneNumberConfirmed { get; set; }

        [Column(nameof(TwoFactorEnabled), DataType.Boolean)]
        [Required]
        public override bool TwoFactorEnabled { get; set; }

        [Column(nameof(LockoutEnd), DataType.DateTimeOffset)]
        [AuditIgnore]
        public override DateTimeOffset? LockoutEnd { get; set; }

        [Column(nameof(LockoutEnabled), DataType.Boolean)]
        [Required]
        public override bool LockoutEnabled { get; set; }

        [Column(nameof(AccessFailedCount), DataType.Int)]
        [Required]
        [AuditIgnore]
        public override int AccessFailedCount { get; set; }

        [Column(nameof(RegistrationDate), DataType.DateTimeOffset)]
        public DateTimeOffset? RegistrationDate { get; set; }

        [Column(nameof(Enabled), DataType.Boolean)]
        [Required]
        public bool Enabled { get; set; }

        [Column(nameof(FirstName), DataType.UnicodeString)]
        [StringLength(100)]
        public string? FirstName { get; set; }

        [Column(nameof(LastName), DataType.UnicodeString)]
        [StringLength(100)]
        public string? LastName { get; set; }

        [Column(nameof(AvatarId), DataType.String)]
        [StringLength(100)]
        public string? AvatarId { get; set; }

        [Column(nameof(CityId), DataType.Int)]
        public int? CityId { get; set; }
        public Location? City { get; set; }

        [Column(nameof(SchoolId), DataType.Long)]
        public long? SchoolId { get; set; }
        public School? School { get; set; }

        [Column(nameof(ReferralId), DataType.String)]
        [StringLength(10)]
        public string? ReferralId { get; set; }

        [Column(nameof(Gender), DataType.Byte)]
        public GenderType? Gender { get; set; }

        [Column(nameof(Board), DataType.Int)]
        public int? Board { get; set; }

        [Column(nameof(Grade), DataType.Int)]
        public int? Grade { get; set; }

        [Column(nameof(Group), DataType.Int)]
        public int? Group { get; set; }

        [Column(nameof(CoreId), DataType.Long)]
        public long? CoreId { get; set; }

        [Column(nameof(CurrentBalance), DataType.Long)]
        [Required]
        public long CurrentBalance { get; set; }

        [Column(nameof(ProfileUpdated), DataType.Boolean)]
        [Required]
        public bool ProfileUpdated { get; set; }

        [Column(nameof(WalletId), DataType.String)]
        [StringLength(50)]
        public string? WalletId { get; set; }

        [Column(nameof(ProfileVisibility), DataType.Byte)]
        public ProfileVisibility ProfileVisibility { get; set; }

        [Column(nameof(ProfileView), DataType.Long)]
        public long ProfileView { get; set; }

        [Column(nameof(Biography), DataType.UnicodeMaxString)]
        public string? Biography { get; set; }

        [Column(nameof(Skills), DataType.UnicodeMaxString)]
        public string? Skills { get; set; }

        [Column(nameof(CurrentStatusSentence), DataType.UnicodeMaxString)]
        public string? CurrentStatusSentence { get; set; }

        [Column(nameof(OrphanDate), DataType.DateTimeOffset)]
        public DateTimeOffset? OrphanDate { get; set; }

        [Column(nameof(Handle), DataType.UnicodeString)]
        [StringLength(100)]
        public string? Handle { get; set; }

        [Column(nameof(LastLoginDate), DataType.DateTimeOffset)]
        public DateTimeOffset? LastLoginDate { get; set; }

        public ICollection<ApplicationUserClaim>? UserClaims { get; set; }

        public ICollection<ApplicationUserLogin>? UserLogins { get; set; }

        public ICollection<ApplicationUserRole>? UserRoles { get; set; }

        public ICollection<ApplicationUserToken>? UserTokens { get; set; }

        public ICollection<Experience>? Experiences { get; set; }

        public ICollection<LoginHistory>? LoginHistories { get; set; }

        public void Configure([NotNull] EntityTypeBuilder<ApplicationUser> builder)
        {
            _ = builder.OwnEnumeration<ApplicationUser, GenderType, byte>(t => t.Gender);
            _ = builder.OwnEnumeration<ApplicationUser, ProfileVisibility, byte>(t => t.ProfileVisibility);

            _ = builder.HasIndex(e => e.NormalizedEmail)
                .HasDatabaseName(DbProviderFactories.GetFactory.GetObjectName($"IX_{nameof(ApplicationUser)}_{nameof(NormalizedEmail)}"));

            _ = builder.HasIndex(e => e.NormalizedUserName)
                .HasDatabaseName(DbProviderFactories.GetFactory.GetObjectName($"IX_{nameof(ApplicationUser)}_{nameof(NormalizedUserName)}"))
                .IsUnique()
                .HasFilter($"([{DbProviderFactories.GetFactory.GetObjectName(nameof(NormalizedUserName), pluralize: false)}] IS NOT NULL)");

            _ = builder.HasIndex(e => e.ReferralId)
                .HasDatabaseName(DbProviderFactories.GetFactory.GetObjectName(
                    $"IX_{nameof(ApplicationUser)}_{nameof(ReferralId)}"))
                .IsUnique()
                .HasFilter($"([{DbProviderFactories.GetFactory.GetObjectName(nameof(ReferralId), pluralize: false)}] IS NOT NULL)");

            _ = builder.HasIndex(e => e.Handle)
                .HasDatabaseName(DbProviderFactories.GetFactory.GetObjectName(
                    $"IX_{nameof(ApplicationUser)}_{nameof(Handle)}"))
                .IsUnique()
                .HasFilter($"([{DbProviderFactories.GetFactory.GetObjectName(nameof(Handle), pluralize: false)}] IS NOT NULL)");

            var now = new DateTimeOffset(2023, 3, 21, 0, 0, 0, TimeSpan.Zero);
#pragma warning disable S2068 // Credentials should not be hard-coded
            List<ApplicationUser> seedData =
            [
                // Password: @Admin123
                new ApplicationUser { Id = DefaultUserId, UserName = "admin", PasswordHash = "AQAAAAIAAYagAAAAEMLN3xqYWUja6ShSK0teeCYzziU6b+KghL4AiSXrb03Y3VbBfxKP7LUF3PZAJhQJ+Q==", NormalizedUserName = "ADMIN", Email = "admin@gamaedtech.com", NormalizedEmail = "ADMIN@GAMAEDTECH.COM", EmailConfirmed = true, ConcurrencyStamp = "5BABA139-4AE5-4C47-BC65-DE4849346A17", PhoneNumber = "09355028981", PhoneNumberConfirmed = true, SecurityStamp = "EAF1FA85-3DA1-4A40-90C6-65B97BF903F1", RegistrationDate = now, Enabled = true, Gender = GenderType.Male, ProfileVisibility = ProfileVisibility.Private },
            ];
#pragma warning restore S2068 // Credentials should not be hard-coded
            _ = builder.HasData(seedData);
        }
    }
}
