namespace GamaEdtech.Infrastructure.Interface
{
    using System.Diagnostics.CodeAnalysis;

    using GamaEdtech.Common.Data;
    using GamaEdtech.Common.DataAnnotation;
    using GamaEdtech.Common.Service.Factory;
    using GamaEdtech.Data.Dto.Provider.Payout;
    using GamaEdtech.Domain.Enumeration;

    /// <summary>Sends commission payouts to a user's account at an external provider (Stripe Connect today).</summary>
    [Injectable]
    public interface IPayoutProvider : IProvider<PayoutMethod>
    {
        /// <summary>Creates the user's account at the provider; returns its id.</summary>
        Task<ResultData<string>> CreateAccountAsync([NotNull] CreatePayoutAccountRequestDto requestDto);

        /// <summary>A short-lived link to the provider's hosted onboarding form for the account.</summary>
        Task<ResultData<string>> CreateOnboardingLinkAsync(string externalAccountId, [NotNull] Uri returnUrl, [NotNull] Uri refreshUrl);

        Task<ResultData<PayoutAccountStatusDto>> GetAccountStatusAsync(string externalAccountId);

        /// <summary>Moves the amount from the platform's balance to the account; returns the provider's transfer id.</summary>
        Task<ResultData<string>> TransferAsync([NotNull] PayoutTransferRequestDto requestDto);
    }
}
