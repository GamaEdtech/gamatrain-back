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

                var urlResult = await provider.GetDownloadUrlAsync(new()
                {
                    Token = requestDto.Token,
                    ExternalContentId = requestDto.Id,
                    ContentType = requestDto.ContentType,
                    FileType = requestDto.FileType,
                    ExtraId = requestDto.ExtraId,
                });
                if (urlResult.OperationResult is not OperationResult.Succeeded || urlResult.Data is null)
                {
                    return new(urlResult.OperationResult) { Errors = urlResult.Errors };
                }

                var data = urlResult.Data;
                if (data.Points is null or 0 || data.Paid == true)
                {
                    // Nothing to charge: either the source reports no price at all (Multimedia/Exam),
                    // the price is zero (a zero-cost Transaction row would just be ledger noise), or
                    // gama-api already considers this paid. No charge means no commission either.
                    return new(OperationResult.Succeeded) { Data = new() { Url = data.Url, Name = data.Name, Spent = false, } };
                }

                // Only DownloadContentType.PastPaper ever reaches here (gama-api reports Points for no other type) -
                // GameService.SpendPointsAsync uses the broader, unrelated ContentType (which also has a Test member).
                var spendResult = await gameService.Value.SpendPointsAsync(new()
                {
                    UserId = requestDto.UserId,
                    Points = data.Points.Value,
                    IdentifierId = requestDto.Id,
                    ContentType = ContentType.PastPaper,
                });
                if (spendResult.OperationResult is not OperationResult.Succeeded || spendResult.Data?.Spent is not true)
                {
                    return new(spendResult.OperationResult) { Errors = spendResult.Errors };
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
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return new(OperationResult.Failed) { Errors = [new() { Message = exc.Message, }] };
            }
        }

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
                    Reason = CommissionReason.LegacyContentDownload,
                    Source = ContentSource.GamaApiLegacy,
                    ContentType = ContentType.PastPaper,
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
