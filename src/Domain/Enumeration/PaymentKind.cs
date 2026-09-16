namespace GamaEdtech.Domain.Enumeration
{
    using GamaEdtech.Common.Data.Enumeration;
    using GamaEdtech.Common.DataAnnotation;

    /// <summary>
    /// What a <see cref="Domain.Entity.Payment"/> actually represents - previously undistinguishable from the
    /// entity alone (every payment looked the same regardless of whether it was a fresh purchase, a renewal, or
    /// a plan/interval switch), which made a per-kind breakdown (e.g. a revenue chart split by new vs. renewal
    /// vs. switch) impossible without fragile heuristics (grouping by <see cref="Domain.Entity.Payment.
    /// UserSubscriptionId"/> and eyeballing <see cref="Domain.Entity.Payment.TransactionId"/>'s prefix).
    /// </summary>
    public sealed class PaymentKind : Enumeration<PaymentKind, byte>
    {
        /// <summary>A non-subscription points top-up (<see cref="Domain.Entity.Payment.UserSubscriptionId"/> is null).</summary>
        [Display]
        public static readonly PaymentKind PointsTopUp = new(nameof(PointsTopUp), 0);

        /// <summary>A subscription's very first period - <c>PaymentService.CreatePaymentAsync</c>, on any gateway.</summary>
        [Display]
        public static readonly PaymentKind NewSubscription = new(nameof(NewSubscription), 1);

        /// <summary>
        /// An ordinary renewal charge for a subsequent period - <c>PaymentService.HandleInvoicePaidAsync</c> (a
        /// genuine <c>invoice.paid</c> webhook) or <c>SubscriptionQuotaService.SyncExpirationFromGatewayAsync</c>
        /// (a reconciliation job catching up a cycle whose webhook was missed) - both represent the same kind of
        /// event from the business's perspective, a period renewing.
        /// </summary>
        [Display]
        public static readonly PaymentKind Renewal = new(nameof(Renewal), 2);

        /// <summary>An immediate plan/interval switch's prorated charge - <c>PaymentService.HandlePlanChangeInvoicePaidAsync</c>.</summary>
        [Display]
        public static readonly PaymentKind PlanSwitch = new(nameof(PlanSwitch), 3);

        public PaymentKind()
        {
        }

        public PaymentKind(string name, byte value) : base(name, value)
        {
        }
    }
}
