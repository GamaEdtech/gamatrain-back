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

    [Table(nameof(UserSubscription))]
    public class UserSubscription : IEntity<UserSubscription, long>, IUserId<long>, ICreationDate
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

        [Column(nameof(SubscriptionPlanId), DataType.Long)]
        [Required]
        public long SubscriptionPlanId { get; set; }
        public SubscriptionPlan? SubscriptionPlan { get; set; }

        [Column(nameof(Status), DataType.Byte)]
        [Required]
        public UserSubscriptionStatus Status { get; set; }

        [Column(nameof(CreationDate), DataType.DateTimeOffset)]
        [Required]
        public DateTimeOffset CreationDate { get; set; }

        [Column(nameof(StartDate), DataType.DateTimeOffset)]
        public DateTimeOffset? StartDate { get; set; }

        [Column(nameof(ExpirationDate), DataType.DateTimeOffset)]
        public DateTimeOffset? ExpirationDate { get; set; }

        /// <summary>Snapshot of the resolved plan price at purchase time; plan price edits never affect it.</summary>
        [Column(nameof(PricePaid), DataType.Decimal)]
        [Required]
        public decimal PricePaid { get; set; }

        [Column(nameof(Currency), DataType.Byte)]
        [Required]
        public Currency Currency { get; set; }

        /// <summary>Snapshot of the resolved plan price's billing interval at purchase time; later price/plan edits never affect it.</summary>
        [Column(nameof(BillingInterval), DataType.Byte)]
        [Required]
        public BillingInterval BillingInterval { get; set; }

        public virtual ICollection<UserSubscriptionQuota> Quotas { get; set; } = [];

        public virtual ICollection<Payment> Payments { get; set; } = [];

        public void Configure([NotNull] EntityTypeBuilder<UserSubscription> builder)
        {
            _ = builder.Property(t => t.PricePaid).HasPrecision(36, 18);
            _ = builder.OwnEnumeration<UserSubscription, UserSubscriptionStatus, byte>(t => t.Status);
            _ = builder.OwnEnumeration<UserSubscription, Currency, byte>(t => t.Currency);
            _ = builder.OwnEnumeration<UserSubscription, BillingInterval, byte>(t => t.BillingInterval);
            _ = builder.HasOne(t => t.User).WithMany().HasForeignKey(t => t.UserId).OnDelete(DeleteBehavior.NoAction);
            _ = builder.HasOne(t => t.SubscriptionPlan).WithMany().HasForeignKey(t => t.SubscriptionPlanId).OnDelete(DeleteBehavior.NoAction);
            _ = builder.HasIndex(t => new { t.UserId, t.Status });
            _ = builder.HasIndex(t => new { t.Status, t.ExpirationDate });
        }
    }
}
