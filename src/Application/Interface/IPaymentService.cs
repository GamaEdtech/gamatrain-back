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
        Task<ResultData<bool>> VerifyPaymentAsync([NotNull] VerifyPaymentRequestDto requestDto);
        Task<ResultData<List<PaymentsSummaryDto>>> GetPaymentsSummaryAsync(ISpecification<Payment>? specification);

        /// <summary>
        /// Verifies and processes a native-recurring-billing webhook event for the given gateway (Stripe today).
        /// Always returns Succeeded for a verified-but-irrelevant event or an already-processed redelivery
        /// (idempotent) - only a bad signature or a genuine internal failure is an error. The caller (a thin
        /// controller action) should still 200 the gateway either way; gateways only care about the HTTP status.
        /// </summary>
        Task<ResultData<bool>> HandleRecurringWebhookAsync(PaymentGateway gateway, [NotNull] HttpRequest request);
    }
}
