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
    /// A teacher's place in the invite program (2026-10-21), one row per teacher. Created as an offer when a user first
    /// becomes a teacher (OfferPercent = the admin's ReferralCommissionPercent at that moment, emailed to them), or when an
    /// existing teacher joins from the dashboard. Joining locks <see cref="Percent"/> for life: the offered percent if the
    /// offer hasn't expired, otherwise the setting's value at join time. Accrual always reads <see cref="Percent"/>, never
    /// the setting, so an admin change only affects teachers who join afterwards. See docs/business/referrals.md.
    /// </summary>
    [Table(nameof(ReferralMembership))]
    public class ReferralMembership : IEntity<ReferralMembership, long>, IUserId<long>
    {
        [System.ComponentModel.DataAnnotations.Key]
        [Column(nameof(Id), DataType.Long)]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        [Required]
        public long Id { get; set; }

        /// <summary>The teacher. Unique.</summary>
        [Column(nameof(UserId), DataType.Long)]
        [Required]
        public long UserId { get; set; }
        public ApplicationUser? User { get; set; }

        /// <summary>The percent promised in the offer email.</summary>
        [Column(nameof(OfferPercent), DataType.Decimal)]
        [Required]
        public decimal OfferPercent { get; set; }

        [Column(nameof(OfferDate), DataType.DateTimeOffset)]
        [Required]
        public DateTimeOffset OfferDate { get; set; }

        /// <summary>Until when joining locks <see cref="OfferPercent"/>. Null: the offer never expires.</summary>
        [Column(nameof(OfferExpirationDate), DataType.DateTimeOffset)]
        public DateTimeOffset? OfferExpirationDate { get; set; }

        /// <summary>When the offer email was sent (or skipped, for a teacher without an email). Null: still to send.</summary>
        [Column(nameof(OfferEmailSentDate), DataType.DateTimeOffset)]
        public DateTimeOffset? OfferEmailSentDate { get; set; }

        /// <summary>Null until the teacher joins; only a joined teacher's invite code links new users.</summary>
        [Column(nameof(JoinedDate), DataType.DateTimeOffset)]
        public DateTimeOffset? JoinedDate { get; set; }

        /// <summary>The percent locked at join, applied to every payment of every user this teacher invites.</summary>
        [Column(nameof(Percent), DataType.Decimal)]
        public decimal? Percent { get; set; }

        public void Configure([NotNull] EntityTypeBuilder<ReferralMembership> builder)
        {
            _ = builder.Property(t => t.OfferPercent).HasPrecision(5, 2);
            _ = builder.Property(t => t.Percent).HasPrecision(5, 2);
            _ = builder.HasIndex(t => t.UserId).IsUnique();
            _ = builder.HasIndex(t => t.OfferEmailSentDate);
            _ = builder.HasOne(t => t.User).WithMany().HasForeignKey(t => t.UserId).OnDelete(DeleteBehavior.NoAction);
        }
    }
}
