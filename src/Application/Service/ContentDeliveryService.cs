namespace GamaEdtech.Application.Service
{
    using System.Diagnostics.CodeAnalysis;

    using GamaEdtech.Application.Interface;
    using GamaEdtech.Common.Core;
    using GamaEdtech.Common.Core.Extensions.Linq;
    using GamaEdtech.Common.Data;
    using GamaEdtech.Common.DataAccess.UnitOfWork;
    using GamaEdtech.Common.Service;
    using GamaEdtech.Common.Service.Factory;
    using GamaEdtech.Data.Dto.Content;
    using GamaEdtech.Data.Dto.Game;
    using GamaEdtech.Data.Dto.Provider.ContentDelivery;
    using GamaEdtech.Domain.Entity;
    using GamaEdtech.Domain.Entity.Identity;
    using GamaEdtech.Domain.Enumeration;
    using GamaEdtech.Infrastructure.Interface;

    using Microsoft.AspNetCore.Http;
    using Microsoft.EntityFrameworkCore;
    using Microsoft.Extensions.Localization;
    using Microsoft.Extensions.Logging;

    using static GamaEdtech.Common.Core.Constants;

    public class ContentDeliveryService(Lazy<IUnitOfWorkProvider> unitOfWorkProvider, Lazy<IHttpContextAccessor> httpContextAccessor
        , Lazy<IStringLocalizer<ContentDeliveryService>> localizer, Lazy<ILogger<ContentDeliveryService>> logger
        , Lazy<IGenericFactory<IContentDeliveryProvider, ContentSource>> contentDeliveryFactory, Lazy<IGameService> gameService
        , Lazy<IApplicationSettingsService> applicationSettingsService)
        : LocalizableServiceBase<ContentDeliveryService>(unitOfWorkProvider, httpContextAccessor, localizer, logger), IContentDeliveryService
    {
        /// <summary>Fixed points-to-USD rate for commission accounting, first phase - not admin-configurable yet, unlike the percent/threshold settings below.</summary>
        private const decimal PointsPerUsd = 100m;

        public async Task<ResultData<ListDataSource<ContentOwnerCommissionDto>>> GetContentOwnerCommissionsAsync(ListRequestDto<ContentOwnerCommission>? requestDto = null)
        {
            try
            {
                var uow = UnitOfWorkProvider.Value.CreateUnitOfWork();
                var result = await uow.GetRepository<ContentOwnerCommission>().GetManyQueryable(requestDto?.Specification).FilterListAsync(requestDto?.PagingDto);
                var commissions = await result.List.Select(t => new ContentOwnerCommissionDto
                {
                    Id = t.Id,
                    OwnerUserId = t.OwnerUserId,
                    OwnerFirstName = t.Owner!.FirstName,
                    OwnerLastName = t.Owner.LastName,
                    DownloaderUserId = t.DownloaderUserId,
                    Reason = t.Reason,
                    Source = t.Source,
                    ContentType = t.ContentType,
                    ExternalContentId = t.ExternalContentId,
                    ExternalFileType = t.ExternalFileType,
                    ExternalExtraId = t.ExternalExtraId,
                    Points = t.Points,
                    CommissionPercent = t.CommissionPercent,
                    AmountUsd = t.AmountUsd,
                    CreationDate = t.CreationDate,
                }).ToListAsync();
                return new(OperationResult.Succeeded) { Data = new() { List = commissions, TotalRecordsCount = result.TotalRecordsCount } };
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return new(OperationResult.Failed) { Errors = [new() { Message = exc.Message, }] };
            }
        }

        public async Task<ResultData<DownloadContentResponseDto>> DownloadContentAsync([NotNull] DownloadContentRequestDto requestDto)
        {
            try
            {
                var provider = contentDeliveryFactory.Value.GetProvider(ContentSource.GamaApiLegacy);
                if (provider is null)
                {
                    return new(OperationResult.Failed) { Errors = [new() { Message = Localizer.Value["GeneralError"], }] };
                }

                // gama-api's three GET .../{id} detail endpoints (tests/files/exams) are
                // side-effect-free (confirmed live 2026-07-20 - repeated calls never change `paid`),
                // unlike GetDownloadUrlAsync below, which legitimately flips `paid` as a side effect
                // of actually serving the file. Checking here first means a download endpoint only
                // ever gets called once payment is already settled - closing a real bug where a
                // failed local charge still left that call marking the item paid on gama-api's side,
                // letting a retried request through for free. Only PastPaper's pdf/word/answer are
                // reported by /tests/{id} (not "extra"); Multimedia/Exam are always covered by their
                // own detail endpoints. PastPaper's "extra" files fall back to the
                // download-endpoint-only path, which still never trusts that endpoint's own `paid`
                // flag.
                return requestDto.ContentType != DownloadContentType.PastPaper || requestDto.FileType is "pdf" or "word" or "answer"
                    ? await DownloadWithPriceCheckAsync(provider, requestDto)
                    : await DownloadWithoutPriceCheckAsync(provider, requestDto);
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return new(OperationResult.Failed) { Errors = [new() { Message = exc.Message, }] };
            }
        }

        private async Task<ResultData<DownloadContentResponseDto>> DownloadWithPriceCheckAsync(IContentDeliveryProvider provider, DownloadContentRequestDto requestDto)
        {
            var priceStatus = await provider.GetContentPriceStatusAsync(new()
            {
                Token = requestDto.Token,
                ExternalContentId = requestDto.Id,
                ContentType = requestDto.ContentType,
                FileType = requestDto.FileType,
            });
            if (priceStatus.OperationResult is not OperationResult.Succeeded || priceStatus.Data is null)
            {
                return new(priceStatus.OperationResult) { Errors = priceStatus.Errors };
            }

            if (priceStatus.Data.Points is null or 0 || priceStatus.Data.Paid)
            {
                // Already paid (gama-api's own record, unaffected by this side-effect-free check) or
                // genuinely free - fetch the URL without ever attempting a charge.
                var freeUrlResult = await provider.GetDownloadUrlAsync(BuildDownloadUrlRequest(requestDto));
                if (freeUrlResult.Data?.LegacyAuthRejected == true)
                {
                    // Nothing was charged in this branch - no refund needed, just propagate the rejection so
                    // DownloadsController can turn it into a real HTTP 401.
                    return new(OperationResult.Succeeded) { Data = new() { LegacyAuthRejected = true, Spent = false } };
                }

                return freeUrlResult.OperationResult is not OperationResult.Succeeded || freeUrlResult.Data?.Url is null
                    ? new(freeUrlResult.OperationResult) { Errors = freeUrlResult.Errors }
                    : new(OperationResult.Succeeded) { Data = new() { Url = freeUrlResult.Data.Url, Name = freeUrlResult.Data.Name, Spent = false, } };
            }

            var spendResult = await SpendPointsForContentAsync(requestDto, priceStatus.Data.Points.Value);
            if (spendResult.OperationResult is not OperationResult.Succeeded || spendResult.Data?.Spent is not true)
            {
                // Never call the download endpoint on a failed charge - that endpoint is what
                // legitimately flips gama-api's paid flag, and calling it here regardless of payment
                // is exactly the bug this whole price check exists to close.
                return new(spendResult.OperationResult)
                {
                    Errors = spendResult.Errors,
                    Data = new()
                    {
                        Reason = spendResult.Data?.Reason,
                        CurrentSubscriptionId = spendResult.Data?.CurrentSubscriptionId,
                        CurrentPlanId = spendResult.Data?.CurrentPlanId,
                        CurrentPlanTitle = spendResult.Data?.CurrentPlanTitle,
                        UpgradeSuggestions = spendResult.Data?.UpgradeSuggestions,
                        AvailableBillingIntervals = spendResult.Data?.AvailableBillingIntervals,
                    },
                };
            }

            var urlResult = await provider.GetDownloadUrlAsync(BuildDownloadUrlRequest(requestDto));
            if (urlResult.Data?.LegacyAuthRejected == true)
            {
                // gama-api rejected the caller's token for this specific call - even though the price check
                // above (an earlier, separate call, using the same token) succeeded a moment ago, and the charge
                // above already went through. Refund it (best-effort, see RefundFailedDownloadAsync) and let
                // DownloadsController turn this into a real HTTP 401, same as IdentitiesController.GetDashboard
                // already does for the same failure shape - see DownloadContentResponseDto.LegacyAuthRejected.
                await RefundFailedDownloadAsync(requestDto, spendResult.Data, priceStatus.Data.Points.Value);
                return new(OperationResult.Succeeded) { Data = new() { LegacyAuthRejected = true, Spent = false } };
            }

            if (urlResult.OperationResult is not OperationResult.Succeeded || urlResult.Data?.Url is null)
            {
                // The charge above already succeeded - gama-api's download call can still fail afterwards for
                // reasons that have nothing to do with auth (content removed, gama-api downtime, ...). Without
                // this, the user is charged (quota or wallet points) for content that never actually gets
                // delivered, with no way to get it back. Best-effort: a refund failure must never turn into a
                // reported success for a download that didn't happen - the original urlResult failure still
                // wins below.
                await RefundFailedDownloadAsync(requestDto, spendResult.Data, priceStatus.Data.Points.Value);
                return new(urlResult.OperationResult) { Errors = urlResult.Errors };
            }

            if (urlResult.Data.OwnerExternalId is not null)
            {
                await AccrueCommissionAsync(requestDto, urlResult.Data.OwnerExternalId.Value, priceStatus.Data.Points.Value);
            }

            return new(OperationResult.Succeeded)
            {
                Data = new() { Url = urlResult.Data.Url, Name = urlResult.Data.Name, Spent = true, PaidBy = spendResult.Data.PaidBy, },
            };
        }

        private async Task<ResultData<DownloadContentResponseDto>> DownloadWithoutPriceCheckAsync(IContentDeliveryProvider provider, DownloadContentRequestDto requestDto)
        {
            var urlResult = await provider.GetDownloadUrlAsync(BuildDownloadUrlRequest(requestDto));
            if (urlResult.Data?.LegacyAuthRejected == true)
            {
                // Nothing was ever charged in this path before this call (charge only happens after a
                // successful fetch, below) - no refund needed, just propagate the rejection so
                // DownloadsController can turn it into a real HTTP 401.
                return new(OperationResult.Succeeded) { Data = new() { LegacyAuthRejected = true, Spent = false } };
            }

            if (urlResult.OperationResult is not OperationResult.Succeeded || urlResult.Data?.Url is null)
            {
                return new(urlResult.OperationResult) { Errors = urlResult.Errors };
            }

            var data = urlResult.Data;
            if (data.Points is null or 0)
            {
                // Nothing to charge: the source reports no price at all (Multimedia/Exam, or
                // PastPaper's "extra" files, which bypass the price-check path above) or the price is
                // a genuine zero. gama-api's own `paid` flag is deliberately not read here - unlike
                // GetContentPriceStatusAsync, this call is exactly the one that can flip it, so it's
                // never trustworthy as a reason to skip charging from this response alone.
                return new(OperationResult.Succeeded) { Data = new() { Url = data.Url, Name = data.Name, Spent = false, } };
            }

            var spendResult = await SpendPointsForContentAsync(requestDto, data.Points.Value);
            if (spendResult.OperationResult is not OperationResult.Succeeded || spendResult.Data?.Spent is not true)
            {
                return new(spendResult.OperationResult)
                {
                    Errors = spendResult.Errors,
                    Data = new()
                    {
                        Reason = spendResult.Data?.Reason,
                        CurrentSubscriptionId = spendResult.Data?.CurrentSubscriptionId,
                        CurrentPlanId = spendResult.Data?.CurrentPlanId,
                        CurrentPlanTitle = spendResult.Data?.CurrentPlanTitle,
                        UpgradeSuggestions = spendResult.Data?.UpgradeSuggestions,
                        AvailableBillingIntervals = spendResult.Data?.AvailableBillingIntervals,
                    },
                };
            }

            if (data.OwnerExternalId is not null)
            {
                await AccrueCommissionAsync(requestDto, data.OwnerExternalId.Value, data.Points.Value);
            }

            return new(OperationResult.Succeeded)
            {
                Data = new() { Url = data.Url, Name = data.Name, Spent = true, PaidBy = spendResult.Data.PaidBy, },
            };
        }

        private static GetDownloadUrlRequestDto BuildDownloadUrlRequest(DownloadContentRequestDto requestDto) => new()
        {
            Token = requestDto.Token,
            ExternalContentId = requestDto.Id,
            ContentType = requestDto.ContentType,
            FileType = requestDto.FileType,
            ExtraId = requestDto.ExtraId,
        };

        // QuotaAmount mirrors the same gama-api-reported price as Points (never the client) - a
        // download's subscription-quota cost scales with what the content actually costs, instead of
        // the old flat 1-credit-per-download. Clamped into `int` range (ConsumeQuotaRequestDto/
        // UserSubscriptionQuota.Used/Limit are all `int`) - no real content price is anywhere near
        // int.MaxValue, this just avoids a checked-arithmetic OverflowException on a pathological value.
        private Task<ResultData<SpendPointsResponseDto>> SpendPointsForContentAsync(DownloadContentRequestDto requestDto, long points) =>
            gameService.Value.SpendPointsAsync(new()
            {
                UserId = requestDto.UserId,
                Points = points,
                QuotaAmount = (int)Math.Min(points, int.MaxValue),
                IdentifierId = requestDto.Id,
                ContentType = MapContentType(requestDto.ContentType),
            });

        /// <summary>
        /// Reverses a SpendPointsForContentAsync charge that just succeeded, for the case where the actual
        /// gama-api download call that follows it then fails - see the caller's comment. Best-effort: swallows
        /// its own failure (logged) rather than throwing, since the caller has already committed to reporting
        /// the original download failure regardless of whether this refund itself works.
        /// </summary>
        private async Task RefundFailedDownloadAsync(DownloadContentRequestDto requestDto, SpendPointsResponseDto? spendData, long points)
        {
            if (spendData?.PaidBy is not SpendSource paidBy)
            {
                return;
            }

            try
            {
                _ = await gameService.Value.RefundPointsAsync(new()
                {
                    UserId = requestDto.UserId,
                    Points = points,
                    QuotaAmount = (int)Math.Min(points, int.MaxValue),
                    IdentifierId = requestDto.Id,
                    ContentType = MapContentType(requestDto.ContentType),
                    PaidBy = paidBy,
                    UserSubscriptionId = spendData.CurrentSubscriptionId,
                });
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
            }
        }

        // Hardcoded, provably-correct mapping - DownloadContentType is this feature's own 3-member
        // enum (see GamaApiContentDeliveryProvider), ContentType is the broader, unrelated enum
        // GameService.SpendPointsAsync/ContentOwnerCommission use (which also has a Test member,
        // relevant only to the unrelated games/spends endpoint).
        private static ContentType MapContentType(DownloadContentType downloadContentType) => downloadContentType switch
        {
            _ when downloadContentType == DownloadContentType.Multimedia => ContentType.Multimedia,
            _ when downloadContentType == DownloadContentType.Exam => ContentType.Exam,
            _ => ContentType.PastPaper,
        };

        /// <summary>Best-effort: an owner that can't be resolved to a local account just means no commission this time, not a failed download - the charge to the downloader has already succeeded.</summary>
        private async Task AccrueCommissionAsync(DownloadContentRequestDto requestDto, long ownerExternalId, long points)
        {
            try
            {
                var uow = UnitOfWorkProvider.Value.CreateUnitOfWork();
                var ownerId = await uow.GetRepository<ApplicationUser>()
                    .GetManyQueryable(t => t.CoreId == ownerExternalId)
                    .Select(t => t.Id)
                    .FirstOrDefaultAsync();
                if (ownerId == default)
                {
                    return;
                }

                var settings = await applicationSettingsService.Value.GetApplicationSettingsAsync();
                var percent = settings.Data?.ContentOwnerCommissionPercent ?? 0;
                if (percent <= 0)
                {
                    return;
                }

                var amountUsd = points * percent / 100m / PointsPerUsd;

                uow.GetRepository<ContentOwnerCommission>().Add(new()
                {
                    OwnerUserId = ownerId,
                    DownloaderUserId = requestDto.UserId,
                    Reason = CommissionReason.ContentDownload,
                    Source = ContentSource.GamaApiLegacy,
                    ContentType = MapContentType(requestDto.ContentType),
                    ExternalContentId = requestDto.Id,
                    ExternalFileType = requestDto.FileType,
                    ExternalExtraId = requestDto.ExtraId,
                    Points = points,
                    CommissionPercent = percent,
                    AmountUsd = amountUsd,
                    CreationDate = DateTimeOffset.UtcNow,
                });
                _ = await uow.SaveChangesAsync();
            }
            catch (Exception exc)
            {
                // Commission accrual is not allowed to fail the download - the downloader has already been charged.
                Logger.Value.LogException(exc);
            }
        }
    }
}
