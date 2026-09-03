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

        /// <summary>
        /// The "is this person a teacher or a student" signal - confirmed live (2026-08-22) against production
        /// data and the frontend's own source (Gamaedtech-frontv3/app/types/user/index.ts):
        /// <c>5 = Teacher</c>, <c>6 = Student</c>. <c>3</c> is reserved for a third type (routes to
        /// /test-maker in the frontend instead of the Teacher/Student picker) but had zero real users as of
        /// the same check. This backend has no local enum for the other values (<see langword="null"/>, 1, 2,
        /// 7) - it just mirrors whatever gama-api reports, verbatim, never needing to interpret them itself.
        /// <b>Not the same concept as <see cref="Role"/>'s Teacher/Student values</b> - same words, unrelated
        /// mechanism: Role is this app's own RBAC (checked via User.IsInRole), Group is opaque data mirrored
        /// from gama-api's own "Group" type via <c>CoreProvider.cs</c>'s <c>info?.Group.ValueOf&lt;int?&gt;()</c>.
        /// In practice Role.Teacher/Role.Student are essentially unassigned in real data - Group is what
        /// actually distinguishes teacher/student today. See docs/business/identity-and-access.md, "User type
        /// (ApplicationUser.Group)".
        /// </summary>
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

        /// <summary>
        /// Set once by NudgeService.EvaluateAndSendNudgesAsync, the first time a user has zero remaining gaps
        /// across every currently-defined NudgeType (role/avatar/name/bio/skills/experience) - null until then.
        /// A one-way latch, not a live/recomputed-on-read signal: excludes the user from that job's candidate
        /// pool forever afterward, without re-deriving completeness from the live columns every night, which is
        /// the whole point (see docs/business/notifications.md, "Eligibility, cooldown, and send cap" - added
        /// 2026-09-02 once the nightly full-table-ish scan was flagged as a real, if not yet urgent, cost).
        /// Deliberately named apart from the *different*, always-freshly-computed completeness the dashboard
        /// shows (IdentityService.BuildDashboardProfileCompletionAsync checks a slightly different field set -
        /// it includes CurrentStatusSentence, which no NudgeType covers, and excludes Group, which
        /// NudgeType.RoleMissing does cover) - the two are related concepts, not the same signal, and this
        /// column is not read by the dashboard. If a field this column depends on is ever cleared again after
        /// being set (an admin edit, a future data migration), this column does NOT self-correct - the user
        /// simply stops being nudged for anything from then on. Accepted deliberately: cheap fix for a real
        /// cost, not worth automatic re-detection for how rare that case is.
        /// </summary>
        [Column(nameof(AllNudgesCompletedAt), DataType.DateTimeOffset)]
        public DateTimeOffset? AllNudgesCompletedAt { get; set; }

        /// <summary>
        /// Null = still subscribed (default); set = opted out of the nudge system entirely, via the one-click
        /// unsubscribe link every nudge email carries (NudgesController.Unsubscribe) or the authenticated
        /// subscription toggle (NudgeService.SetNudgeSubscriptionAsync) - excludes the user from
        /// NudgeService.EvaluateAndSendNudgesAsync's candidate pool regardless of which fields are still
        /// missing. Unlike AllNudgesCompletedAt above, this one IS meant to be reversible - the authenticated
        /// toggle clears it back to null. See docs/business/notifications.md.
        /// </summary>
        [Column(nameof(NudgesOptedOutAt), DataType.DateTimeOffset)]
        public DateTimeOffset? NudgesOptedOutAt { get; set; }

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

            // Speeds up the public profiles list (GetProfilesListAsync): filters to Public
            // profiles and services its activity-based default sort (bucketed off LastLoginDate)
            // without a full table scan.
            _ = builder.HasIndex(e => new { e.ProfileVisibility, e.LastLoginDate }).IsDescending(false, true);

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
