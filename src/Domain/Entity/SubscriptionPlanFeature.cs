namespace GamaEdtech.Domain.Entity
{
    using System.Diagnostics.CodeAnalysis;

    using GamaEdtech.Common.Data;
    using GamaEdtech.Common.DataAccess.Entities;
    using GamaEdtech.Common.DataAnnotation;
    using GamaEdtech.Common.DataAnnotation.Schema;

    using Microsoft.EntityFrameworkCore;
    using Microsoft.EntityFrameworkCore.Metadata.Builders;

    [Table(nameof(SubscriptionPlanFeature))]
    public class SubscriptionPlanFeature : IEntity<SubscriptionPlanFeature, long>
    {
        [System.ComponentModel.DataAnnotations.Key]
        [Column(nameof(Id), DataType.Long)]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        [Required]
        public long Id { get; set; }

        [Column(nameof(SubscriptionPlanId), DataType.Long)]
        [Required]
        public long SubscriptionPlanId { get; set; }
        public SubscriptionPlan? SubscriptionPlan { get; set; }

        [Column(nameof(FeatureId), DataType.Int)]
        [Required]
        public int FeatureId { get; set; }
        public Feature? Feature { get; set; }

        /// <summary>Feature allowance for this plan; <see langword="null"/> means unlimited.</summary>
        [Column(nameof(Limit), DataType.Int)]
        public int? Limit { get; set; }

        /// <summary>
        /// <see langword="null"/> means this feature has its own independent quota (default). A non-null value
        /// shared by two or more <see cref="SubscriptionPlanFeature"/> rows under the same plan means those
        /// features are one group sharing a single pooled quota instead of separate ones - all rows sharing a
        /// key must carry the same <see cref="Limit"/> (enforced in <c>SubscriptionService.SetPlanFeaturesAsync</c>,
        /// not the DB). Server-generated (a GUID) when a group is created via <c>SetPlanFeaturesAsync</c>'s
        /// grouped request shape - never admin-typed, so there's nothing to mistype into two unlinked groups.
        /// </summary>
        [Column(nameof(FeatureGroupKey), DataType.String)]
        [StringLength(100)]
        public string? FeatureGroupKey { get; set; }

        /// <summary>
        /// User-facing description of the pooled bucket (e.g. "Shared download allowance covering Pastpaper and
        /// Exam downloads") - a single feature has its own <see cref="Feature.Description"/> to show, but a
        /// pooled group has no single feature to describe it, so this is required whenever
        /// <see cref="FeatureGroupKey"/> is set (enforced in <c>SubscriptionService.SetPlanFeaturesAsync</c>, not
        /// the DB) and duplicated across every row in the group, the same way <see cref="Limit"/> already is.
        /// </summary>
        [Column(nameof(FeatureGroupDescription), DataType.UnicodeString)]
        [StringLength(500)]
        public string? FeatureGroupDescription { get; set; }

        public void Configure([NotNull] EntityTypeBuilder<SubscriptionPlanFeature> builder)
        {
            _ = builder.HasOne(t => t.SubscriptionPlan).WithMany(t => t.PlanFeatures).HasForeignKey(t => t.SubscriptionPlanId).OnDelete(DeleteBehavior.Cascade);
            _ = builder.HasOne(t => t.Feature).WithMany().HasForeignKey(t => t.FeatureId).OnDelete(DeleteBehavior.NoAction);
            _ = builder.HasIndex(t => new { t.SubscriptionPlanId, t.FeatureId }).IsUnique();
        }
    }
}
