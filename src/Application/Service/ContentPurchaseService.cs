namespace GamaEdtech.Application.Service
{
    using System.Diagnostics.CodeAnalysis;

    using GamaEdtech.Application.Interface;
    using GamaEdtech.Common.Core;
    using GamaEdtech.Common.Data;
    using GamaEdtech.Common.DataAccess.UnitOfWork;
    using GamaEdtech.Common.Service;
    using GamaEdtech.Data.Dto.ContentPurchase;
    using GamaEdtech.Domain.Entity;
    using GamaEdtech.Domain.Enumeration;

    using Microsoft.AspNetCore.Http;
    using Microsoft.EntityFrameworkCore;
    using Microsoft.Extensions.Localization;
    using Microsoft.Extensions.Logging;

    using static GamaEdtech.Common.Core.Constants;

    /// <inheritdoc cref="IContentPurchaseService"/>
    public class ContentPurchaseService(Lazy<IUnitOfWorkProvider> unitOfWorkProvider, Lazy<IHttpContextAccessor> httpContextAccessor
        , Lazy<IStringLocalizer<ContentPurchaseService>> localizer, Lazy<ILogger<ContentPurchaseService>> logger, Lazy<IGameService> gameService)
        : LocalizableServiceBase<ContentPurchaseService>(unitOfWorkProvider, httpContextAccessor, localizer, logger), IContentPurchaseService
    {
        public async Task<ResultData<ContentChargeResponseDto>> ChargeAsync([NotNull] ContentPurchaseRequestDto requestDto)
        {
            try
            {
                if (requestDto.Points <= 0)
                {
                    return new(OperationResult.Succeeded) { Data = new() };
                }

                var variant = Normalize(requestDto.Variant);
                var uow = UnitOfWorkProvider.Value.CreateUnitOfWork();
                var alreadyPurchased = await uow.GetRepository<ContentPurchase>()
                    .GetManyQueryable(t => t.UserId == requestDto.UserId && t.ContentType == requestDto.ContentType && t.ContentId == requestDto.ContentId && t.Variant == variant)
                    .AnyAsync();
                if (alreadyPurchased)
                {
                    return new(OperationResult.Succeeded) { Data = new() { Points = requestDto.Points, AlreadyPurchased = true } };
                }

                // ContentType -> subscription feature (e.g. Exam -> ExamDownload): quota first, then points.
                var spend = await gameService.Value.SpendPointsAsync(new()
                {
                    UserId = requestDto.UserId,
                    Points = requestDto.Points,
                    QuotaAmount = (int)Math.Min(requestDto.Points, int.MaxValue),
                    IdentifierId = requestDto.ContentId,
                    ContentType = requestDto.SpendContentType,
                });
                if (spend.OperationResult is OperationResult.Succeeded && spend.Data?.Spent is true)
                {
                    return new(OperationResult.Succeeded) { Data = new() { Points = requestDto.Points, Spend = spend.Data } };
                }

                var refusedResult = spend.OperationResult is OperationResult.Succeeded ? OperationResult.Failed : spend.OperationResult;
                return new(refusedResult)
                {
                    Errors = spend.Errors,
                    Data = new() { Points = requestDto.Points, Refused = spend.Data ?? new() },
                };
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return new(OperationResult.Failed) { Errors = [new() { Message = exc.Message },] };
            }
        }

        public async Task CompletePurchaseAsync([NotNull] ContentPurchaseRequestDto requestDto, [NotNull] ContentChargeResponseDto charge)
        {
            if (charge.Spend?.PaidBy is not SpendSource paidBy)
            {
                return;
            }

            try
            {
                var uow = UnitOfWorkProvider.Value.CreateUnitOfWork();
                uow.GetRepository<ContentPurchase>().Add(new()
                {
                    UserId = requestDto.UserId,
                    ContentType = requestDto.ContentType,
                    ContentId = requestDto.ContentId,
                    Variant = Normalize(requestDto.Variant),
                    Points = charge.Points,
                    PaidBy = paidBy,
                    CreationDate = DateTimeOffset.UtcNow,
                });
                _ = await uow.SaveChangesAsync();
            }
            catch (DbUpdateException exc)
            {
                Logger.Value.LogException(exc);
                await RefundAsync(requestDto, charge);
            }
        }

        public async Task RefundAsync([NotNull] ContentPurchaseRequestDto requestDto, [NotNull] ContentChargeResponseDto charge)
        {
            if (charge.Spend?.PaidBy is not SpendSource paidBy)
            {
                return;
            }

            try
            {
                _ = await gameService.Value.RefundPointsAsync(new()
                {
                    UserId = requestDto.UserId,
                    Points = charge.Points,
                    QuotaAmount = (int)Math.Min(charge.Points, int.MaxValue),
                    IdentifierId = requestDto.ContentId,
                    ContentType = requestDto.SpendContentType,
                    PaidBy = paidBy,
                    UserSubscriptionId = charge.Spend.CurrentSubscriptionId,
                });
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
            }
        }

        public async Task<ResultData<IReadOnlyCollection<string>>> GetPurchasedVariantsAsync(long userId, PurchasableContentType contentType, long contentId)
        {
            try
            {
                var uow = UnitOfWorkProvider.Value.CreateUnitOfWork();
                var variants = await uow.GetRepository<ContentPurchase>()
                    .GetManyQueryable(t => t.UserId == userId && t.ContentType == contentType && t.ContentId == contentId)
                    .Select(t => t.Variant)
                    .ToListAsync();
                return new(OperationResult.Succeeded) { Data = variants };
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return new(OperationResult.Failed) { Errors = [new() { Message = exc.Message },] };
            }
        }

        private static string Normalize(string? variant) => variant ?? string.Empty;
    }
}
