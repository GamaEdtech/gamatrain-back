namespace GamaEdtech.Domain.Entity.Identity
{
    using GamaEdtech.Common.Data;
    using GamaEdtech.Common.DataAccess.Entities;
    using GamaEdtech.Common.DataAnnotation;
    using GamaEdtech.Common.DataAnnotation.Schema;

    using Microsoft.AspNetCore.Identity;
    using Microsoft.EntityFrameworkCore;
    using Microsoft.EntityFrameworkCore.Metadata.Builders;

    using System.Collections.Generic;
    using System.Diagnostics.CodeAnalysis;

    [Table(nameof(ApplicationUser))]
    [Audit((int)Common.Core.Constants.EntityType.ApplicationUser)]
    public class ApplicationUser : IdentityUser<int>, IEntity<ApplicationUser, int>, IEnablable
    {
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
        [Required]
        [Column(nameof(Id), DataType.Int)]
        public override int Id { get; set; }

        [StringLength(256)]
        [Required]
        [Column(nameof(UserName), DataType.UnicodeString)]
        public override string? UserName { get; set; }

        [StringLength(256)]
        [Required]
        [Column(nameof(NormalizedUserName), DataType.UnicodeString)]
        public override string? NormalizedUserName { get; set; }

        [StringLength(256)]
        [Column(nameof(Email), DataType.UnicodeString)]
        public override string? Email { get; set; }

        [StringLength(256)]
        [Column(nameof(NormalizedEmail), DataType.UnicodeString)]
        public override string? NormalizedEmail { get; set; }

        [Required]
        [Column(nameof(EmailConfirmed), DataType.Boolean)]
        public override bool EmailConfirmed { get; set; }

        [StringLength(512)]
        [Column(nameof(PasswordHash), DataType.UnicodeString)]
        public override string? PasswordHash { get; set; }

        [StringLength(50)]
        [Required]
        [Column(nameof(SecurityStamp), DataType.String)]
        [AuditIgnore]
        public override string? SecurityStamp { get; set; }

        [StringLength(50)]
        [Required]
        [Column(nameof(ConcurrencyStamp), DataType.String)]
        [AuditIgnore]
        public override string? ConcurrencyStamp { get; set; }

        [StringLength(50)]
        [Column(nameof(PhoneNumber), DataType.String)]
        public override string? PhoneNumber { get; set; }

        [Required]
        [Column(nameof(PhoneNumberConfirmed), DataType.Boolean)]
        public override bool PhoneNumberConfirmed { get; set; }

        [Required]
        [Column(nameof(TwoFactorEnabled), DataType.Boolean)]
        public override bool TwoFactorEnabled { get; set; }

        [Column(nameof(LockoutEnd), DataType.DateTimeOffset)]
        [AuditIgnore]
        public override DateTimeOffset? LockoutEnd { get; set; }

        [Required]
        [Column(nameof(LockoutEnabled), DataType.Boolean)]
        public override bool LockoutEnabled { get; set; }

        [Required]
        [Column(nameof(AccessFailedCount), DataType.Int)]
        [AuditIgnore]
        public override int AccessFailedCount { get; set; }

        [Column(nameof(RegistrationDate), DataType.DateTimeOffset)]
        public DateTimeOffset? RegistrationDate { get; set; }

        [Required]
        [Column(nameof(Enabled), DataType.Boolean)]
        public bool Enabled { get; set; }

        [Column(nameof(FirstName), DataType.UnicodeString)]
        [StringLength(100)]
        public string? FirstName { get; set; }

        [Column(nameof(LastName), DataType.UnicodeString)]
        [StringLength(100)]
        public string? LastName { get; set; }

        [Column(nameof(Avatar), DataType.UnicodeMaxString)]
        public string? Avatar { get; set; }

        [Column(nameof(CityId), DataType.Int)]
        public int CityId { get; set; }

        [Column(nameof(SchoolId), DataType.Int)]
        public int SchoolId { get; set; }

        public ICollection<ApplicationUserClaim>? UserClaims { get; set; }

        public ICollection<ApplicationUserLogin>? UserLogins { get; set; }

        public ICollection<ApplicationUserRole>? UserRoles { get; set; }

        public ICollection<ApplicationUserToken>? UserTokens { get; set; }

        public void Configure([NotNull] EntityTypeBuilder<ApplicationUser> builder)
        {
            _ = builder.HasIndex(e => e.NormalizedEmail)
                .HasDatabaseName(DbProviderFactories.GetFactory.GetObjectName($"IX_{nameof(ApplicationUser)}_{nameof(NormalizedEmail)}"));

            _ = builder.HasIndex(e => e.NormalizedUserName)
                .HasDatabaseName(DbProviderFactories.GetFactory.GetObjectName($"IX_{nameof(ApplicationUser)}_{nameof(NormalizedUserName)}"))
                .IsUnique()
                .HasFilter($"([{DbProviderFactories.GetFactory.GetObjectName(nameof(NormalizedUserName), pluralize: false)}] IS NOT NULL)");

            var now = new DateTimeOffset(2023, 3, 21, 0, 0, 0, TimeSpan.Zero);
            List<ApplicationUser> seedData =
            [
                // Password: @Admin123
                new ApplicationUser { Id = 1, UserName = "admin", PasswordHash = "AQAAAAIAAYagAAAAEMLN3xqYWUja6ShSK0teeCYzziU6b+KghL4AiSXrb03Y3VbBfxKP7LUF3PZAJhQJ+Q==", NormalizedUserName = "ADMIN", Email = "admin@gamaedtech.com", NormalizedEmail = "ADMIN@GAMAEDTECH.COM", EmailConfirmed = true, ConcurrencyStamp = "5BABA139-4AE5-4C47-BC65-DE4849346A17", PhoneNumber = "09355028981", PhoneNumberConfirmed = true, SecurityStamp = "EAF1FA85-3DA1-4A40-90C6-65B97BF903F1", RegistrationDate = now, Enabled = true, },
            ];
            _ = builder.HasData(seedData);
        }
    }
}
