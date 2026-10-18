namespace GamaEdtech.Test.Infrastructure
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.IO;
    using System.Security.Cryptography;
    using System.Text;
    using System.Threading.Tasks;

    using GamaEdtech.Common.HttpProvider;
    using GamaEdtech.Data.Dto.Provider.PaymentGateway;
    using GamaEdtech.Infrastructure.Provider.PaymentGateway;

    using Microsoft.AspNetCore.Http;
    using Microsoft.Extensions.Configuration;
    using Microsoft.Extensions.Localization;
    using Microsoft.Extensions.Logging.Abstractions;

    using Stripe;

    using Xunit;

    using static GamaEdtech.Common.Core.Constants;

    // Until 2026-10-18 only the browser's verify redirect completed a checkout, so a customer who paid and never came
    // back was charged and got nothing. The webhook now maps Stripe's checkout events onto the payment the checkout was
    // created for (its client_reference_id). Each payload below is signed like Stripe signs it; no network needed.
    public class StripeCheckoutWebhookTests
    {
        private const string Secret = "whsec_test_secret";

        private static StripePaymentGatewayProvider CreateProvider()
        {
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?> { ["PaymentGateway:Stripe:WebhookSecret"] = Secret })
                .Build();

            return new(
                new(() => configuration),
                new Lazy<IHttpProvider>(() => throw new InvalidOperationException("parsing a webhook must not call out")),
                new(() => new KeyLocalizer()),
                new(() => NullLogger<StripePaymentGatewayProvider>.Instance));
        }

        private static HttpRequest SignedRequest(string type, string sessionJson, string? secret = Secret)
        {
            var payload = $$$"""{"id":"evt_test","object":"event","api_version":"{{{StripeConfiguration.ApiVersion}}}","type":"{{{type}}}","data":{"object":{{{sessionJson}}}}}""";
            var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
            var signature = Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret!), Encoding.UTF8.GetBytes($"{timestamp}.{payload}"))).ToLowerInvariant();

            var context = new DefaultHttpContext();
            context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(payload));
            context.Request.Headers["Stripe-Signature"] = $"t={timestamp},v1={signature}";
            return context.Request;
        }

        private static string Session(string paymentStatus, string? clientReferenceId = "1001") =>
            $$"""{"id":"cs_test_1","object":"checkout.session","payment_status":"{{paymentStatus}}","client_reference_id":{{(clientReferenceId is null ? "null" : $"\"{clientReferenceId}\"")}}}""";

        [Theory]
        [InlineData("checkout.session.completed")]
        [InlineData("checkout.session.async_payment_succeeded")]
        public async Task PaidCheckoutCompletesItsPayment(string type)
        {
            var result = await CreateProvider().ParseWebhookEventAsync(SignedRequest(type, Session("paid")));

            Assert.Equal(OperationResult.Succeeded, result.OperationResult);
            Assert.Equal(RecurringWebhookEventType.CheckoutCompleted, result.Data!.EventType);
            Assert.Equal(1001, result.Data.PaymentId);
            Assert.Equal("cs_test_1", result.Data.ExternalTransactionId);
        }

        // A delayed payment method reports "completed" before the money arrives; its async_payment_* event decides.
        [Fact]
        public async Task CompletedButUnpaidCheckoutIsIgnored()
        {
            var result = await CreateProvider().ParseWebhookEventAsync(SignedRequest("checkout.session.completed", Session("unpaid")));

            Assert.Equal(RecurringWebhookEventType.Ignored, result.Data!.EventType);
        }

        [Theory]
        [InlineData("checkout.session.expired")]
        [InlineData("checkout.session.async_payment_failed")]
        public async Task ExpiredOrFailedCheckoutFailsItsPayment(string type)
        {
            var result = await CreateProvider().ParseWebhookEventAsync(SignedRequest(type, Session("unpaid")));

            Assert.Equal(RecurringWebhookEventType.CheckoutFailed, result.Data!.EventType);
            Assert.Equal(1001, result.Data.PaymentId);
            Assert.Equal("cs_test_1", result.Data.ExternalTransactionId);
        }

        // Not one of our checkouts (no or a non-numeric reference): nothing to complete.
        [Theory]
        [InlineData(null)]
        [InlineData("not-a-payment")]
        public async Task CheckoutWithoutPaymentReferenceHasNoPayment(string? clientReferenceId)
        {
            var result = await CreateProvider().ParseWebhookEventAsync(SignedRequest("checkout.session.completed", Session("paid", clientReferenceId)));

            Assert.Equal(RecurringWebhookEventType.CheckoutCompleted, result.Data!.EventType);
            Assert.Null(result.Data.PaymentId);
        }

        [Fact]
        public async Task ForgedSignatureIsRejected()
        {
            var result = await CreateProvider().ParseWebhookEventAsync(SignedRequest("checkout.session.completed", Session("paid"), secret: "whsec_someone_else"));

            Assert.Equal(OperationResult.NotValid, result.OperationResult);
            Assert.Null(result.Data);
        }

        private sealed class KeyLocalizer : IStringLocalizer<StripePaymentGatewayProvider>
        {
            public LocalizedString this[string name] => new(name, name);

            public LocalizedString this[string name, params object[] arguments] => new(name, name);

            public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures) => [];
        }
    }
}
