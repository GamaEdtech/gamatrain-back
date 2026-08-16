namespace GamaEdtech.Data.Dto.Subscription
{
    using GamaEdtech.Domain.Enumeration;

    public sealed class PurchaseSubscriptionRequestDto
    {
        public required long UserId { get; set; }
        public required long SubscriptionPlanId { get; set; }
        public required BillingInterval BillingInterval { get; set; }
        public required PaymentGateway Gateway { get; set; }

        /// <summary>
        /// Only meaningful when the caller already has an Active recurring subscription - this call is then
        /// delegated to <c>SubscriptionService.SwitchSubscriptionPlanAsync</c> internally rather than starting a
        /// second, independent purchase (see that method's own <c>Confirm</c> doc comment for the full
        /// preview-then-confirm reasoning). Ignored when the caller has no existing subscription - a fresh
        /// purchase always goes through Stripe Checkout, which shows the price on its own hosted page before
        /// charging, so there's nothing local to confirm first. Added 2026-08-16.
        /// </summary>
        public bool Confirm { get; set; }
    }
}
