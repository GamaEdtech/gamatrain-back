namespace GamaEdtech.Presentation.Api.Areas.Admin.Controllers
{
    using System.Diagnostics.CodeAnalysis;

    using Asp.Versioning;

    using GamaEdtech.Application.Interface;
    using GamaEdtech.Common.Core;
    using GamaEdtech.Common.Data;
    using GamaEdtech.Common.Data.Enumeration;
    using GamaEdtech.Common.Identity;
    using GamaEdtech.Data.Dto.Campaign;
    using GamaEdtech.Domain.Enumeration;
    using GamaEdtech.Presentation.ViewModel.Campaign;

    using Microsoft.AspNetCore.Mvc;

    /// <summary>
    /// Sale campaigns: create, edit, enable (one click, with the caller's authenticator code), disable, delete. Admin only,
    /// never delegable: enabling one gives money away. See docs/business/campaigns.md.
    /// </summary>
    [Common.DataAnnotation.Area(nameof(Admin), "Admin")]
    [Route("api/v{version:apiVersion}/[area]/[controller]")]
    [ApiVersion("1.0")]
    [Permission(Roles = [nameof(Role.Admin)])]
    public class CampaignsController(Lazy<ILogger<CampaignsController>> logger, Lazy<ICampaignService> campaignService)
        : ApiControllerBase<CampaignsController>(logger)
    {
        /// <summary>Campaigns, latest start first: <c>page</c>, <c>pageSize</c> (max 100).</summary>
        [HttpGet(""), Produces<ApiResponse<CampaignListViewModel>>()]
        public async Task<IActionResult<CampaignListViewModel>> GetCampaigns([FromQuery] int page = 1, [FromQuery] int pageSize = 20)
        {
            try
            {
                var result = await campaignService.Value.GetCampaignsAsync(page, pageSize);
                return Ok<CampaignListViewModel>(new(result.Errors)
                {
                    Data = result.Data is null ? null : new() { Total = result.Data.Total, Items = result.Data.Items.Select(Map) },
                });
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return Ok<CampaignListViewModel>(new(new Error { Message = exc.Message }));
            }
        }

        [HttpGet("{id:long}"), Produces<ApiResponse<CampaignViewModel>>()]
        public async Task<IActionResult<CampaignViewModel>> GetCampaign([FromRoute] long id)
        {
            try
            {
                var result = await campaignService.Value.GetCampaignAsync(id);
                return Ok<CampaignViewModel>(new(result.Errors) { Data = result.Data is null ? null : Map(result.Data) });
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return Ok<CampaignViewModel>(new(new Error { Message = exc.Message }));
            }
        }

        /// <summary>Creates a campaign, disabled. Answers its id.</summary>
        [HttpPost(""), Produces<ApiResponse<long>>()]
        public async Task<IActionResult<long>> CreateCampaign([NotNull, FromBody] ManageCampaignRequestViewModel request) =>
            await ManageAsync(null, request);

        /// <summary>Edits a campaign. On an enabled one, changing the percent, dates or repeat needs <c>twoFactorCode</c>.</summary>
        [HttpPut("{id:long}"), Produces<ApiResponse<long>>()]
        public async Task<IActionResult<long>> UpdateCampaign([FromRoute] long id, [NotNull, FromBody] ManageCampaignRequestViewModel request) =>
            await ManageAsync(id, request);

        /// <summary>The one-click switch. Enabling needs <c>twoFactorCode</c> and no overlap with another enabled campaign;
        /// disabling (the emergency stop) needs neither.</summary>
        [HttpPut("{id:long}/enabled"), Produces<ApiResponse<bool>>()]
        public async Task<IActionResult<bool>> SetEnabled([FromRoute] long id, [NotNull, FromBody] SetCampaignEnabledRequestViewModel request)
        {
            try
            {
                var result = await campaignService.Value.SetEnabledAsync(id, request.Enabled, User.UserId(), request.TwoFactorCode);
                return Ok<bool>(new(result.Errors) { Data = result.Data });
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return Ok<bool>(new(new Error { Message = exc.Message }));
            }
        }

        /// <summary>Deletes a campaign no payment used.</summary>
        [HttpDelete("{id:long}"), Produces<ApiResponse<bool>>()]
        public async Task<IActionResult<bool>> DeleteCampaign([FromRoute] long id)
        {
            try
            {
                var result = await campaignService.Value.DeleteCampaignAsync(id);
                return Ok<bool>(new(result.Errors) { Data = result.Data });
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return Ok<bool>(new(new Error { Message = exc.Message }));
            }
        }

        private async Task<IActionResult<long>> ManageAsync(long? id, ManageCampaignRequestViewModel request)
        {
            try
            {
                CampaignTheme? theme = null;
                if (!string.IsNullOrWhiteSpace(request.Theme) && !request.Theme.TryGetFromNameOrValue<CampaignTheme, byte>(out theme))
                {
                    return Ok<long>(new(new Error { Message = "Unknown theme.", Reference = "theme" }));
                }

                var result = await campaignService.Value.ManageCampaignAsync(new ManageCampaignRequestDto
                {
                    Id = id,
                    Title = request.Title,
                    Headline = request.Headline,
                    Theme = theme,
                    DiscountPercent = request.DiscountPercent,
                    StartsAt = request.StartsAt,
                    EndsAt = request.EndsAt,
                    RepeatsYearly = request.RepeatsYearly,
                    UserId = User.UserId(),
                    TwoFactorCode = request.TwoFactorCode,
                });
                return Ok<long>(new(result.Errors) { Data = result.Data });
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return Ok<long>(new(new Error { Message = exc.Message }));
            }
        }

        private static CampaignViewModel Map(CampaignDto t) => new()
        {
            Id = t.Id,
            Title = t.Title,
            Headline = t.Headline,
            Theme = t.Theme,
            DiscountPercent = t.DiscountPercent,
            StartsAt = CampaignDate.Iso(t.StartsAt),
            EndsAt = CampaignDate.Iso(t.EndsAt),
            RepeatsYearly = t.RepeatsYearly,
            IsEnabled = t.IsEnabled,
            CurrentStartsAt = CampaignDate.Iso(t.CurrentStartsAt),
            CurrentEndsAt = CampaignDate.Iso(t.CurrentEndsAt),
            IsLive = t.IsLive,
            PaidPaymentCount = t.PaidPaymentCount,
            CreationDate = CampaignDate.Iso(t.CreationDate),
            LastModifyDate = CampaignDate.Iso(t.LastModifyDate),
        };
    }
}
