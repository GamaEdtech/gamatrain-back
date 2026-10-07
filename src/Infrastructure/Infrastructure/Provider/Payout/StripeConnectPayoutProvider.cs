namespace GamaEdtech.Infrastructure.Provider.Payout
{
    using System;
    using System.Diagnostics.CodeAnalysis;
    using System.Globalization;

    using GamaEdtech.Common.Core;
    using GamaEdtech.Common.Data;
    using GamaEdtech.Common.HttpProvider;
    using GamaEdtech.Common.Infrastructure;
    using GamaEdtech.Data.Dto.Provider.Payout;
    using GamaEdtech.Domain.Enumeration;
    using GamaEdtech.Infrastructure.Interface;

    using Microsoft.Extensions.Configuration;
    using Microsoft.Extensions.Localization;
    using Microsoft.Extensions.Logging;

    using Stripe;

    using static GamaEdtech.Common.Core.Constants;

    /// <summary>
    /// Commission payouts through Stripe Connect Express accounts (2026-10-07). Uses the same platform key as
    /// StripePaymentGatewayProvider. Stripe hosts onboarding (identity + bank details) and pays the connected account out
    /// to the owner's bank on its own schedule; we only create the account, link to onboarding, read its status, and send
    /// Transfers from the platform's USD balance.
    /// Unlike StripePaymentGatewayProvider.RequestOptions (a fresh random idempotency key per call - see CLAUDE.md), the
    /// transfer uses the caller's fixed key per payout, so a retried approval can't pay twice. Stripe keeps idempotency
    /// keys for 24 hours.
    /// </summary>
    public sealed class StripeConnectPayoutProvider(Lazy<IConfiguration> configuration, Lazy<IHttpProvider> httpProvider
        , Lazy<IStringLocalizer<StripeConnectPayoutProvider>> localizer, Lazy<ILogger<StripeConnectPayoutProvider>> logger)
        : InfrastructureBase<StripeConnectPayoutProvider>(httpProvider, localizer, logger), IPayoutProvider
    {
        public PayoutMethod ProviderType => PayoutMethod.StripeConnect;

        private string? ApiKey => configuration.Value.GetValue<string>("PaymentGateway:Stripe:ApiKey");

        public async Task<ResultData<string>> CreateAccountAsync([NotNull] CreatePayoutAccountRequestDto requestDto)
        {
            try
            {
                var platform = await new AccountService().GetSelfAsync(new RequestOptions { ApiKey = ApiKey });
                var crossBorder = !string.Equals(platform.Country, requestDto.Country, StringComparison.OrdinalIgnoreCase);

                var account = await new AccountService().CreateAsync(new AccountCreateOptions
                {
                    Type = "express",
                    Country = requestDto.Country,
                    Email = requestDto.Email,
                    BusinessType = "individual",
                    Capabilities = new()
                    {
                        Transfers = new() { Requested = true },
                    },
                    // An account in another country than the platform can only receive transfers under the "recipient"
                    // service agreement (Stripe cross-border payouts); a same-country account uses the default.
                    TosAcceptance = crossBorder ? new() { ServiceAgreement = "recipient" } : null,
                    Metadata = new() { ["userId"] = requestDto.UserId.ToString(CultureInfo.InvariantCulture) },
                }, new RequestOptions { ApiKey = ApiKey, IdempotencyKey = $"payout-account-{requestDto.UserId}-{requestDto.Country}" });

                return new(OperationResult.Succeeded) { Data = account.Id };
            }
            catch (StripeException exc)
            {
                Logger.Value.LogException(exc);
                return new(OperationResult.Failed) { Errors = [new() { Message = exc.StripeError?.Message ?? exc.Message }] };
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return new(OperationResult.Failed) { Errors = [new() { Message = exc.Message }] };
            }
        }

        public async Task<ResultData<string>> CreateOnboardingLinkAsync(string externalAccountId, [NotNull] Uri returnUrl, [NotNull] Uri refreshUrl)
        {
            try
            {
                var link = await new AccountLinkService().CreateAsync(new AccountLinkCreateOptions
                {
                    Account = externalAccountId,
                    Type = "account_onboarding",
                    ReturnUrl = returnUrl.AbsoluteUri,
                    RefreshUrl = refreshUrl.AbsoluteUri,
                }, new RequestOptions { ApiKey = ApiKey });

                return new(OperationResult.Succeeded) { Data = link.Url };
            }
            catch (StripeException exc)
            {
                Logger.Value.LogException(exc);
                return new(OperationResult.Failed) { Errors = [new() { Message = exc.StripeError?.Message ?? exc.Message }] };
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return new(OperationResult.Failed) { Errors = [new() { Message = exc.Message }] };
            }
        }

        public async Task<ResultData<PayoutAccountStatusDto>> GetAccountStatusAsync(string externalAccountId)
        {
            try
            {
                var account = await new AccountService().GetAsync(externalAccountId, null, new RequestOptions { ApiKey = ApiKey });

                return new(OperationResult.Succeeded)
                {
                    Data = new()
                    {
                        DetailsSubmitted = account.DetailsSubmitted,
                        PayoutsEnabled = account.PayoutsEnabled && account.Capabilities?.Transfers == "active",
                    },
                };
            }
            catch (StripeException exc)
            {
                Logger.Value.LogException(exc);
                return new(OperationResult.Failed) { Errors = [new() { Message = exc.StripeError?.Message ?? exc.Message }] };
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return new(OperationResult.Failed) { Errors = [new() { Message = exc.Message }] };
            }
        }

        public async Task<ResultData<string>> TransferAsync([NotNull] PayoutTransferRequestDto requestDto)
        {
            try
            {
                var payoutId = requestDto.PayoutId.ToString(CultureInfo.InvariantCulture);
                var transferGroup = $"commission-payout-{payoutId}";

                // Already sent? (A retry after a crash between the transfer and recording it, past the 24-hour idempotency
                // window.) Each payout's transfer carries its own transfer group, so one lookup answers it.
                var existing = await new TransferService().ListAsync(new TransferListOptions
                {
                    TransferGroup = transferGroup,
                    Destination = requestDto.ExternalAccountId,
                    Limit = 1,
                }, new RequestOptions { ApiKey = ApiKey });
                if (existing.Data.Count > 0)
                {
                    return new(OperationResult.Succeeded) { Data = existing.Data[0].Id };
                }

                var transfer = await new TransferService().CreateAsync(new TransferCreateOptions
                {
                    Amount = (long)decimal.Round(requestDto.AmountUsd * 100m, 0, MidpointRounding.AwayFromZero),   // to cents
                    Currency = "usd",
                    Destination = requestDto.ExternalAccountId,
                    Description = $"Gamatrain commission payout #{payoutId}",
                    TransferGroup = transferGroup,
                    Metadata = new() { ["commissionPayoutId"] = payoutId },
                }, new RequestOptions { ApiKey = ApiKey, IdempotencyKey = requestDto.IdempotencyKey });

                return new(OperationResult.Succeeded) { Data = transfer.Id };
            }
            catch (StripeException exc)
            {
                Logger.Value.LogException(exc);
                return new(OperationResult.Failed) { Errors = [new() { Message = exc.StripeError?.Message ?? exc.Message }] };
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return new(OperationResult.Failed) { Errors = [new() { Message = exc.Message }] };
            }
        }
    }
}
