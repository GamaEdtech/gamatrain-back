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

        public void Configure([NotNull] EntityTypeBuilder<SubscriptionPlanFeature> builder)
        {
            _ = builder.HasOne(t => t.SubscriptionPlan).WithMany(t => t.PlanFeatures).HasForeignKey(t => t.SubscriptionPlanId).OnDelete(DeleteBehavior.Cascade);
            _ = builder.HasOne(t => t.Feature).WithMany().HasForeignKey(t => t.FeatureId).OnDelete(DeleteBehavior.NoAction);
            _ = builder.HasIndex(t => new { t.SubscriptionPlanId, t.FeatureId }).IsUnique();
        }
    }
}
