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
    /// One feature covered by a <see cref="UserSubscriptionQuota"/> bucket. Usually one row per bucket
    /// (an unpooled feature); more than one when <see cref="SubscriptionPlanFeature.FeatureGroupKey"/>
    /// pooled several features onto the same bucket at activation.
    /// </summary>
    [Table(nameof(UserSubscriptionQuotaFeature))]
    public class UserSubscriptionQuotaFeature : IEntity<UserSubscriptionQuotaFeature, long>
    {
        [System.ComponentModel.DataAnnotations.Key]
        [Column(nameof(Id), DataType.Long)]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        [Required]
        public long Id { get; set; }

        [Column(nameof(UserSubscriptionQuotaId), DataType.Long)]
        [Required]
        public long UserSubscriptionQuotaId { get; set; }
        public UserSubscriptionQuota? UserSubscriptionQuota { get; set; }

        /// <summary>Denormalized from the parent quota row purely to let the unique index below guarantee one bucket per feature per subscription.</summary>
        [Column(nameof(UserSubscriptionId), DataType.Long)]
        [Required]
        public long UserSubscriptionId { get; set; }

        [Column(nameof(FeatureId), DataType.Int)]
        [Required]
        public int FeatureId { get; set; }
        public Feature? Feature { get; set; }

        public void Configure([NotNull] EntityTypeBuilder<UserSubscriptionQuotaFeature> builder)
        {
            _ = builder.HasOne(t => t.UserSubscriptionQuota).WithMany(t => t.Features).HasForeignKey(t => t.UserSubscriptionQuotaId).OnDelete(DeleteBehavior.Cascade);
            _ = builder.HasOne(t => t.Feature).WithMany().HasForeignKey(t => t.FeatureId).OnDelete(DeleteBehavior.NoAction);
            _ = builder.HasIndex(t => new { t.UserSubscriptionId, t.FeatureId }).IsUnique();
        }
    }
}
