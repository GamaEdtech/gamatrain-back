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
    /// Per-feature allowance of one user subscription. <see cref="Limit"/> is snapshotted from
    /// <see cref="SubscriptionPlanFeature"/> at activation; later plan edits never touch these rows.
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

        [Column(nameof(FeatureId), DataType.Int)]
        [Required]
        public int FeatureId { get; set; }
        public Feature? Feature { get; set; }

        /// <summary>Snapshotted allowance; <see langword="null"/> means unlimited.</summary>
        [Column(nameof(Limit), DataType.Int)]
        public int? Limit { get; set; }

        [Column(nameof(Used), DataType.Int)]
        [Required]
        public int Used { get; set; }

        public void Configure([NotNull] EntityTypeBuilder<UserSubscriptionQuota> builder)
        {
            _ = builder.HasOne(t => t.UserSubscription).WithMany(t => t.Quotas).HasForeignKey(t => t.UserSubscriptionId).OnDelete(DeleteBehavior.Cascade);
            _ = builder.HasOne(t => t.Feature).WithMany().HasForeignKey(t => t.FeatureId).OnDelete(DeleteBehavior.NoAction);
            _ = builder.HasIndex(t => new { t.UserSubscriptionId, t.FeatureId }).IsUnique();
        }
    }
}
