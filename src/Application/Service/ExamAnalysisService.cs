namespace GamaEdtech.Application.Service
{
    using System.Diagnostics.CodeAnalysis;

    using EntityFramework.Exceptions.Common;

    using GamaEdtech.Application.Interface;
    using GamaEdtech.Common.Core;
    using GamaEdtech.Common.Data;
    using GamaEdtech.Common.DataAccess.UnitOfWork;
    using GamaEdtech.Common.Service;
    using GamaEdtech.Data.Dto.ContentPurchase;
    using GamaEdtech.Data.Dto.ExamAnalysis;
    using GamaEdtech.Domain.Entity;
    using GamaEdtech.Domain.Enumeration;
    using GamaEdtech.Domain.Specification.ExamAnalysis;

    using Microsoft.AspNetCore.Http;
    using Microsoft.Extensions.Localization;
    using Microsoft.Extensions.Logging;

    using static GamaEdtech.Common.Core.Constants;

    public class ExamAnalysisService(Lazy<IUnitOfWorkProvider> unitOfWorkProvider, Lazy<IHttpContextAccessor> httpContextAccessor,
        Lazy<IStringLocalizer<ExamAnalysisService>> localizer, Lazy<ILogger<ExamAnalysisService>> logger,
        Lazy<IContentPurchaseService> contentPurchaseService)
        : LocalizableServiceBase<ExamAnalysisService>(unitOfWorkProvider, httpContextAccessor, localizer, logger), IExamAnalysisService
    {
        public async Task<ResultData<PrepareExamAnalysisResponseDto>> PrepareAsync([NotNull] PrepareExamAnalysisRequestDto requestDto)
        {
            ContentPurchaseRequestDto? purchase = null;
            ContentChargeResponseDto? charge = null;

            try
            {
                var existing = await GetAsync(requestDto.UserId, requestDto.ExamId);
                if (existing is not null)
                {
                    return Prepared(existing);
                }

                purchase = CreatePurchase(requestDto);
                var chargeResult = await contentPurchaseService.Value.ChargeAsync(purchase);
                if (chargeResult.OperationResult is not OperationResult.Succeeded || chargeResult.Data is null)
                {
                    return new(chargeResult.OperationResult)
                    {
                        Errors = chargeResult.Errors,
                        Data = new() { Charge = chargeResult.Data?.Refused },
                    };
                }

                charge = chargeResult.Data;
                var uow = UnitOfWorkProvider.Value.CreateUnitOfWork();
                var analysis = new ExamAnalysis
                {
                    UserId = requestDto.UserId,
                    ExamId = requestDto.ExamId,
                    Points = ExamAnalysisPricing.Price,
                    CreationDate = DateTimeOffset.UtcNow,
                };
                uow.GetRepository<ExamAnalysis>().Add(analysis);
                _ = await uow.SaveChangesAsync();

                await contentPurchaseService.Value.CompletePurchaseAsync(purchase, charge);

                return new(OperationResult.Succeeded)
                {
                    Data = new()
                    {
                        Analysis = ToDto(analysis),
                        RequiresAnalysis = true,
                        PaymentSucceeded = charge.Spend?.Spent == true,
                        Charge = charge.Spend,
                    },
                };
            }
            catch (UniqueConstraintException)
            {
                await RefundAsync(purchase, charge);
                var existing = await GetAsync(requestDto.UserId, requestDto.ExamId);
                return existing is null
                    ? Failed<PrepareExamAnalysisResponseDto>()
                    : Prepared(existing);
            }
            catch (Exception exc)
            {
                await RefundAsync(purchase, charge);
                Logger.Value.LogException(exc);
                return Failed<PrepareExamAnalysisResponseDto>();
            }
        }

        public async Task<ResultData<ExamAnalysisDto>> SaveResultAsync([NotNull] SaveExamAnalysisResultRequestDto requestDto)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(requestDto.Result))
                {
                    return new(OperationResult.NotValid)
                    {
                        Errors = [new() { Message = Localizer.Value["ResultRequired"] },],
                    };
                }

                var uow = UnitOfWorkProvider.Value.CreateUnitOfWork();
                var repository = uow.GetRepository<ExamAnalysis>();
                var analysis = await repository.GetAsync(new UserIdAndExamIdEqualsSpecification(requestDto.UserId, requestDto.ExamId));
                if (analysis is null)
                {
                    return new(OperationResult.NotFound)
                    {
                        Errors = [new() { Message = Localizer.Value["ExamAnalysisNotFound"] },],
                    };
                }

                if (analysis.Result is null)
                {
                    analysis.Result = requestDto.Result;
                    _ = repository.Update(analysis);
                    _ = await uow.SaveChangesAsync();
                }

                return new(OperationResult.Succeeded) { Data = ToDto(analysis) };
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return Failed<ExamAnalysisDto>();
            }
        }

        private async Task<ExamAnalysis?> GetAsync(long userId, long examId)
        {
            var uow = UnitOfWorkProvider.Value.CreateUnitOfWork();
            return await uow.GetRepository<ExamAnalysis>().GetAsync(new UserIdAndExamIdEqualsSpecification(userId, examId), false);
        }

        private async Task RefundAsync(ContentPurchaseRequestDto? purchase, ContentChargeResponseDto? charge)
        {
            if (purchase is not null && charge is not null)
            {
                await contentPurchaseService.Value.RefundAsync(purchase, charge);
            }
        }

        private ResultData<T> Failed<T>() => new(OperationResult.Failed)
        {
            Errors = [new() { Message = Localizer.Value["OperationFailed"] },],
        };

        private static ContentPurchaseRequestDto CreatePurchase(PrepareExamAnalysisRequestDto requestDto) => new()
        {
            UserId = requestDto.UserId,
            ContentType = PurchasableContentType.ExamAnalysis,
            ContentId = requestDto.ExamId,
            Points = ExamAnalysisPricing.Price,
            SpendContentType = ContentType.Exam,
        };

        private static ResultData<PrepareExamAnalysisResponseDto> Prepared(ExamAnalysis analysis) => new(OperationResult.Succeeded)
        {
            Data = new()
            {
                Analysis = ToDto(analysis),
                RequiresAnalysis = analysis.Result is null,
            },
        };

        private static ExamAnalysisDto ToDto(ExamAnalysis analysis) => new()
        {
            Id = analysis.Id,
            ExamId = analysis.ExamId,
            Result = analysis.Result,
            Points = analysis.Points,
        };
    }
}
