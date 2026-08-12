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

        /// <summary>
        /// Requests cancel-at-period-end on the gateway's own recurring subscription - the gateway keeps tracking
        /// the exact end date and still fires its own subscription-ended event then, which the existing webhook
        /// handler already processes unchanged. Never touches local state itself.
        /// </summary>
        Task<ResultData<bool>> CancelSubscriptionAsync([NotNull] string externalSubscriptionId);

        /// <summary>Reverses a pending cancel-at-period-end - the subscription goes back to renewing normally. Never touches local state itself.</summary>
        Task<ResultData<bool>> ResumeSubscriptionAsync([NotNull] string externalSubscriptionId);

        /// <summary>
        /// Immediately cancels the gateway's own recurring subscription - unlike <see cref="CancelSubscriptionAsync"/>
        /// (which waits for the current period to end), this stops billing right away. Admin-initiated revoke for a
        /// support case only - the self-service <c>subscriptions/me/cancel</c> flow always uses the period-end
        /// version instead. Never touches local state itself.
        /// </summary>
        Task<ResultData<bool>> TerminateSubscriptionAsync([NotNull] string externalSubscriptionId);

        /// <summary>
        /// Swaps the gateway's own recurring subscription onto a different price. <paramref name="immediate"/> =
        /// <see langword="true"/> (upgrade): bills the prorated difference right away via a plain price/item
        /// update (releasing any attached schedule first, so the update is actually honored).
        /// <paramref name="immediate"/> = <see langword="false"/> (downgrade): the new price must not take effect
        /// or bill until the current period ends - a bare update with no-proration still applies immediately, it
        /// only skips generating a proration invoice line, so this is managed via a 2-phase subscription schedule
        /// instead (the gateway's own documented mechanism for deferring a price change to period end). Never
        /// touches local state itself.
        /// </summary>
        Task<ResultData<bool>> SwitchSubscriptionPlanAsync([NotNull] string externalSubscriptionId, [NotNull] string newExternalPriceId, bool immediate);
    }
}
