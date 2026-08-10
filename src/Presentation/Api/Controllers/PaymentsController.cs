namespace GamaEdtech.Presentation.Api.Controllers
{
    using System.Diagnostics.CodeAnalysis;

    using Asp.Versioning;

    using GamaEdtech.Application.Interface;
    using GamaEdtech.Common.Core;
    using GamaEdtech.Common.Data;
    using GamaEdtech.Common.Identity;
    using GamaEdtech.Domain.Enumeration;
    using GamaEdtech.Presentation.ViewModel.Payment;

    using Microsoft.AspNetCore.Authorization;
    using Microsoft.AspNetCore.Mvc;

    [Route("api/v{version:apiVersion}/[controller]")]
    [ApiVersion("1.0")]
    [Permission(policy: null)]
    public class PaymentsController(Lazy<ILogger<PaymentsController>> logger, Lazy<IPaymentService> paymentService)
        : ApiControllerBase<PaymentsController>(logger)
    {
        [HttpPost, Produces(typeof(ApiResponse<CreatePaymentResponseViewModel>))]
        public async Task<IActionResult<CreatePaymentResponseViewModel>> CreatePayment([NotNull] CreatePaymentRequestViewModel request)
        {
            try
            {
                var result = await paymentService.Value.CreatePaymentAsync(new()
                {
                    UserId = User.UserId(),
                    Amount = request.Amount.GetValueOrDefault(),
                    Currency = request.Currency!,
                    Gateway = request.Gateway!,
                    Title = request.Title,
                    Description = request.Description,
                });

                return Ok<CreatePaymentResponseViewModel>(new(result.Errors)
                {
                    Data = result.Data is null
                    ? null
                    : new()
                    {
                        PaymentId = result.Data.PaymentId,
                        Url = result.Data.Url,
                    },
                });
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return Ok<CreatePaymentResponseViewModel>(new(new Error { Message = exc.Message }));
            }
        }

        [HttpPost("{id:long}/verify"), Produces(typeof(ApiResponse<bool>))]
        public async Task<IActionResult<bool>> VerifyPayment([FromRoute] long id, [NotNull] VerifyPaymentRequestViewModel request)
        {
            try
            {
                var result = await paymentService.Value.VerifyPaymentAsync(new()
                {
                    Id = id,
                    TransactionId = request.TransactionId,
                });

                return Ok<bool>(new(result.Errors)
                {
                    Data = result.OperationResult is Constants.OperationResult.Succeeded,
                });
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return Ok<bool>(new(new Error { Message = exc.Message }));
            }
        }

        /// <summary>
        /// Native-recurring-billing webhook receiver, gateway-parameterized in the route so a future gateway
        /// (e.g. PayPal) is purely additive here - no new action needed, just a provider implementing
        /// <c>IRecurringPaymentGatewayProvider</c>. Always returns 200: the gateway only reads the HTTP status,
        /// and returning anything else would make it retry an event that will never succeed differently (a bad
        /// signature or unknown event is a real condition, logged inside the service, not a client-facing error).
        /// Raw body/signature-header reading happens in the provider (mirroring
        /// <c>TicketsController.InboundWebHook</c>/<c>ResendEmailProvider</c>'s inbound-webhook handling) - this
        /// action just forwards the request, no raw request-data access here.
        /// </summary>
        [HttpPost("webhooks/{gateway:PaymentGateway}"), Produces(typeof(ApiResponse<Void>))]
        [AllowAnonymous]
        public async Task<IActionResult<Void>> RecurringWebhook([FromRoute] PaymentGateway gateway)
        {
            try
            {
                _ = await paymentService.Value.HandleRecurringWebhookAsync(gateway, Request);
                return Ok<Void>(new());
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return Ok<Void>(new());
            }
        }
    }
}
