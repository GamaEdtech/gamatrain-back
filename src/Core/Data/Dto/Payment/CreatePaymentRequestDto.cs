namespace GamaEdtech.Data.Dto.Payment
{
    using GamaEdtech.Domain.Enumeration;

    public sealed class CreatePaymentRequestDto
    {
        public required long UserId { get; set; }
        public required decimal Amount { get; set; }
        public required Currency Currency { get; set; }
        public required PaymentGateway Gateway { get; set; }
        public string? Title { get; set; }
        public string? Description { get; set; }

        /// <summary>Set when this payment purchases a subscription; verify activates it instead of crediting points.</summary>
        public long? UserSubscriptionId { get; set; }

        /// <summary>
        /// Set alongside <see cref="UserSubscriptionId"/> when purchasing a subscription - identifies which
        /// <see cref="Domain.Entity.SubscriptionPlanPrice"/> was resolved, so a <see cref="Gateway"/> that supports
        /// native recurring billing (Stripe today) can look up its <see cref="Domain.Entity.SubscriptionPlanGatewayMapping"/>
        /// and create a real recurring subscription instead of a one-time charge. Ignored by gateways that don't
        /// support recurring billing (GamaTrain) - those always charge once, same as before.
        /// </summary>
        public long? SubscriptionPlanPriceId { get; set; }
    }
}
