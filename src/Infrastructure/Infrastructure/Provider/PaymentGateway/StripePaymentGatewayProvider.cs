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
                    // Only set for a Mode = "subscription" session (session.SubscriptionId) - null for every
                    // ordinary Mode = "payment" top-up, which is the only kind this same method verified before
                    // native recurring billing existed. Null-forgiving: paymentCompleted being true already
                    // proved session is not null above (the same guarantee PaymentStatus below already relies on).
                    ExternalSubscriptionId = session!.SubscriptionId,
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
                //
                // BillingReason == "subscription_cycle" is what makes this a genuine renewal, as opposed to
                // "subscription_create" - the very first invoice of a brand-new subscription. That first
                // invoice.paid is deliberately treated as Ignored here: the client-driven verify ->
                // ActivateSubscriptionAsync path (unchanged, same as the one-time-payment flow before this
                // feature existed) already fully handles the first period - it records its own Payment row
                // (keyed by the Checkout Session id) and sets the initial ExpirationDate. Also reacting to the
                // first invoice.paid here would both double-record that same charge as a second Payment row
                // (keyed by the invoice id instead) and double-extend ExpirationDate on top of what activation
                // just set - found live while tracing through a 3-month test purchase end to end.
                RecurringWebhookEventDto data;
                if (stripeEvent.Type == "invoice.paid" && stripeEvent.Data.Object is Invoice { Parent.SubscriptionDetails: not null, BillingReason: "subscription_cycle" } invoice)
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
                // Unlike InvoicePaid above, not restricted to BillingReason == "subscription_cycle" - a failed
                // first-period invoice ("subscription_create") is just as worth surfacing, and there's no
                // double-recording risk here to guard against (PaymentFailed never writes a Payment row or
                // touches ExpirationDate, only a visibility timestamp - see HandlePaymentFailedAsync).
                else if (stripeEvent.Type == "invoice.payment_failed" && stripeEvent.Data.Object is Invoice { Parent.SubscriptionDetails: not null } failedInvoice)
                {
                    var hasUserSubscriptionId = failedInvoice.Parent.SubscriptionDetails.Metadata.TryGetValue("userSubscriptionId", out var failedUserSubscriptionId);
                    data = new() { EventType = RecurringWebhookEventType.PaymentFailed, UserSubscriptionId = hasUserSubscriptionId ? failedUserSubscriptionId.ValueOf<long?>() : null, };
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

        public async Task<ResultData<bool>> CancelSubscriptionAsync([NotNull] string externalSubscriptionId)
        {
            try
            {
                // A pending downgrade schedule (if any) implies stopping the subscription at a *later*
                // boundary than the schedule expects, so release it back to a plain subscription first -
                // cancellation wins over any pending plan switch.
                await ReleaseScheduleIfAttachedAsync(externalSubscriptionId);

                _ = await new Stripe.SubscriptionService().UpdateAsync(externalSubscriptionId, new SubscriptionUpdateOptions
                {
                    CancelAtPeriodEnd = true,
                }, RequestOptions);

                return new(OperationResult.Succeeded) { Data = true };
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return new(OperationResult.Failed) { Data = false, Errors = [new() { Message = exc.Message, }] };
            }
        }

        public async Task<ResultData<bool>> ResumeSubscriptionAsync([NotNull] string externalSubscriptionId)
        {
            try
            {
                // Deliberately does NOT release an attached schedule (unlike CancelSubscriptionAsync) - by the
                // time a cancellation exists to resume, CancelSubscriptionAsync already released any schedule
                // that was there. A schedule attached here instead means a downgrade is separately pending
                // (unrelated to cancellation) and must be left alone - a schedule-attached subscription still
                // honors a plain CancelAtPeriodEnd update fine, this is only the earlier concern about a price
                // *item* update not being honored while scheduled (see SwitchSubscriptionPlanAsync).
                _ = await new Stripe.SubscriptionService().UpdateAsync(externalSubscriptionId, new SubscriptionUpdateOptions
                {
                    CancelAtPeriodEnd = false,
                }, RequestOptions);

                return new(OperationResult.Succeeded) { Data = true };
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return new(OperationResult.Failed) { Data = false, Errors = [new() { Message = exc.Message, }] };
            }
        }

        public async Task<ResultData<bool>> TerminateSubscriptionAsync([NotNull] string externalSubscriptionId)
        {
            try
            {
                var subscription = await new Stripe.SubscriptionService().GetAsync(externalSubscriptionId, requestOptions: RequestOptions);
                if (!string.IsNullOrEmpty(subscription.ScheduleId))
                {
                    // A schedule-attached subscription is cancelled through the schedule itself, not the
                    // plain Subscription API - cancelling the schedule cancels the underlying subscription too.
                    _ = await new SubscriptionScheduleService().CancelAsync(subscription.ScheduleId, null, RequestOptions);
                }
                else
                {
                    _ = await new Stripe.SubscriptionService().CancelAsync(externalSubscriptionId, null, RequestOptions);
                }

                return new(OperationResult.Succeeded) { Data = true };
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return new(OperationResult.Failed) { Data = false, Errors = [new() { Message = exc.Message, }] };
            }
        }

        public async Task<ResultData<bool>> SwitchSubscriptionPlanAsync([NotNull] string externalSubscriptionId, [NotNull] string newExternalPriceId, bool immediate)
        {
            try
            {
                var subscriptionService = new Stripe.SubscriptionService();
                var subscription = await subscriptionService.GetAsync(externalSubscriptionId, requestOptions: RequestOptions);
                var item = subscription.Items.Data[0];

                if (immediate)
                {
                    // A plain item update on a schedule-attached subscription isn't honored the way an
                    // upgrade needs (bill now) - release the schedule first, same as Cancel/Resume above.
                    await ReleaseScheduleIfAttachedAsync(externalSubscriptionId);

                    _ = await subscriptionService.UpdateAsync(externalSubscriptionId, new SubscriptionUpdateOptions
                    {
                        Items = [new SubscriptionItemOptions { Id = item.Id, Price = newExternalPriceId }],
                        // Invoices the prorated difference immediately, rather than folding it into the next
                        // regular invoice - matches "upgrade takes effect now, pay the difference now."
                        ProrationBehavior = "always_invoice",
                    }, RequestOptions);
                }
                else
                {
                    // Deferring a price change to period-end isn't a flag on a plain Subscription update -
                    // ProrationBehavior=None still applies the new price immediately, it only skips the
                    // proration invoice line. Stripe's documented mechanism for this is a 2-phase Subscription
                    // Schedule: phase 0 keeps the current price through the current period end, phase 1 (open-
                    // ended, no EndDate/Duration) starts the new price - Stripe flips the item at that boundary
                    // itself, before generating that period's invoice.
                    var scheduleService = new SubscriptionScheduleService();
                    SubscriptionSchedulePhase currentPhase;
                    string scheduleId;
                    if (string.IsNullOrEmpty(subscription.ScheduleId))
                    {
                        var schedule = await scheduleService.CreateAsync(new SubscriptionScheduleCreateOptions
                        {
                            FromSubscription = externalSubscriptionId,
                        }, RequestOptions);
                        scheduleId = schedule.Id;
                        currentPhase = schedule.Phases[0];
                    }
                    else
                    {
                        // Already schedule-attached (e.g. a prior downgrade request being changed) - reuse its
                        // existing current phase rather than creating a second schedule.
                        scheduleId = subscription.ScheduleId;
                        var schedule = await scheduleService.GetAsync(scheduleId, requestOptions: RequestOptions);
                        currentPhase = schedule.Phases[0];
                    }

                    _ = await scheduleService.UpdateAsync(scheduleId, new SubscriptionScheduleUpdateOptions
                    {
                        EndBehavior = "release",
                        Phases =
                        [
                            new SubscriptionSchedulePhaseOptions
                            {
                                StartDate = currentPhase.StartDate,
                                EndDate = currentPhase.EndDate,
                                Items = [new SubscriptionSchedulePhaseItemOptions { Price = item.Price.Id, Quantity = 1 }],
                            },
                            new SubscriptionSchedulePhaseOptions
                            {
                                // No EndDate/Duration - open-ended, continues indefinitely once reached.
                                Items = [new SubscriptionSchedulePhaseItemOptions { Price = newExternalPriceId, Quantity = 1 }],
                            },
                        ],
                    }, RequestOptions);
                }

                return new(OperationResult.Succeeded) { Data = true };
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return new(OperationResult.Failed) { Data = false, Errors = [new() { Message = exc.Message, }] };
            }
        }

        /// <summary>Releases (detaches) any Subscription Schedule attached to this subscription, if one exists - a safe no-op otherwise. Shared by Cancel/Resume/Switch's immediate path, all of which need a plain (non-scheduled) subscription to act on directly.</summary>
        private async Task ReleaseScheduleIfAttachedAsync(string externalSubscriptionId)
        {
            var subscription = await new Stripe.SubscriptionService().GetAsync(externalSubscriptionId, requestOptions: RequestOptions);
            if (!string.IsNullOrEmpty(subscription.ScheduleId))
            {
                _ = await new SubscriptionScheduleService().ReleaseAsync(subscription.ScheduleId, null, RequestOptions);
            }
        }
    }
}
