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

        /// <summary>
        /// The gateway's own recurring-subscription id (e.g. Stripe's <c>sub_...</c>), captured at activation
        /// from the completed Checkout Session. <see langword="null"/> for a one-time/GamaTrain subscription,
        /// or a Stripe subscription that hasn't finished activating yet - doubles as the "is this actually
        /// recurring" signal, so a client-facing AutoRenews flag is just <c>ExternalSubscriptionId is not null</c>.
        /// </summary>
        [Column(nameof(ExternalSubscriptionId), DataType.String)]
        [StringLength(200)]
        public string? ExternalSubscriptionId { get; set; }

        /// <summary>
        /// Set when the user requests cancellation (<c>POST subscriptions/me/cancel</c>). The subscription stays
        /// <see cref="UserSubscriptionStatus.Active"/> - quota still usable - until <see cref="ExpirationDate"/>,
        /// at which point the existing <c>customer.subscription.deleted</c> webhook path (unchanged) flips
        /// <see cref="Status"/> to <see cref="UserSubscriptionStatus.Cancelled"/>, exactly like it already does
        /// for any other subscription-ended reason.
        /// </summary>
        [Column(nameof(CancelAtPeriodEnd), DataType.Boolean)]
        [Required]
        public bool CancelAtPeriodEnd { get; set; }

        /// <summary>
        /// Set when the user requests a downgrade (<c>POST subscriptions/me/switch</c> to a cheaper plan) -
        /// the subscription stays on its current <see cref="SubscriptionPlanId"/>/<see cref="PricePaid"/> until
        /// <see cref="ExpirationDate"/>, at which point the renewal webhook path (<c>RenewSubscriptionAsync</c>)
        /// applies this plan instead of just extending the current one. <see langword="null"/> when no downgrade
        /// is pending. An upgrade never sets this - it applies immediately instead.
        /// </summary>
        [Column(nameof(PendingSwitchSubscriptionPlanId), DataType.Long)]
        public long? PendingSwitchSubscriptionPlanId { get; set; }
        public SubscriptionPlan? PendingSwitchSubscriptionPlan { get; set; }

        /// <summary>
        /// Snapshot of the resolved price for <see cref="PendingSwitchSubscriptionPlanId"/> at request time, not
        /// re-resolved at renewal - must match whatever price the gateway's own Subscription Schedule already
        /// locked in, not a possibly-since-edited <see cref="SubscriptionPlanPrice"/> row. No paired
        /// PendingSwitchCurrency column - a pending switch is only ever created when it's already the same
        /// <see cref="Currency"/> as the current subscription, so nothing new to track there.
        /// </summary>
        [Column(nameof(PendingSwitchPricePaid), DataType.Decimal)]
        public decimal? PendingSwitchPricePaid { get; set; }

        /// <summary>
        /// Set when the gateway reports a failed renewal charge (Stripe's <c>invoice.payment_failed</c>,
        /// while its own Smart Retries are still ongoing) - cleared back to <see langword="null"/> the next
        /// time <c>RenewSubscriptionAsync</c> succeeds (a later retry succeeded, or a manual card update
        /// triggered a successful charge). Visibility only - deliberately never changes <see cref="Status"/>,
        /// <see cref="ExpirationDate"/>, or quota consumption; the subscription stays exactly as usable as
        /// before until <see cref="ExpirationDate"/> naturally passes or Stripe's own retries exhaust and fire
        /// <c>customer.subscription.deleted</c> (unchanged, see <c>CancelSubscriptionAsync</c>). Lets a client
        /// show a "payment failed, please update your card" prompt during Stripe's own dunning window, which
        /// was previously entirely invisible locally - see docs/business/subscriptions.md, "Dunning
        /// visibility".
        /// </summary>
        [Column(nameof(LastPaymentFailedDate), DataType.DateTimeOffset)]
        public DateTimeOffset? LastPaymentFailedDate { get; set; }

        public virtual ICollection<UserSubscriptionQuota> Quotas { get; set; } = [];

        public virtual ICollection<Payment> Payments { get; set; } = [];

        public void Configure([NotNull] EntityTypeBuilder<UserSubscription> builder)
        {
            _ = builder.Property(t => t.PricePaid).HasPrecision(36, 18);
            _ = builder.Property(t => t.PendingSwitchPricePaid).HasPrecision(36, 18);
            _ = builder.OwnEnumeration<UserSubscription, UserSubscriptionStatus, byte>(t => t.Status);
            _ = builder.OwnEnumeration<UserSubscription, Currency, byte>(t => t.Currency);
            _ = builder.OwnEnumeration<UserSubscription, BillingInterval, byte>(t => t.BillingInterval);
            _ = builder.HasOne(t => t.User).WithMany().HasForeignKey(t => t.UserId).OnDelete(DeleteBehavior.NoAction);
            _ = builder.HasOne(t => t.SubscriptionPlan).WithMany().HasForeignKey(t => t.SubscriptionPlanId).OnDelete(DeleteBehavior.NoAction);
            _ = builder.HasOne(t => t.PendingSwitchSubscriptionPlan).WithMany().HasForeignKey(t => t.PendingSwitchSubscriptionPlanId).OnDelete(DeleteBehavior.NoAction);
            _ = builder.HasIndex(t => new { t.UserId, t.Status });
            _ = builder.HasIndex(t => new { t.Status, t.ExpirationDate });
        }
    }
}
