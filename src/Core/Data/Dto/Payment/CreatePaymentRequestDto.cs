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
    }
}
