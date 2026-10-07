namespace GamaEdtech.Data.Dto.Content
{
    using GamaEdtech.Domain.Enumeration;

    public sealed class RequestCommissionPayoutRequestDto
    {
        public long UserId { get; set; }

        /// <summary>Null means the whole available balance.</summary>
        public decimal? AmountUsd { get; set; }

        /// <summary>StripeConnect (default) needs a Stripe account with payouts enabled; Manual needs Destination.</summary>
        public PayoutMethod Method { get; set; } = PayoutMethod.StripeConnect;

        /// <summary>Manual only (ignored for StripeConnect).</summary>
        public string? Destination { get; set; }
    }
}
