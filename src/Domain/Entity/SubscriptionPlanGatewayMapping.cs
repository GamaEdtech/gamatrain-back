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

    /// <summary>
    /// Maps a regional plan price to the external product/plan objects of a payment gateway
    /// (e.g. Stripe Product/Price, PayPal Product/Plan). Written by admin now; consumed by the
    /// native recurring-billing integration in a later phase.
    /// </summary>
    [Table(nameof(SubscriptionPlanGatewayMapping))]
    public class SubscriptionPlanGatewayMapping : IEntity<SubscriptionPlanGatewayMapping, long>
    {
        [System.ComponentModel.DataAnnotations.Key]
        [Column(nameof(Id), DataType.Long)]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        [Required]
        public long Id { get; set; }

        [Column(nameof(SubscriptionPlanPriceId), DataType.Long)]
        [Required]
        public long SubscriptionPlanPriceId { get; set; }
        public SubscriptionPlanPrice? SubscriptionPlanPrice { get; set; }

        [Column(nameof(Gateway), DataType.Byte)]
        [Required]
        public PaymentGateway Gateway { get; set; }

        [Column(nameof(ExternalProductId), DataType.String)]
        [StringLength(200)]
        [Required]
        public string? ExternalProductId { get; set; }

        [Column(nameof(ExternalPlanId), DataType.String)]
        [StringLength(200)]
        public string? ExternalPlanId { get; set; }

        public void Configure([NotNull] EntityTypeBuilder<SubscriptionPlanGatewayMapping> builder)
        {
            _ = builder.OwnEnumeration<SubscriptionPlanGatewayMapping, PaymentGateway, byte>(t => t.Gateway);
            _ = builder.HasOne(t => t.SubscriptionPlanPrice).WithMany().HasForeignKey(t => t.SubscriptionPlanPriceId).OnDelete(DeleteBehavior.Cascade);
            _ = builder.HasIndex(t => new { t.SubscriptionPlanPriceId, t.Gateway }).IsUnique();
        }
    }
}
