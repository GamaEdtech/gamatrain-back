namespace GamaEdtech.Application.Interface
{
    using System.Diagnostics.CodeAnalysis;

    using GamaEdtech.Common.Data;
    using GamaEdtech.Common.DataAnnotation;
    using GamaEdtech.Data.Dto.Campaign;

    /// <summary>
    /// Sale campaigns (Black Friday, Christmas, New Year, any event; 2026-11-24): admins create one and enable it once,
    /// and between its dates a new subscription's first payment is discounted and the site shows its theme. See
    /// docs/business/campaigns.md.
    /// </summary>
    [Injectable]
    public interface ICampaignService
    {
        Task<ResultData<CampaignListDto>> GetCampaignsAsync(int page, int pageSize);

        Task<ResultData<CampaignDto>> GetCampaignAsync(long id);

        /// <summary>Creates (disabled) or edits a campaign. Changing the percent or dates of an enabled one needs the
        /// caller's authenticator code and must not overlap another enabled campaign.</summary>
        Task<ResultData<long>> ManageCampaignAsync([NotNull] ManageCampaignRequestDto requestDto);

        /// <summary>The one-click switch. Enabling needs the caller's authenticator code and no overlap with another
        /// enabled campaign; disabling (the emergency stop) needs neither.</summary>
        Task<ResultData<bool>> SetEnabledAsync(long id, bool enabled, long userId, string? twoFactorCode);

        /// <summary>Deletes a campaign no payment used; a used one can only be disabled.</summary>
        Task<ResultData<bool>> DeleteCampaignAsync(long id);

        /// <summary>The live campaign, else the next enabled one starting within a week (<c>IsLive</c> false); null when neither.</summary>
        Task<ResultData<PublicCampaignDto?>> GetPublicCampaignAsync();

        /// <summary>
        /// The campaign a new subscription bought by <paramref name="userId"/> right now gets: the live one, or null when
        /// none is live or the user has an Active subscription (a plan switch is never discounted).
        /// </summary>
        Task<PublicCampaignDto?> GetCheckoutCampaignAsync(long userId);
    }
}
