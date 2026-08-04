namespace GamaEdtech.Application.Service
{
    using System.Diagnostics.CodeAnalysis;
    using System.Security.Cryptography;
    using System.Threading.Tasks;

    using GamaEdtech.Application.Interface;
    using GamaEdtech.Common.Caching;
    using GamaEdtech.Common.Core;
    using GamaEdtech.Common.Data;
    using GamaEdtech.Common.Data.Enumeration;
    using GamaEdtech.Common.DataAccess.UnitOfWork;
    using GamaEdtech.Common.Service;
    using GamaEdtech.Data.Dto.ApplicationSettings;
    using GamaEdtech.Data.Dto.Game;
    using GamaEdtech.Data.Dto.Transaction;
    using GamaEdtech.Domain.Entity;
    using GamaEdtech.Domain.Enumeration;
    using GamaEdtech.Infrastructure.Interface;

    using Microsoft.AspNetCore.Http;
    using Microsoft.Extensions.Localization;
    using Microsoft.Extensions.Logging;

    using static GamaEdtech.Common.Core.Constants;

    public class GameService(Lazy<IUnitOfWorkProvider> unitOfWorkProvider, Lazy<IHttpContextAccessor> httpContextAccessor,
        Lazy<IStringLocalizer<GameService>> localizer, Lazy<ILogger<GameService>> logger, Lazy<ITransactionService> transactionService
        , Lazy<ICacheProvider> cacheProvider, Lazy<ICoreProvider> coreProvider, Lazy<IApplicationSettingsService> applicationSettingsService
        , Lazy<ISubscriptionQuotaService> subscriptionQuotaService)
        : LocalizableServiceBase<GameService>(unitOfWorkProvider, httpContextAccessor, localizer, logger), IGameService
    {
        private const string Prefix = "COIN_";

        public async Task<ResultData<IEnumerable<CoinDto>>> EasterEggFortuneWheelAsync()
        {
            try
            {
                var maxGeneratedCoin = RandomNumberGenerator.GetInt32(3);
                if (maxGeneratedCoin == 0)
                {
                    return new(OperationResult.Succeeded);
                }

                var coins = new List<CoinDto>(maxGeneratedCoin);
                for (var i = 0; i < maxGeneratedCoin; i++)
                {
                    var roll = RandomNumberGenerator.GetInt32(1, 11);

                    var coinType = roll switch
                    {
                        <= 6 => CoinType.Bronze,
                        <= 9 => CoinType.Silver,
                        _ => CoinType.Gold,
                    };

                    var coin = new CoinDto
                    {
                        CoinType = coinType,
                        Id = Guid.NewGuid(),
                        ExpirationTime = DateTimeOffset.UtcNow.AddMinutes(10),
                    };
                    coins.Add(coin);
                    await cacheProvider.Value.SetAsync($"{Prefix}{coin.Id}", coin.CoinType, new Microsoft.Extensions.Caching.Distributed.DistributedCacheEntryOptions
                    {
                        AbsoluteExpiration = coin.ExpirationTime,
                    });
                }

                return new(OperationResult.Succeeded) { Data = coins };
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return new(OperationResult.Failed) { Errors = [new() { Message = exc.Message },] };
            }
        }

        public async Task<ResultData<long>> EasterEggPointsAsync([NotNull] EasterEggPointsRequestDto requestDto)
        {
            try
            {
                var key = $"{Prefix}{requestDto.Id}";
                var coin = await cacheProvider.Value.GetAsync<CoinType>(key);
                if (coin is null)
                {
                    return new(OperationResult.Failed) { Errors = [new() { Message = "Id is Invalid or has been Expired" },] };
                }

                await cacheProvider.Value.RemoveAsync(key);

                var points = await applicationSettingsService.Value.GetSettingAsync<long>(coin.Name.ToEnumeration<CoinType, byte>().ApplicationSettingsName);
                if (points.Data > 0)
                {
                    var transactionRequest = new CreateTransactionRequestDto
                    {
                        UserId = requestDto.UserId,
                        Points = points.Data,
                        Description = "the Easter Egg game.",
                        TransactionType = TransactionType.EasterEgg,
                    };
                    var result = await transactionService.Value.IncreaseBalanceAsync(transactionRequest);

                    return new(result.OperationResult)
                    {
                        Errors = result.Errors,
                        Data = result.Data > 0 ? transactionRequest.Points : 0,
                    };
                }

                return new(OperationResult.Succeeded)
                {
                    Data = 0,
                };
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return new(OperationResult.Failed) { Errors = [new() { Message = exc.Message },] };
            }
        }

        public async Task<ResultData<SpendPointsResponseDto>> SpendPointsAsync([NotNull] SpendPointsRequestDto requestDto)
        {
            try
            {
                var (featureCode, transactionType) = MapContentType(requestDto.ContentType);
                var quotaResult = await subscriptionQuotaService.Value.ConsumeQuotaAsync(new() { UserId = requestDto.UserId, FeatureCode = featureCode, Amount = 1 });
                if (quotaResult.OperationResult is not OperationResult.Succeeded)
                {
                    return new(OperationResult.Failed) { Errors = quotaResult.Errors };
                }

                if (quotaResult.Data!.Consumed)
                {
                    // Covered by the user's subscription quota: no wallet debit, no Transaction row.
                    return new(OperationResult.Succeeded)
                    {
                        Data = new() { Spent = true, PaidBy = SpendSource.SubscriptionQuota, RemainingQuota = quotaResult.Data.RemainingQuota },
                    };
                }

                var currentBalance = await transactionService.Value.GetCurrentBalanceAsync(new() { UserId = requestDto.UserId });
                if (currentBalance.OperationResult is not OperationResult.Succeeded)
                {
                    return new(OperationResult.Failed) { Errors = currentBalance.Errors };
                }

                if (currentBalance.Data < requestDto.Points)
                {
                    return new(OperationResult.Failed)
                    {
                        Errors = [new() { Message = Localizer.Value["InsufficientBalance"] },],
                        Data = new() { Spent = false, UpgradeSuggestions = quotaResult.Data.UpgradeSuggestions, AvailableBillingIntervals = quotaResult.Data.AvailableBillingIntervals },
                    };
                }

                var transactionRequest = new CreateTransactionRequestDto
                {
                    UserId = requestDto.UserId,
                    Points = requestDto.Points,
                    Description = $"Spend Game Points - {requestDto.ContentType.Name}",
                    IdentifierId = requestDto.IdentifierId,
                    TransactionType = transactionType,
                };
                var result = await transactionService.Value.DecreaseBalanceAsync(transactionRequest);

                return new(result.OperationResult)
                {
                    Errors = result.Errors,
                    Data = new() { Spent = result.Data > 0, PaidBy = SpendSource.Points },
                };
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return new(OperationResult.Failed) { Errors = [new() { Message = exc.Message },] };
            }
        }

        // Hardcoded, provably-correct mapping - every ContentType member is listed explicitly.
        // ContentType.Test (the fallback) is used exclusively by the unrelated games/spends endpoint.
        private static (string FeatureCode, TransactionType TransactionType) MapContentType(ContentType contentType) => contentType switch
        {
            _ when contentType == ContentType.PastPaper => (FeatureCodes.PastpaperDownload, TransactionType.DownloadPastPaper),
            _ when contentType == ContentType.Multimedia => (FeatureCodes.MultimediaDownload, TransactionType.DownloadMultimedia),
            _ when contentType == ContentType.Exam => (FeatureCodes.ExamDownload, TransactionType.DownloadExam),
            _ => (FeatureCodes.TestDownload, TransactionType.DownloadTest),
        };

        public async Task<ResultData<TestTimeResponseDto>> TestTimeAsync([NotNull] TestTimeRequestDto requestDto)
        {
            try
            {
                var uow = UnitOfWorkProvider.Value.CreateUnitOfWork();
                var repository = uow.GetRepository<TestSubmission>();
                var exists = await repository.AnyAsync(t => t.UserId == requestDto.UserId && t.TestId == requestDto.TestId);
                if (exists)
                {
                    return new(OperationResult.Duplicate) { Errors = [new Error { Message = Localizer.Value["DuplicateTestSubmission"] }] };
                }

                var valid = await coreProvider.Value.ValidateTestAsync(requestDto);
                if (valid.OperationResult is not OperationResult.Succeeded)
                {
                    return new(valid.OperationResult) { Errors = valid.Errors };
                }

                using var trn = uow.CreateTransactionScope();

                TestSubmission testSubmission = new()
                {
                    CreationDate = DateTimeOffset.UtcNow,
                    IsCorrect = valid.Data,
                    SubmissionId = requestDto.SubmissionId,
                    TestId = requestDto.TestId,
                    UserId = requestDto.UserId,
                };
                repository.Add(testSubmission);
                _ = await uow.SaveChangesAsync();

                var points = 0L;
                if (valid.Data)
                {
                    var pointsValue = await applicationSettingsService.Value.GetSettingAsync<long>(nameof(ApplicationSettingsDto.TestTimeCorrectSubmissionPoints));
                    if (pointsValue.Data > 0)
                    {
                        points = pointsValue.Data;
                        _ = await transactionService.Value.IncreaseBalanceAsync(new()
                        {
                            UserId = requestDto.UserId,
                            Points = points,
                            Description = "TestTime Correct Submission",
                            IdentifierId = testSubmission.Id,
                            TransactionType = TransactionType.CorrectTestTimeSubmission,
                        });
                    }
                }
                else
                {
                    var pointsValue = await applicationSettingsService.Value.GetSettingAsync<long>(nameof(ApplicationSettingsDto.TestTimeIncorrectSubmissionPoints));
                    points = Math.Abs(pointsValue.Data);
                    if (points > 0)
                    {
                        _ = await transactionService.Value.DecreaseBalanceAsync(new()
                        {
                            UserId = requestDto.UserId,
                            Points = points,
                            Description = "TestTime Incorrect Submission",
                            IdentifierId = testSubmission.Id,
                            TransactionType = TransactionType.IncorrectTestTimeSubmission,
                        });
                    }
                }

                trn.Complete();
                return new(OperationResult.Succeeded)
                {
                    Data = new()
                    {
                        IsCorrect = valid.Data,
                        Points = valid.Data ? points : points * -1,
                    },
                };
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return new(OperationResult.Failed) { Errors = [new() { Message = exc.Message },] };
            }
        }

        public async Task<ResultData<ExamPointsResponseDto>> ExamPointsAsync([NotNull] ExamPointsRequestDto requestDto)
        {
            try
            {
                var uow = UnitOfWorkProvider.Value.CreateUnitOfWork();
                var repository = uow.GetRepository<ExamSubmission>();
                var exists = await repository.AnyAsync(t => t.UserId == requestDto.UserId && t.ExamId == requestDto.ExamId);
                if (exists)
                {
                    return new(OperationResult.Duplicate) { Errors = [new Error { Message = Localizer.Value["DuplicateExamSubmission"] }] };
                }

                var result = await coreProvider.Value.GetExamResultAsync(new()
                {
                    ExamId = requestDto.ExamId,
                    SecretKey = requestDto.SecretKey,
                });
                if (result.OperationResult is not OperationResult.Succeeded)
                {
                    return new(result.OperationResult) { Errors = result.Errors };
                }

                using var trn = uow.CreateTransactionScope();

                ExamSubmission examSubmission = new()
                {
                    CreationDate = DateTimeOffset.UtcNow,
                    UserId = requestDto.UserId,
                    ExamId = requestDto.ExamId,
                    Invalid = result.Data!.Invalid,
                    Valid = result.Data.Valid,
                    NoAnswer = result.Data.NoAnswer,
                };
                repository.Add(examSubmission);
                _ = await uow.SaveChangesAsync();

                var correctPointsValue = await applicationSettingsService.Value.GetSettingAsync<long>(nameof(ApplicationSettingsDto.ExamCorrectTestSubmissionPoints));
                var incorrectPointsValue = await applicationSettingsService.Value.GetSettingAsync<long>(nameof(ApplicationSettingsDto.ExamIncorrectTestSubmissionPoints));

                var total = (result.Data.Valid * correctPointsValue.Data) - (result.Data.Invalid * Math.Abs(incorrectPointsValue.Data) / 3);

                _ = total > 0
                    ? await transactionService.Value.IncreaseBalanceAsync(new()
                    {
                        UserId = requestDto.UserId,
                        Points = total,
                        Description = "Exam Submission",
                        IdentifierId = examSubmission.Id,
                        TransactionType = TransactionType.CorrectExamSubmission,
                    })
                    : await transactionService.Value.DecreaseBalanceAsync(new()
                    {
                        UserId = requestDto.UserId,
                        Points = Math.Abs(total),
                        Description = "Exam Submission",
                        IdentifierId = examSubmission.Id,
                        TransactionType = TransactionType.IncorrectExamSubmission,
                    });

                trn.Complete();
                return new(OperationResult.Succeeded)
                {
                    Data = new()
                    {
                        Id = examSubmission.Id,
                        Points = total,
                    },
                };
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return new(OperationResult.Failed) { Errors = [new() { Message = exc.Message },] };
            }
        }
    }
}
