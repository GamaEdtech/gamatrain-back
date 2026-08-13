namespace GamaEdtech.Domain.Entity
{
    using System.Diagnostics.CodeAnalysis;

    using GamaEdtech.Common.Data;
    using GamaEdtech.Common.DataAccess.Entities;
    using GamaEdtech.Common.DataAnnotation;
    using GamaEdtech.Common.DataAnnotation.Schema;

    using Microsoft.EntityFrameworkCore;
    using Microsoft.EntityFrameworkCore.Metadata.Builders;

    /// <summary>
    /// One allowance bucket of a user subscription, covering one or more features (see
    /// <see cref="Features"/> - pooled features share the same bucket). <see cref="Limit"/> is
    /// snapshotted from <see cref="SubscriptionPlanFeature"/> at activation; later plan edits never
    /// touch these rows.
    /// </summary>
    [Table(nameof(UserSubscriptionQuota))]
    public class UserSubscriptionQuota : IEntity<UserSubscriptionQuota, long>
    {
        [System.ComponentModel.DataAnnotations.Key]
        [Column(nameof(Id), DataType.Long)]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        [Required]
        public long Id { get; set; }

        [Column(nameof(UserSubscriptionId), DataType.Long)]
        [Required]
        public long UserSubscriptionId { get; set; }
        public UserSubscription? UserSubscription { get; set; }

        /// <summary>Snapshotted allowance; <see langword="null"/> means unlimited.</summary>
        [Column(nameof(Limit), DataType.Int)]
        public int? Limit { get; set; }

        [Column(nameof(Used), DataType.Int)]
        [Required]
        public int Used { get; set; }

        /// <summary>
        /// Snapshotted from <see cref="SubscriptionPlanFeature.FeatureGroupDescription"/> at activation;
        /// <see langword="null"/> for an unpooled bucket (display the single feature's own description instead,
        /// via <see cref="Features"/>).
        /// </summary>
        [Column(nameof(Description), DataType.UnicodeString)]
        [StringLength(500)]
        public string? Description { get; set; }

        /// <summary>The feature(s) this bucket covers - more than one when the plan pooled them via <see cref="SubscriptionPlanFeature.FeatureGroupKey"/>.</summary>
        public virtual ICollection<UserSubscriptionQuotaFeature> Features { get; set; } = [];

        public void Configure([NotNull] EntityTypeBuilder<UserSubscriptionQuota> builder) =>
            _ = builder.HasOne(t => t.UserSubscription).WithMany(t => t.Quotas).HasForeignKey(t => t.UserSubscriptionId).OnDelete(DeleteBehavior.Cascade);
    }
}
