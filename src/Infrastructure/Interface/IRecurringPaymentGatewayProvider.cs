namespace GamaEdtech.Infrastructure.Interface
{
    using System.Diagnostics.CodeAnalysis;

    using GamaEdtech.Common.Data;
    using GamaEdtech.Common.DataAnnotation;
    using GamaEdtech.Common.Service.Factory;
    using GamaEdtech.Data.Dto.Provider.PaymentGateway;
    using GamaEdtech.Domain.Enumeration;

    using Microsoft.AspNetCore.Http;

    /// <summary>
    /// Optional capability alongside <see cref="IPaymentGatewayProvider"/> - only implemented by gateways that
    /// support native recurring billing (Stripe today; GamaTrain's crypto wallet has no saved-payment-method/
    /// auto-charge concept and never will implement this). Resolved the same way, via
    /// <c>IGenericFactory&lt;IRecurringPaymentGatewayProvider, PaymentGateway&gt;.GetProvider(gateway)</c>, which
    /// already returns <see langword="null"/> gracefully for a gateway that doesn't register one - callers branch
    /// on that instead of a capability flag.
    /// </summary>
    [Injectable]
    public interface IRecurringPaymentGatewayProvider : IProvider<PaymentGateway>
    {
        /// <summary>Creates the gateway's own recurring subscription/checkout - never resolves pricing/mapping itself, the caller hands it an already-resolved external price id.</summary>
        Task<ResultData<CreateResponseDto>> CreateSubscriptionCheckoutAsync([NotNull] CreateSubscriptionCheckoutRequestDto requestDto);

        /// <summary>
        /// Reads the raw body/signature header itself (mirroring <c>IEmailProvider.ProccessInboundEmailAsync</c>'s
        /// inbound-webhook handling) and verifies+parses into a typed event - no DB access; the caller
        /// (<c>PaymentService</c>) does all persistence based on the result.
        /// </summary>
        Task<ResultData<RecurringWebhookEventDto>> ParseWebhookEventAsync([NotNull] HttpRequest request);
    }
}
