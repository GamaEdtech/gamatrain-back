namespace GamaEdtech.Domain.Entity
{
    using System.Diagnostics.CodeAnalysis;

    using GamaEdtech.Common.Data;
    using GamaEdtech.Common.Data.Enumeration;
    using GamaEdtech.Common.DataAccess.Entities;
    using GamaEdtech.Common.DataAnnotation;
    using GamaEdtech.Common.DataAnnotation.Schema;
    using GamaEdtech.Domain.Entity.Identity;
    using GamaEdtech.Domain.Enumeration;

    using Microsoft.EntityFrameworkCore.Metadata.Builders;

    using NetTopologySuite.Geometries;

    [Table(nameof(SubscriptionPlan))]
    public class SubscriptionPlan : VersionableEntity<ApplicationUser, long, long?>, IEntity<SubscriptionPlan, long>
    {
        [System.ComponentModel.DataAnnotations.Key]
        [Column(nameof(Id), DataType.Long)]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        [Required]
        public long Id { get; set; }

        [Column(nameof(Title), DataType.UnicodeString)]
        [StringLength(100)]
        [Required]
        public string? Title { get; set; }

        [Column(nameof(Polygon), TypeName = "geography")]
        public Polygon? Polygon { get; set; }

        [Column(nameof(IsActive), DataType.Boolean)]
        [Required]
        public bool IsActive { get; set; }

        [Column(nameof(Highlight), DataType.Boolean)]
        [Required]
        public bool Highlight { get; set; }

        [Column(nameof(BillingInterval), DataType.Byte)]
        [Required]
        public BillingInterval BillingInterval { get; set; }

        public virtual ICollection<SubscriptionPlanFeature> PlanFeatures { get; set; } = [];

        public virtual ICollection<SubscriptionPlanPrice> Prices { get; set; } = [];

        public void Configure([NotNull] EntityTypeBuilder<SubscriptionPlan> builder) => _ = builder.OwnEnumeration<SubscriptionPlan, BillingInterval, byte>(t => t.BillingInterval);
    }
}
