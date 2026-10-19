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

    using Microsoft.EntityFrameworkCore;
    using Microsoft.EntityFrameworkCore.Metadata.Builders;

    /// <summary>
    /// A one-time first-payment discount emailed to a user whose subscription checkout expired unpaid (the
    /// CheckoutAbandoned nudge). The email link carries a Data Protection token of <see cref="Id"/>; redeeming it opens
    /// a new Stripe checkout for the same plan and interval with a "once" coupon of <see cref="DiscountPercent"/>. The
    /// percent is stored here, not read from settings at redeem time, so the user gets what the email promised. See
    /// docs/business/notifications.md, "Checkout recovery".
    /// </summary>
    [Table(nameof(CheckoutRecoveryOffer))]
    public class CheckoutRecoveryOffer : IEntity<CheckoutRecoveryOffer, long>, IUserId<long>, ICreationDate
    {
        [System.ComponentModel.DataAnnotations.Key]
        [Column(nameof(Id), DataType.Long)]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        [Required]
        public long Id { get; set; }

        [Column(nameof(UserId), DataType.Long)]
        [Required]
        public long UserId { get; set; }
        public ApplicationUser? User { get; set; }

        /// <summary>The expired checkout's payment this offer was sent for. Unique: one offer per abandoned checkout.</summary>
        [Column(nameof(AbandonedPaymentId), DataType.Long)]
        [Required]
        public long AbandonedPaymentId { get; set; }
        public Payment? AbandonedPayment { get; set; }

        [Column(nameof(SubscriptionPlanId), DataType.Long)]
        [Required]
        public long SubscriptionPlanId { get; set; }
        public SubscriptionPlan? SubscriptionPlan { get; set; }

        [Column(nameof(BillingInterval), DataType.Byte)]
        [Required]
        public BillingInterval BillingInterval { get; set; }

        [Column(nameof(DiscountPercent), DataType.Int)]
        [Required]
        public int DiscountPercent { get; set; }

        [Column(nameof(CreationDate), DataType.DateTimeOffset)]
        [Required]
        public DateTimeOffset CreationDate { get; set; }

        [Column(nameof(ExpirationDate), DataType.DateTimeOffset)]
        [Required]
        public DateTimeOffset ExpirationDate { get; set; }

        /// <summary>The latest discounted checkout opened from this offer (each click opens a fresh one).</summary>
        [Column(nameof(PaymentId), DataType.Long)]
        public long? PaymentId { get; set; }
        public Payment? Payment { get; set; }

        public void Configure([NotNull] EntityTypeBuilder<CheckoutRecoveryOffer> builder)
        {
            _ = builder.OwnEnumeration<CheckoutRecoveryOffer, BillingInterval, byte>(t => t.BillingInterval);
            _ = builder.HasIndex(t => t.AbandonedPaymentId).IsUnique();
            _ = builder.HasOne(t => t.User).WithMany().HasForeignKey(t => t.UserId).OnDelete(DeleteBehavior.NoAction);
            _ = builder.HasOne(t => t.AbandonedPayment).WithMany().HasForeignKey(t => t.AbandonedPaymentId).OnDelete(DeleteBehavior.NoAction);
            _ = builder.HasOne(t => t.Payment).WithMany().HasForeignKey(t => t.PaymentId).OnDelete(DeleteBehavior.NoAction);
            _ = builder.HasOne(t => t.SubscriptionPlan).WithMany().HasForeignKey(t => t.SubscriptionPlanId).OnDelete(DeleteBehavior.NoAction);
        }
    }
}
