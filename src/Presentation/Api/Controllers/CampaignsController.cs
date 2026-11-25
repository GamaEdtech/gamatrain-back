namespace GamaEdtech.Presentation.Api.Controllers
{
    using Asp.Versioning;

    using GamaEdtech.Application.Interface;
    using GamaEdtech.Common.Core;
    using GamaEdtech.Common.Data;
    using GamaEdtech.Common.Identity;
    using GamaEdtech.Presentation.ViewModel.Campaign;

    using Microsoft.AspNetCore.Authorization;
    using Microsoft.AspNetCore.Mvc;

    /// <summary>
    /// The sale campaign the site shows (home hero, plans dialog), for anyone, signed in or not. See
    /// docs/business/campaigns.md.
    /// </summary>
    [Route("api/v{version:apiVersion}/[controller]")]
    [ApiVersion("1.0")]
    [Permission(policy: null)]
    [AllowAnonymous]
    public class CampaignsController(Lazy<ILogger<CampaignsController>> logger, Lazy<ICampaignService> campaignService)
        : ApiControllerBase<CampaignsController>(logger)
    {
        /// <summary>
        /// The live campaign, else the next enabled one starting within a week (<c>isLive</c> false), else null. Theme,
        /// title, percent and the current run's dates, never prices. Cached for a minute: a page switches the theme on and
        /// off at <c>startsAt</c>/<c>endsAt</c> itself, and the checkout decides the discount on its own.
        /// </summary>
        [HttpGet("current"), Produces<ApiResponse<PublicCampaignViewModel>>()]
        [ResponseCache(Location = ResponseCacheLocation.Any, Duration = 60)]
        public async Task<IActionResult<PublicCampaignViewModel>> GetCurrent()
        {
            try
            {
                var result = await campaignService.Value.GetPublicCampaignAsync();
                return Ok<PublicCampaignViewModel>(new(result.Errors)
                {
                    Data = result.Data is not { } campaign ? null : new()
                    {
                        Id = campaign.Id,
                        Title = campaign.Title,
                        Headline = campaign.Headline,
                        Theme = campaign.Theme,
                        DiscountPercent = campaign.DiscountPercent,
                        StartsAt = CampaignDate.Iso(campaign.StartsAt),
                        EndsAt = CampaignDate.Iso(campaign.EndsAt),
                        IsLive = campaign.IsLive,
                    },
                });
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return Ok<PublicCampaignViewModel>(new(new Error { Message = exc.Message }));
            }
        }
    }
}
