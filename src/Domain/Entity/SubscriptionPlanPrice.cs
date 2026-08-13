namespace GamaEdtech.Domain.Entity
{
    using System.Diagnostics.CodeAnalysis;

    using GamaEdtech.Common.Data;
    using GamaEdtech.Common.Data.Enumeration;
    using GamaEdtech.Common.DataAccess.Entities;
    using GamaEdtech.Common.DataAnnotation;
    using GamaEdtech.Common.DataAnnotation.Schema;
    using GamaEdtech.Domain.Enumeration;

    using Microsoft.EntityFrameworkCore;
    using Microsoft.EntityFrameworkCore.Metadata.Builders;

    [Table(nameof(SubscriptionPlanPrice))]
    public class SubscriptionPlanPrice : IEntity<SubscriptionPlanPrice, long>
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

        /// <summary>Null means the global default price; at most one default row per plan.</summary>
        [Column(nameof(CountryCode), DataType.String)]
        [StringLength(10)]
        public string? CountryCode { get; set; }

        [Column(nameof(Currency), DataType.Byte)]
        [Required]
        public Currency Currency { get; set; }

        [Column(nameof(Price), DataType.Decimal)]
        [Required]
        public decimal Price { get; set; }

        [Column(nameof(BillingInterval), DataType.Byte)]
        [Required]
        public BillingInterval BillingInterval { get; set; }

        public void Configure([NotNull] EntityTypeBuilder<SubscriptionPlanPrice> builder)
        {
            _ = builder.Property(t => t.Price).HasPrecision(36, 18);
            _ = builder.OwnEnumeration<SubscriptionPlanPrice, Currency, byte>(t => t.Currency);
            _ = builder.OwnEnumeration<SubscriptionPlanPrice, BillingInterval, byte>(t => t.BillingInterval);
            _ = builder.HasOne(t => t.SubscriptionPlan).WithMany(t => t.Prices).HasForeignKey(t => t.SubscriptionPlanId).OnDelete(DeleteBehavior.Cascade);
            _ = builder.HasIndex(t => new { t.SubscriptionPlanId, t.CountryCode, t.BillingInterval }).IsUnique();
        }
    }
}
