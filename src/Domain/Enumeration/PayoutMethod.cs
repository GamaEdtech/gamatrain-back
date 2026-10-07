namespace GamaEdtech.Domain.Enumeration
{
    using GamaEdtech.Common.Data.Enumeration;
    using GamaEdtech.Common.DataAnnotation;

    /// <summary>
    /// How a CommissionPayout reaches the owner. Manual: an admin sends the money outside the system and marks it paid.
    /// StripeConnect: the owner's Stripe Express account (UserPayoutAccount); approving the payout sends a Stripe Transfer.
    /// Doubles as the provider key for IGenericFactory&lt;IPayoutProvider, PayoutMethod&gt; (StripeConnect only).
    /// </summary>
    public sealed class PayoutMethod : Enumeration<PayoutMethod, byte>
    {
        [Display]
        public static readonly PayoutMethod Manual = new(nameof(Manual), 0);

        [Display]
        public static readonly PayoutMethod StripeConnect = new(nameof(StripeConnect), 1);

        public PayoutMethod()
        {
        }

        public PayoutMethod(string name, byte value) : base(name, value)
        {
        }
    }
}
