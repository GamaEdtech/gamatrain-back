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

        [Column(nameof(Price), DataType.Decimal)]
        [Required]
        public decimal Price { get; set; }

        [Column(nameof(Currency), DataType.Byte)]
        [Required]
        public Currency Currency { get; set; }

        [Column(nameof(Polygon), TypeName = "geography")]
        public Polygon? Polygon { get; set; }

        [Column(nameof(Point))]
        [Required]
        public long Point { get; set; }

        [Column(nameof(IsActive), DataType.Boolean)]
        [Required]
        public bool IsActive { get; set; }

        [Column(nameof(Highlight), DataType.Boolean)]
        [Required]
        public bool Highlight { get; set; }

        [Column(nameof(BillingInterval), DataType.Byte)]
        [Required]
        public BillingInterval BillingInterval { get; set; }

        public void Configure([NotNull] EntityTypeBuilder<SubscriptionPlan> builder)
        {
            _ = builder.Property(t => t.Price).HasPrecision(36, 18);
            _ = builder.OwnEnumeration<SubscriptionPlan, Currency, byte>(t => t.Currency);
            _ = builder.OwnEnumeration<SubscriptionPlan, BillingInterval, byte>(t => t.BillingInterval);
        }
    }
}
