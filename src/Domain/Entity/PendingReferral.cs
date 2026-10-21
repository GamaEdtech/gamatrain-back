namespace GamaEdtech.Domain.Entity
{
    using System.Diagnostics.CodeAnalysis;

    using GamaEdtech.Common.Data;
    using GamaEdtech.Common.DataAccess.Entities;
    using GamaEdtech.Common.DataAnnotation;
    using GamaEdtech.Common.DataAnnotation.Schema;

    using Microsoft.EntityFrameworkCore.Metadata.Builders;

    /// <summary>
    /// An invite code handed to the legacy OTP register (2026-10-21). That register is a passthrough to gama-api and
    /// creates no local user; the local user appears at the login that follows (IdentityService.SyncLegacyAuthAsync),
    /// which links the inviter when it finds a row here for the same email/phone. Short-lived; deleted when used or
    /// expired. See docs/business/referrals.md.
    /// </summary>
    [Table(nameof(PendingReferral))]
    public class PendingReferral : IEntity<PendingReferral, long>, ICreationDate
    {
        [System.ComponentModel.DataAnnotations.Key]
        [Column(nameof(Id), DataType.Long)]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        [Required]
        public long Id { get; set; }

        /// <summary>The email or phone number being registered, normalized (trimmed, upper-case). Unique: a later register replaces the code.</summary>
        [Column(nameof(Identity), DataType.UnicodeString)]
        [StringLength(256)]
        [Required]
        public string? Identity { get; set; }

        [Column(nameof(ReferralCode), DataType.String)]
        [StringLength(10)]
        [Required]
        public string? ReferralCode { get; set; }

        [Column(nameof(CreationDate), DataType.DateTimeOffset)]
        [Required]
        public DateTimeOffset CreationDate { get; set; }

        [Column(nameof(ExpirationDate), DataType.DateTimeOffset)]
        [Required]
        public DateTimeOffset ExpirationDate { get; set; }

        public void Configure([NotNull] EntityTypeBuilder<PendingReferral> builder) =>
            _ = builder.HasIndex(t => t.Identity).IsUnique();
    }
}
