namespace GamaEdtech.Infrastructure.Provider.PaymentGateway
{
    using System;
    using System.Diagnostics.CodeAnalysis;

    using GamaEdtech.Common.Core;
    using GamaEdtech.Common.Data;
    using GamaEdtech.Common.HttpProvider;
    using GamaEdtech.Common.Infrastructure;
    using GamaEdtech.Data.Dto.Provider.PaymentGateway;
    using GamaEdtech.Domain.Enumeration;
    using GamaEdtech.Infrastructure.Interface;

    using Microsoft.Extensions.Configuration;
    using Microsoft.Extensions.Localization;
    using Microsoft.Extensions.Logging;

    using Stripe;
    using Stripe.Checkout;

    using static GamaEdtech.Common.Core.Constants;

    public sealed class StripePaymentGatewayProvider(Lazy<IConfiguration> configuration, Lazy<IHttpProvider> httpProvider, Lazy<IStringLocalizer<StripePaymentGatewayProvider>> localizer
        , Lazy<ILogger<StripePaymentGatewayProvider>> logger)
        : InfrastructureBase<StripePaymentGatewayProvider>(httpProvider, localizer, logger), IPaymentGatewayProvider
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
    }
}
