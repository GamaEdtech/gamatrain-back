namespace GamaEdtech.Application.Service
{
    using System.Diagnostics.CodeAnalysis;
    using System.Linq;

    using GamaEdtech.Application.Interface;
    using GamaEdtech.Common.Caching;
    using GamaEdtech.Common.Core;
    using GamaEdtech.Common.Data;
    using GamaEdtech.Common.Data.Enumeration;
    using GamaEdtech.Common.DataAccess.UnitOfWork;
    using GamaEdtech.Common.Service;
    using GamaEdtech.Data.Dto.Campaign;
    using GamaEdtech.Domain.Entity;
    using GamaEdtech.Domain.Enumeration;

    using Microsoft.AspNetCore.Http;
    using Microsoft.EntityFrameworkCore;
    using Microsoft.Extensions.Caching.Distributed;
    using Microsoft.Extensions.Logging;

    using static GamaEdtech.Common.Core.Constants;

    /// <inheritdoc cref="ICampaignService"/>
    public sealed class CampaignService(Lazy<IUnitOfWorkProvider> unitOfWorkProvider, Lazy<IHttpContextAccessor> httpContextAccessor,
        Lazy<ILogger<CampaignService>> logger, Lazy<ICacheProvider> cacheProvider, Lazy<ITwoFactorService> twoFactorService)
        : ServiceBase<CampaignService>(unitOfWorkProvider, httpContextAccessor, logger), ICampaignService
    {
        /// <summary>Enabled campaigns, cached because every plans list and checkout reads them. Cleared on every admin
        /// change; the time window is always worked out against now on read, so a cached copy never keeps a campaign alive
        /// past its end.</summary>
        private const string EnabledCampaignsCacheKey = "Campaigns:Enabled";
        private const int MaxPageSize = 100;
        private static readonly TimeSpan CacheLifetime = TimeSpan.FromMinutes(10);

        public async Task<ResultData<CampaignListDto>> GetCampaignsAsync(int page, int pageSize)
        {
            try
            {
                var uow = UnitOfWorkProvider.Value.CreateUnitOfWork();
                var query = uow.GetRepository<Campaign>().GetManyQueryable(t => true);
                var size = Math.Clamp(pageSize, 1, MaxPageSize);
                var total = await query.CountAsync();
                var items = await Project(uow, query.OrderByDescending(t => t.StartsAt).ThenByDescending(t => t.Id)
                    .Skip((Math.Max(page, 1) - 1) * size).Take(size)).ToListAsync();
                items.ForEach(AddCurrentRun);
                return new(OperationResult.Succeeded) { Data = new() { Total = total, Items = items } };
            }
            catch (Exception exc)
            {
                return Error<CampaignListDto>(exc);
            }
        }

        public async Task<ResultData<CampaignDto>> GetCampaignAsync(long id)
        {
            try
            {
                var uow = UnitOfWorkProvider.Value.CreateUnitOfWork();
                var item = await Project(uow, uow.GetRepository<Campaign>().GetManyQueryable(t => t.Id == id)).FirstOrDefaultAsync();
                if (item is null)
                {
                    return NotFound<CampaignDto>();
                }

                AddCurrentRun(item);
                return new(OperationResult.Succeeded) { Data = item };
            }
            catch (Exception exc)
            {
                return Error<CampaignDto>(exc);
            }
        }

        public async Task<ResultData<long>> ManageCampaignAsync([NotNull] ManageCampaignRequestDto requestDto)
        {
            try
            {
                if (CampaignRules.Validate(requestDto.Title, requestDto.Headline, requestDto.DiscountPercent, requestDto.StartsAt,
                    requestDto.EndsAt, requestDto.RepeatsYearly) is { } invalid)
                {
                    return Invalid<long>(invalid.Reference, invalid.Message);
                }

                var uow = UnitOfWorkProvider.Value.CreateUnitOfWork();
                var repository = uow.GetRepository<Campaign>();
                var now = DateTimeOffset.UtcNow;
                var headline = requestDto.Headline?.Trim();
                Campaign campaign;
                if (requestDto.Id is long id)
                {
                    var found = await repository.GetManyQueryable(t => t.Id == id, tracking: true).FirstOrDefaultAsync();
                    if (found is null)
                    {
                        return NotFound<long>();
                    }

                    campaign = found;

                    // Changing what an enabled campaign gives away is the same decision as enabling it.
                    var termsChanged = campaign.DiscountPercent != requestDto.DiscountPercent || campaign.StartsAt != requestDto.StartsAt
                        || campaign.EndsAt != requestDto.EndsAt || campaign.RepeatsYearly != requestDto.RepeatsYearly;
                    if (campaign.IsEnabled && termsChanged)
                    {
                        var check = await twoFactorService.Value.VerifyCodeAsync(requestDto.UserId, requestDto.TwoFactorCode);
                        if (check.OperationResult is not OperationResult.Succeeded)
                        {
                            return new(check.OperationResult) { Errors = check.Errors };
                        }

                        if (await OverlappingTitleAsync(uow, campaign.Id, (requestDto.StartsAt!.Value, requestDto.EndsAt!.Value, requestDto.RepeatsYearly)) is { } other)
                        {
                            return Invalid<long>("overlap", $"These dates overlap the enabled campaign \"{other}\".");
                        }
                    }

                    campaign.LastModifyDate = now;
                    campaign.LastModifyUserId = requestDto.UserId;
                }
                else
                {
                    campaign = new() { CreationDate = now, CreationUserId = requestDto.UserId, IsEnabled = false };
                    repository.Add(campaign);
                }

                campaign.Title = requestDto.Title!.Trim();
                campaign.Headline = string.IsNullOrEmpty(headline) ? null : headline;
                campaign.Theme = requestDto.Theme ?? CampaignTheme.Generic;
                campaign.DiscountPercent = requestDto.DiscountPercent!.Value;
                campaign.StartsAt = requestDto.StartsAt!.Value;
                campaign.EndsAt = requestDto.EndsAt!.Value;
                campaign.RepeatsYearly = requestDto.RepeatsYearly;
                _ = await uow.SaveChangesAsync();
                await ClearCacheAsync();
                return new(OperationResult.Succeeded) { Data = campaign.Id };
            }
            catch (Exception exc)
            {
                return Error<long>(exc);
            }
        }

        public async Task<ResultData<bool>> SetEnabledAsync(long id, bool enabled, long userId, string? twoFactorCode)
        {
            try
            {
                var uow = UnitOfWorkProvider.Value.CreateUnitOfWork();
                var campaign = await uow.GetRepository<Campaign>().GetManyQueryable(t => t.Id == id, tracking: true).FirstOrDefaultAsync();
                if (campaign is null)
                {
                    return NotFound<bool>();
                }

                if (enabled && !campaign.IsEnabled)
                {
                    var check = await twoFactorService.Value.VerifyCodeAsync(userId, twoFactorCode);
                    if (check.OperationResult is not OperationResult.Succeeded)
                    {
                        return new(check.OperationResult) { Errors = check.Errors };
                    }

                    if (CampaignRules.Occurrence(campaign.StartsAt, campaign.EndsAt, campaign.RepeatsYearly, DateTimeOffset.UtcNow) is null)
                    {
                        return Invalid<bool>("endsAt", "This campaign has already ended; change its dates first.");
                    }

                    if (await OverlappingTitleAsync(uow, campaign.Id, (campaign.StartsAt, campaign.EndsAt, campaign.RepeatsYearly)) is { } other)
                    {
                        return Invalid<bool>("overlap", $"These dates overlap the enabled campaign \"{other}\".");
                    }
                }

                campaign.IsEnabled = enabled;
                campaign.LastModifyDate = DateTimeOffset.UtcNow;
                campaign.LastModifyUserId = userId;
                _ = await uow.SaveChangesAsync();
                await ClearCacheAsync();
                return new(OperationResult.Succeeded) { Data = true };
            }
            catch (Exception exc)
            {
                return Error<bool>(exc);
            }
        }

        public async Task<ResultData<bool>> DeleteCampaignAsync(long id)
        {
            try
            {
                var uow = UnitOfWorkProvider.Value.CreateUnitOfWork();
                if (await uow.GetRepository<Payment>().AnyAsync(t => t.CampaignId == id))
                {
                    return Invalid<bool>("used", "Payments used this campaign, so it stays for the record; disable it instead.");
                }

                var repository = uow.GetRepository<Campaign>();
                var campaign = await repository.GetManyQueryable(t => t.Id == id, tracking: true).FirstOrDefaultAsync();
                if (campaign is null)
                {
                    return NotFound<bool>();
                }

                repository.Remove(campaign);
                _ = await uow.SaveChangesAsync();
                await ClearCacheAsync();
                return new(OperationResult.Succeeded) { Data = true };
            }
            catch (Exception exc)
            {
                return Error<bool>(exc);
            }
        }

        public async Task<ResultData<PublicCampaignDto?>> GetPublicCampaignAsync()
        {
            try
            {
                var now = DateTimeOffset.UtcNow;
                var runs = CurrentRuns(await GetEnabledAsync(), now);
                var found = runs.Where(t => t.IsLive).OrderBy(t => t.EndsAt).FirstOrDefault()
                    ?? runs.Where(t => t.StartsAt <= now + CampaignRules.LookAhead).OrderBy(t => t.StartsAt).FirstOrDefault();
                return new(OperationResult.Succeeded) { Data = found };
            }
            catch (Exception exc)
            {
                return Error<PublicCampaignDto?>(exc);
            }
        }

        public async Task<PublicCampaignDto?> GetCheckoutCampaignAsync(long userId)
        {
            try
            {
                var live = CurrentRuns(await GetEnabledAsync(), DateTimeOffset.UtcNow).Where(t => t.IsLive).OrderBy(t => t.EndsAt).FirstOrDefault();
                if (live is null)
                {
                    return null;
                }

                var uow = UnitOfWorkProvider.Value.CreateUnitOfWork();
                var subscribed = await uow.GetRepository<UserSubscription>().AnyAsync(t => t.UserId == userId && t.Status == UserSubscriptionStatus.Active);
                return subscribed ? null : live;
            }
            catch (Exception exc)
            {
                // A failed lookup only means no discount: the checkout itself must not fail over it.
                Logger.Value.LogException(exc);
                return null;
            }
        }

        /// <summary>Each enabled campaign's current or next run, as the site shows it; over one-off campaigns are left out.</summary>
        private static List<PublicCampaignDto> CurrentRuns(List<CachedCampaign> campaigns, DateTimeOffset now) =>
            [.. campaigns
                .Select(t => (Campaign: t, Run: CampaignRules.Occurrence(t.StartsAt, t.EndsAt, t.RepeatsYearly, now)))
                .Where(t => t.Run is not null)
                .Select(t => new PublicCampaignDto
                {
                    Id = t.Campaign.Id,
                    Title = t.Campaign.Title,
                    Headline = t.Campaign.Headline,
                    Theme = t.Campaign.Theme.ToEnumeration<CampaignTheme, byte>(),
                    DiscountPercent = t.Campaign.DiscountPercent,
                    StartsAt = t.Run!.Value.StartsAt,
                    EndsAt = t.Run.Value.EndsAt,
                    IsLive = t.Run.Value.StartsAt <= now,
                })];

        private async Task<List<CachedCampaign>> GetEnabledAsync() =>
            await cacheProvider.Value.GetAsync<List<CachedCampaign>>(EnabledCampaignsCacheKey, async () =>
            {
                var now = DateTimeOffset.UtcNow;
                var uow = UnitOfWorkProvider.Value.CreateUnitOfWork();
                var rows = await uow.GetRepository<Campaign>().GetManyQueryable(t => t.IsEnabled && (t.EndsAt > now || t.RepeatsYearly)).ToListAsync();
                return [.. rows.Select(t => new CachedCampaign
                {
                    Id = t.Id,
                    Title = t.Title,
                    Headline = t.Headline,
                    Theme = t.Theme.Value,
                    DiscountPercent = t.DiscountPercent,
                    StartsAt = t.StartsAt,
                    EndsAt = t.EndsAt,
                    RepeatsYearly = t.RepeatsYearly,
                })];
            }, new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = CacheLifetime }) ?? [];

        private async Task ClearCacheAsync() => await cacheProvider.Value.RemoveAsync(EnabledCampaignsCacheKey);

        /// <summary>The title of another enabled campaign that would ever be live at the same time, or null.</summary>
        private static async Task<string?> OverlappingTitleAsync(IUnitOfWork uow, long exceptId, (DateTimeOffset StartsAt, DateTimeOffset EndsAt, bool RepeatsYearly) window)
        {
            var now = DateTimeOffset.UtcNow;
            var others = await uow.GetRepository<Campaign>().GetManyQueryable(t => t.Id != exceptId && t.IsEnabled && (t.EndsAt > now || t.RepeatsYearly))
                .Select(t => new { t.Title, t.StartsAt, t.EndsAt, t.RepeatsYearly })
                .ToListAsync();
            return others.Find(t => CampaignRules.OverlapsFrom(window, (t.StartsAt, t.EndsAt, t.RepeatsYearly), now))?.Title;
        }

        private static void AddCurrentRun(CampaignDto item)
        {
            var now = DateTimeOffset.UtcNow;
            var run = CampaignRules.Occurrence(item.StartsAt, item.EndsAt, item.RepeatsYearly, now);
            item.CurrentStartsAt = run?.StartsAt;
            item.CurrentEndsAt = run?.EndsAt;
            item.IsLive = CampaignRules.IsLive(item.IsEnabled, item.StartsAt, item.EndsAt, item.RepeatsYearly, now);
        }

        private static IQueryable<CampaignDto> Project(IUnitOfWork uow, IQueryable<Campaign> query)
        {
            var payments = uow.GetRepository<Payment>().GetManyQueryable(t => true);
            return query.Select(t => new CampaignDto
            {
                Id = t.Id,
                Title = t.Title,
                Headline = t.Headline,
                Theme = t.Theme,
                DiscountPercent = t.DiscountPercent,
                StartsAt = t.StartsAt,
                EndsAt = t.EndsAt,
                RepeatsYearly = t.RepeatsYearly,
                IsEnabled = t.IsEnabled,
                PaidPaymentCount = payments.Count(p => p.CampaignId == t.Id && p.Status == PaymentStatus.Paid),
                CreationDate = t.CreationDate,
                LastModifyDate = t.LastModifyDate,
            });
        }

        private static ResultData<T> NotFound<T>() => new(OperationResult.NotFound) { Errors = [new() { Message = "No such campaign.", Reference = "notFound" }] };

        private static ResultData<T> Invalid<T>(string reference, string message) => new(OperationResult.NotValid) { Errors = [new() { Message = message, Reference = reference }] };

        private ResultData<T> Error<T>(Exception exc)
        {
            Logger.Value.LogException(exc);
            return new(OperationResult.Failed) { Errors = [new() { Message = exc.Message }] };
        }

        /// <summary>The cached copy: plain values only (the cache's JSON has no smart-enum converter).</summary>
        private sealed class CachedCampaign
        {
            public long Id { get; set; }
            public string Title { get; set; } = string.Empty;
            public string? Headline { get; set; }
            public byte Theme { get; set; }
            public int DiscountPercent { get; set; }
            public DateTimeOffset StartsAt { get; set; }
            public DateTimeOffset EndsAt { get; set; }
            public bool RepeatsYearly { get; set; }
        }
    }
}
