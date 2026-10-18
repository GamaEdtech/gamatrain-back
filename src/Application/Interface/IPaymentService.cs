namespace GamaEdtech.Application.Interface
{
    using System.Diagnostics.CodeAnalysis;

    using GamaEdtech.Common.Data;
    using GamaEdtech.Common.DataAccess.Specification;
    using GamaEdtech.Common.DataAnnotation;
    using GamaEdtech.Data.Dto.Payment;
    using GamaEdtech.Domain.Entity;
    using GamaEdtech.Domain.Enumeration;

    using Microsoft.AspNetCore.Http;

    [Injectable]
    public interface IPaymentService
    {
        Task<ResultData<ListDataSource<PaymentDto>>> GetPaymentsAsync(ListRequestDto<Payment>? requestDto = null);
        Task<ResultData<CreatePaymentResponseDto>> CreatePaymentAsync([NotNull] CreatePaymentRequestDto requestDto);
        /// <summary>
        /// Completes a Pending payment once its gateway confirms it was paid - the one path for the browser's verify
        /// redirect, the gateway's checkout webhook and the pending-payment sweep, so whichever arrives first grants
        /// and the others find it Paid (and report success without granting again).
        /// </summary>
        Task<ResultData<bool>> VerifyPaymentAsync([NotNull] VerifyPaymentRequestDto requestDto);

        /// <summary>
        /// Hourly safety net for the checkout webhook: re-checks recent Pending payments with the gateway and completes
        /// the ones that were paid but never confirmed (the customer left before the redirect and the webhook was
        /// missed). Returns how many were completed.
        /// </summary>
        Task<ResultData<int>> CompletePendingPaymentsAsync();
        Task<ResultData<List<PaymentsSummaryDto>>> GetPaymentsSummaryAsync(ISpecification<Payment>? specification, TimeZoneInfo? timeZone = null);

        /// <summary>
        /// Verifies and processes a native-recurring-billing webhook event for the given gateway (Stripe today).
        /// Always returns Succeeded for a verified-but-irrelevant event or an already-processed redelivery
        /// (idempotent) - only a bad signature or a genuine internal failure is an error. The caller (a thin
        /// controller action) should still 200 the gateway either way; gateways only care about the HTTP status.
        /// </summary>
        Task<ResultData<bool>> HandleRecurringWebhookAsync(PaymentGateway gateway, [NotNull] HttpRequest request);
    }
}
