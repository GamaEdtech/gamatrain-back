namespace GamaEdtech.Domain.Entity
{
    using System.Diagnostics.CodeAnalysis;

    using GamaEdtech.Common.Data;
    using GamaEdtech.Common.DataAccess.Entities;
    using GamaEdtech.Common.DataAnnotation;
    using GamaEdtech.Common.DataAnnotation.Schema;
    using GamaEdtech.Domain.Entity.Identity;

    using Microsoft.EntityFrameworkCore;
    using Microsoft.EntityFrameworkCore.Metadata.Builders;

    /// <summary>
    /// A network a user of the invite program was seen on (2026-10-22), kept only to flag likely self-invites on the admin
    /// dashboard: an invited user who signed up from a network their teacher also used. Recorded for a teacher when they
    /// join or open Invite &amp; Earn, and for an invited user at sign-up. Only a SHA-256 hash of the visitor's real IP is
    /// stored (Cloudflare's CF-Connecting-IP), never the address. See docs/business/admin-dashboard.md.
    /// </summary>
    [Table(nameof(ReferralIpSighting))]
    public class ReferralIpSighting : IEntity<ReferralIpSighting, long>, IUserId<long>
    {
        [System.ComponentModel.DataAnnotations.Key]
        [Column(nameof(Id), DataType.Long)]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        [Required]
        public long Id { get; set; }

        [Column(nameof(UserId), DataType.Long)]
        [Required]
        public long UserId { get; set; }
        public ApplicationUser? User { get; set; }

        /// <summary>Lower-case hex SHA-256 of the IP address.</summary>
        [Column(nameof(IpHash), DataType.String)]
        [StringLength(64)]
        [Required]
        public string? IpHash { get; set; }

        [Column(nameof(LastSeenDate), DataType.DateTimeOffset)]
        [Required]
        public DateTimeOffset LastSeenDate { get; set; }

        public void Configure([NotNull] EntityTypeBuilder<ReferralIpSighting> builder)
        {
            _ = builder.HasIndex(t => new { t.UserId, t.IpHash }).IsUnique();
            _ = builder.HasOne(t => t.User).WithMany().HasForeignKey(t => t.UserId).OnDelete(DeleteBehavior.NoAction);
        }
    }
}
