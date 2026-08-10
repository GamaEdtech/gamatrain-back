namespace GamaEdtech.Infrastructure.Provider.PaymentGateway
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics.CodeAnalysis;
    using System.Globalization;
    using System.IO;
    using System.Text;

    using GamaEdtech.Common.Core;
    using GamaEdtech.Common.Data;
    using GamaEdtech.Common.HttpProvider;
    using GamaEdtech.Common.Infrastructure;
    using GamaEdtech.Data.Dto.Provider.PaymentGateway;
    using GamaEdtech.Domain.Enumeration;
    using GamaEdtech.Infrastructure.Interface;

    using Microsoft.AspNetCore.Http;
    using Microsoft.Extensions.Configuration;
    using Microsoft.Extensions.Localization;
    using Microsoft.Extensions.Logging;

    using Stripe;
    using Stripe.Checkout;

    using static GamaEdtech.Common.Core.Constants;

    public sealed class StripePaymentGatewayProvider(Lazy<IConfiguration> configuration, Lazy<IHttpProvider> httpProvider, Lazy<IStringLocalizer<StripePaymentGatewayProvider>> localizer
        , Lazy<ILogger<StripePaymentGatewayProvider>> logger)
        : InfrastructureBase<StripePaymentGatewayProvider>(httpProvider, localizer, logger), IPaymentGatewayProvider, IRecurringPaymentGatewayProvider
    {
        public PaymentGateway ProviderType => PaymentGateway.Stripe;

        private RequestOptions RequestOptions => new()
        {
            ApiKey = configuration.Value.GetValue<string>("PaymentGateway:Stripe:ApiKey"),
            IdempotencyKey = Guid.NewGuid().ToString("N"),
        };

        public async Task<ResultData<CreateResponseDto>> CreateAsync([NotNull] CreateRequestDto requestDto)
        {
            try
            {
                if (requestDto.Currency != Currency.USD)
                {
                    return new(OperationResult.NotValid) { Errors = [new() { Message = Localizer.Value["NotSupportedCurrency"], }] };
                }

                var sessionOptions = new SessionCreateOptions
                {
                    Mode = "payment",
                    UiMode = "hosted_page",
                    SuccessUrl = requestDto.CallbackUrl + "?transactionId={CHECKOUT_SESSION_ID}",
                    CustomerEmail = requestDto.Email,
                    LineItems =
                    [
                        new()
                        {
                            Quantity = 1,
                            PriceData = new()
                            {
                                Currency = Currency.USD.Name,
                                UnitAmount = (long) requestDto.Amount * 100,   //to cent
                                ProductData = new()
                                {
                                    Name = requestDto.Title,
                                    Description = requestDto.Description,
                                }
                            },
                        }
                    ],
                    ClientReferenceId = requestDto.PaymentId.ToString(),
                };

                var session = await new SessionService().CreateAsync(sessionOptions, RequestOptions);

                return new(OperationResult.Succeeded)
                {
                    Data = new()
                    {
                        Url = session.Url,
                        TransactionId = session.Id,
                    },
                };
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return new(OperationResult.Failed) { Errors = [new() { Message = exc.Message, }] };
            }
        }

        // Unchanged for Mode = "subscription" sessions too: Stripe sets PaymentStatus = "paid" once the first
        // invoice is paid synchronously during checkout completion for a real (non-trial, non-$0) recurring
        // price - the only case this phase supports (trials are explicitly out of scope, see the
        // "Trial periods" backlog item). No subscription-mode-specific check needed here.
        public async Task<ResultData<VerifyResponseDto>> VerifyAsync([NotNull] VerifyRequestDto requestDto)
        {
            try
            {
                var session = await new SessionService().GetAsync(requestDto.TransactionId, requestOptions: RequestOptions);

                var paymentCompleted = session is not null && session.PaymentStatus.Equals("paid", StringComparison.OrdinalIgnoreCase);
                if (!paymentCompleted)
                {
                    return new(OperationResult.Failed) { Errors = [new() { Message = Localizer.Value["PaymentHasBeenFailed"], }] };
                }

                VerifyResponseDto data = new()
                {
                    Mint = configuration.Value.GetValue<string>("PaymentGateway:UsdcMint"),
                };
                return new(OperationResult.Succeeded)
                {
                    Data = data,
                };
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return new(OperationResult.Failed) { Errors = [new() { Message = exc.Message, }] };
            }
        }

        /// <summary>
        /// Creates a real Stripe Subscription (via Checkout in <c>Mode = "subscription"</c>) rather than a
        /// one-time charge - <paramref name="requestDto"/>.ExternalPriceId is already the resolved
        /// <see cref="Domain.Entity.SubscriptionPlanGatewayMapping.ExternalPlanId"/>; this method never looks up
        /// pricing/mapping itself. <see cref="Stripe.Checkout.SessionSubscriptionDataOptions.Metadata"/> carries
        /// <c>UserSubscriptionId</c> through to the created Subscription, and from there to every Invoice/event
        /// under it - the only way the webhook handler resolves which local subscription an event is about,
        /// with no new DB column needed.
        /// </summary>
        public async Task<ResultData<CreateResponseDto>> CreateSubscriptionCheckoutAsync([NotNull] CreateSubscriptionCheckoutRequestDto requestDto)
        {
            try
            {
                var sessionOptions = new SessionCreateOptions
                {
                    Mode = "subscription",
                    UiMode = "hosted_page",
                    SuccessUrl = requestDto.CallbackUrl + "?transactionId={CHECKOUT_SESSION_ID}",
                    CustomerEmail = requestDto.Email,
                    LineItems = [new() { Quantity = 1, Price = requestDto.ExternalPriceId, }],
                    ClientReferenceId = requestDto.PaymentId.ToString(),
                    SubscriptionData = new SessionSubscriptionDataOptions
                    {
                        Metadata = new Dictionary<string, string> { ["userSubscriptionId"] = requestDto.UserSubscriptionId.ToString(CultureInfo.InvariantCulture), },
                    },
                };

                var session = await new SessionService().CreateAsync(sessionOptions, RequestOptions);

                return new(OperationResult.Succeeded)
                {
                    Data = new()
                    {
                        Url = session.Url,
                        TransactionId = session.Id,
                    },
                };
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return new(OperationResult.Failed) { Errors = [new() { Message = exc.Message, }] };
            }
        }

        /// <summary>
        /// Reads the raw body/signature header itself, mirroring <c>ResendEmailProvider.ProccessInboundEmailAsync</c>'s
        /// buffering approach - never touches the database, <c>PaymentService</c> does all persistence based on
        /// the returned <see cref="RecurringWebhookEventDto"/>.
        /// </summary>
        public async Task<ResultData<RecurringWebhookEventDto>> ParseWebhookEventAsync([NotNull] HttpRequest request)
        {
            try
            {
                if (!request.Body.CanSeek)
                {
                    request.EnableBuffering();
                }
                request.Body.Position = 0;
                using var reader = new StreamReader(request.Body, Encoding.UTF8);
                var payload = await reader.ReadToEndAsync();
                request.Body.Position = 0;

                var signatureHeader = request.Headers["Stripe-Signature"].ToString();
                var secret = configuration.Value.GetValue<string>("PaymentGateway:Stripe:WebhookSecret");

                Event stripeEvent;
                try
                {
                    stripeEvent = EventUtility.ConstructEvent(payload, signatureHeader, secret);
                }
                catch (StripeException)
                {
                    return new(OperationResult.NotValid) { Errors = [new() { Message = Localizer.Value["InvalidSignature"], }] };
                }

                // No "Events.InvoicePaid"-style constants class in this SDK version - Event.Type is a plain
                // string compared against Stripe's own literal event-type names. Stripe copies the
                // Subscription's own metadata onto every invoice under it (Invoice.Parent.SubscriptionDetails.
                // Metadata) - no separate fetch of the Subscription object needed just to read
                // UserSubscriptionId back out.
                RecurringWebhookEventDto data;
                if (stripeEvent.Type == "invoice.paid" && stripeEvent.Data.Object is Invoice { Parent.SubscriptionDetails: not null } invoice)
                {
                    var hasUserSubscriptionId = invoice.Parent.SubscriptionDetails.Metadata.TryGetValue("userSubscriptionId", out var invoiceUserSubscriptionId);
                    data = new()
                    {
                        EventType = RecurringWebhookEventType.InvoicePaid,
                        UserSubscriptionId = hasUserSubscriptionId ? invoiceUserSubscriptionId.ValueOf<long?>() : null,
                        ExternalTransactionId = invoice.Id,
                    };
                }
                else if (stripeEvent.Type == "customer.subscription.deleted" && stripeEvent.Data.Object is Subscription endedSubscription)
                {
                    var hasUserSubscriptionId = endedSubscription.Metadata.TryGetValue("userSubscriptionId", out var endedUserSubscriptionId);
                    data = new() { EventType = RecurringWebhookEventType.SubscriptionEnded, UserSubscriptionId = hasUserSubscriptionId ? endedUserSubscriptionId.ValueOf<long?>() : null, };
                }
                else
                {
                    data = new() { EventType = RecurringWebhookEventType.Ignored, };
                }

                return new(OperationResult.Succeeded) { Data = data };
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return new(OperationResult.Failed) { Errors = [new() { Message = exc.Message, }] };
            }
        }
    }
}
