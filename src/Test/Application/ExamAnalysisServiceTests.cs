namespace GamaEdtech.Test.Application
{
    using System.Diagnostics.CodeAnalysis;
    using System.Reflection;

    using GamaEdtech.Application.Interface;
    using GamaEdtech.Application.Service;
    using GamaEdtech.Common.Data;
    using GamaEdtech.Common.DataAccess.Context;
    using GamaEdtech.Common.DataAccess.Repositories;
    using GamaEdtech.Common.DataAccess.UnitOfWork;
    using GamaEdtech.Common.Identity;
    using GamaEdtech.Data.Dto.ApplicationSettings;
    using GamaEdtech.Data.Dto.ContentPurchase;
    using GamaEdtech.Data.Dto.ExamAnalysis;
    using GamaEdtech.Data.Dto.Game;
    using GamaEdtech.Data.Dto.Provider.Ai;
    using GamaEdtech.Domain.Entity;
    using GamaEdtech.Domain.Enumeration;
    using GamaEdtech.Infrastructure.EntityFramework.Context;
    using GamaEdtech.Infrastructure.Interface;
    using GamaEdtech.Presentation.Api.Controllers;

    using Microsoft.AspNetCore.Http;
    using Microsoft.AspNetCore.Mvc;
    using Microsoft.EntityFrameworkCore;
    using Microsoft.Extensions.Configuration;
    using Microsoft.Extensions.DependencyInjection;
    using Microsoft.Extensions.Localization;
    using Microsoft.Extensions.Logging.Abstractions;

    using Xunit;

    using static GamaEdtech.Common.Core.Constants;

    public sealed class ExamAnalysisServiceTests : IDisposable
    {
        private const long ExamId = 4001;
        private const long UserId = 71;
        private const string SecretKey = "legacy-token";

        private readonly TestApplicationDBContext context;
        private readonly FakeContentPurchaseService contentPurchaseService = new();
        private readonly FakeApplicationSettingsService applicationSettingsService = new();
        private readonly FakeAiService aiService = new();
        private readonly ICoreProvider coreProvider;
        private readonly CoreProviderProxy coreProviderProxy;
        private readonly ServiceProvider serviceProvider;

        public ExamAnalysisServiceTests()
        {
            Environment.SetEnvironmentVariable("Test", "True");

            ServiceCollection services = new();
            _ = services.AddLogging();
            _ = services.AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["EnableAudit"] = bool.FalseString,
            }).Build());
            _ = services.AddSingleton<IHttpContextAccessor, HttpContextAccessor>();
            _ = services.AddSingleton<TestApplicationDBContext>();
            _ = services.AddSingleton<IEntityContext>(provider => provider.GetRequiredService<TestApplicationDBContext>());
            _ = services.AddTransient(typeof(IRepository<,>), typeof(GenericEntityRepository<,>));
            _ = services.AddSingleton<IUnitOfWorkProvider, UnitOfWorkProvider>();

            serviceProvider = services.BuildServiceProvider();
            context = serviceProvider.GetRequiredService<TestApplicationDBContext>();
            coreProvider = DispatchProxy.Create<ICoreProvider, CoreProviderProxy>();
            coreProviderProxy = (CoreProviderProxy)coreProvider;
        }

        public void Dispose()
        {
            context.Dispose();
            serviceProvider.Dispose();
            GC.SuppressFinalize(this);
        }

        [Fact]
        public async Task CompletedAnalysisIsReturnedWithoutCoreChargeOrAiCalls()
        {
            _ = await AddAnalysisAsync("existing result");

            var result = await CreateService().PrepareAsync(Request());

            Assert.Equal(OperationResult.Succeeded, result.OperationResult);
            Assert.Equal("existing result", result.Data!.Analysis!.Result);
            Assert.Null(result.Data.Charge);
            Assert.Equal(0, coreProviderProxy.GetExamResultDetailCalls);
            Assert.Equal(0, contentPurchaseService.ChargeCalls);
            Assert.Equal(0, aiService.Calls);
        }

        [Fact]
        public async Task SuccessfulChargeGeneratesAndStoresAnalysisThenCompletesPurchase()
        {
            var result = await CreateService().PrepareAsync(Request());

            Assert.Equal(OperationResult.Succeeded, result.OperationResult);
            Assert.Equal("AI analysis", result.Data!.Analysis!.Result);
            Assert.NotNull(result.Data.Charge);
            Assert.Equal(1, contentPurchaseService.ChargeCalls);
            Assert.Equal(1, aiService.Calls);
            Assert.Equal(1, contentPurchaseService.CompleteCalls);
            Assert.Equal(0, contentPurchaseService.RefundCalls);
            Assert.Equal(UserId, contentPurchaseService.LastChargeRequest!.UserId);
            Assert.Equal(ExamId, contentPurchaseService.LastChargeRequest.ContentId);
            Assert.Equal(ApplicationSettingsDto.DefaultExamAnalysisPoints, contentPurchaseService.LastChargeRequest.Points);
            Assert.Equal(PurchasableContentType.ExamAnalysis, contentPurchaseService.LastChargeRequest.ContentType);
            Assert.Equal(ContentType.ExamAnalysis, contentPurchaseService.LastChargeRequest.SpendContentType);

            var analysis = Assert.Single(await context.Set<ExamAnalysis>().ToListAsync());
            Assert.Equal("AI analysis", analysis.Result);
        }

        [Fact]
        public async Task LegacyPendingAnalysisIsGeneratedWithoutCreatingAnotherRow()
        {
            _ = await AddAnalysisAsync(null);
            contentPurchaseService.ChargeResult = new(OperationResult.Succeeded)
            {
                Data = new() { Points = ApplicationSettingsDto.DefaultExamAnalysisPoints, AlreadyPurchased = true },
            };

            var result = await CreateService().PrepareAsync(Request());

            Assert.Equal(OperationResult.Succeeded, result.OperationResult);
            Assert.Equal("AI analysis", result.Data!.Analysis!.Result);
            Assert.Null(result.Data.Charge);
            _ = Assert.Single(await context.Set<ExamAnalysis>().ToListAsync());
        }

        [Fact]
        public async Task SubscriptionQuotaChargeIsReturnedWithTheGeneratedAnalysis()
        {
            contentPurchaseService.ChargeResult = new(OperationResult.Succeeded)
            {
                Data = new()
                {
                    Points = ApplicationSettingsDto.DefaultExamAnalysisPoints,
                    Spend = new() { Spent = true, PaidBy = SpendSource.SubscriptionQuota, RemainingQuota = 4 },
                },
            };

            var result = await CreateService().PrepareAsync(Request());

            Assert.Equal(OperationResult.Succeeded, result.OperationResult);
            Assert.Equal(SpendSource.SubscriptionQuota, result.Data!.Charge!.PaidBy);
            Assert.Equal(4, result.Data.Charge.RemainingQuota);
            Assert.Equal("AI analysis", result.Data.Analysis!.Result);
        }

        [Fact]
        public async Task RefusedChargeDoesNotCallAiOrCreateAnalysis()
        {
            contentPurchaseService.ChargeResult = new(OperationResult.Failed)
            {
                Errors = [new() { Message = "Insufficient balance" },],
                Data = new() { Points = ApplicationSettingsDto.DefaultExamAnalysisPoints, Refused = new() { Spent = false } },
            };

            var result = await CreateService().PrepareAsync(Request());

            Assert.Equal(OperationResult.Failed, result.OperationResult);
            Assert.NotNull(result.Data!.Charge);
            Assert.Null(result.Data.Analysis);
            Assert.Equal(0, aiService.Calls);
            Assert.Empty(await context.Set<ExamAnalysis>().ToListAsync());
            Assert.Equal(0, contentPurchaseService.CompleteCalls);
        }

        [Fact]
        public async Task AiFailureRefundsTheChargeAndDoesNotCreateAnalysis()
        {
            aiService.CompletionResult = new(OperationResult.Failed)
            {
                Errors = [new() { Message = "AI failed" },],
            };

            var result = await CreateService().PrepareAsync(Request());

            Assert.Equal(OperationResult.Failed, result.OperationResult);
            Assert.Equal(1, contentPurchaseService.RefundCalls);
            Assert.Equal(0, contentPurchaseService.CompleteCalls);
            Assert.Empty(await context.Set<ExamAnalysis>().ToListAsync());
        }

        [Fact]
        public async Task PurchaseCompletionExceptionRefundsAndDoesNotStoreAnalysis()
        {
            contentPurchaseService.CompleteException = new InvalidOperationException("Purchase completion failed");

            var result = await CreateService().PrepareAsync(Request());

            Assert.Equal(OperationResult.Failed, result.OperationResult);
            Assert.Equal(1, contentPurchaseService.CompleteCalls);
            Assert.Equal(1, contentPurchaseService.RefundCalls);
            Assert.Empty(await context.Set<ExamAnalysis>().ToListAsync());
        }

        [Fact]
        public async Task SaveFailureAfterCompletedPurchaseIsNotRefundedAndCanBeRetriedForFree()
        {
            context.FailOnSave = true;

            var result = await CreateService().PrepareAsync(Request());

            Assert.Equal(OperationResult.Failed, result.OperationResult);
            Assert.Equal(1, contentPurchaseService.CompleteCalls);
            Assert.Equal(0, contentPurchaseService.RefundCalls);
        }

        [Fact]
        public async Task InvalidOrInaccessibleExamIsRejectedBeforeCharging()
        {
            coreProviderProxy.ExamResult = new(OperationResult.NotFound)
            {
                Errors = [new() { Message = "Exam result not found" },],
            };

            var result = await CreateService().PrepareAsync(Request());

            Assert.Equal(OperationResult.NotFound, result.OperationResult);
            Assert.Equal(1, coreProviderProxy.GetExamResultDetailCalls);
            Assert.Equal(0, contentPurchaseService.ChargeCalls);
            Assert.Equal(0, aiService.Calls);
        }

        [Fact]
        public async Task ConfiguredPriceIsUsedForChargeAndStoredAnalysis()
        {
            const long configuredPoints = 250;
            applicationSettingsService.SettingsResult = new(OperationResult.Succeeded)
            {
                Data = new() { ExamAnalysisPoints = configuredPoints },
            };

            var result = await CreateService().PrepareAsync(Request());

            Assert.Equal(OperationResult.Succeeded, result.OperationResult);
            Assert.Equal(configuredPoints, contentPurchaseService.LastChargeRequest!.Points);
            Assert.Equal(configuredPoints, result.Data!.Analysis!.Points);
            Assert.Equal(configuredPoints, (await context.Set<ExamAnalysis>().SingleAsync()).Points);
        }

        [Fact]
        public async Task SettingsFailureIsReturnedBeforeCharging()
        {
            applicationSettingsService.SettingsResult = new(OperationResult.Failed)
            {
                Errors = [new() { Message = "Settings unavailable" },],
            };

            var result = await CreateService().PrepareAsync(Request());

            Assert.Equal(OperationResult.Failed, result.OperationResult);
            Assert.Equal("Settings unavailable", Assert.Single(result.Errors!).Message);
            Assert.Equal(0, contentPurchaseService.ChargeCalls);
            Assert.Equal(0, aiService.Calls);
        }

        [Fact]
        public async Task PromptContainsVerifiedResultAndQuestionAnswers()
        {
            _ = await CreateService().PrepareAsync(Request());

            Assert.Contains("Score percentage: 100%", aiService.LastRequest!.UserPrompt, StringComparison.Ordinal);
            Assert.Contains("Student's first name: Sara", aiService.LastRequest.UserPrompt, StringComparison.Ordinal);
            Assert.Contains("Title: Arithmetic", aiService.LastRequest.UserPrompt, StringComparison.Ordinal);
            Assert.Contains("Time allowed (minutes): 45", aiService.LastRequest.UserPrompt, StringComparison.Ordinal);
            Assert.Contains("Time the student took: 12 min 5 s", aiService.LastRequest.UserPrompt, StringComparison.Ordinal);
            Assert.Contains("Rank among all participants: 3 of 120", aiService.LastRequest.UserPrompt, StringComparison.Ordinal);
            Assert.Contains("Topics: Addition", aiService.LastRequest.UserPrompt, StringComparison.Ordinal);
            Assert.Contains("Add the numbers. 2 + 2 = ?", aiService.LastRequest.UserPrompt, StringComparison.Ordinal);
            Assert.Contains("B) 4", aiService.LastRequest.UserPrompt, StringComparison.Ordinal);
            Assert.Contains("Student answer: B", aiService.LastRequest.UserPrompt, StringComparison.Ordinal);
            Assert.Contains("Correct answer: B", aiService.LastRequest.UserPrompt, StringComparison.Ordinal);
            Assert.Contains("Result: Correct", aiService.LastRequest.UserPrompt, StringComparison.Ordinal);
            Assert.DoesNotContain("<p>", aiService.LastRequest.UserPrompt, StringComparison.Ordinal);
            Assert.Contains("Return only plain text", aiService.LastRequest.SystemPrompt, StringComparison.Ordinal);
            Assert.Contains("other exams on Gamatrain", aiService.LastRequest.SystemPrompt, StringComparison.Ordinal);
            Assert.DoesNotContain("Gamma", aiService.LastRequest.SystemPrompt, StringComparison.Ordinal);
            Assert.DoesNotContain("Example", aiService.LastRequest.UserPrompt, StringComparison.Ordinal);
        }

        [Fact]
        public async Task ResultWithoutQuestionsIsRejectedBeforeCharging()
        {
            coreProviderProxy.ExamResult.Data!.Questions = [];

            var result = await CreateService().PrepareAsync(Request());

            Assert.Equal(OperationResult.NotValid, result.OperationResult);
            Assert.Equal("ExamAnalysisResultUnavailable", Assert.Single(result.Errors!).Message);
            Assert.Equal(0, contentPurchaseService.ChargeCalls);
            Assert.Equal(0, aiService.Calls);
        }

        [Theory]
        [InlineData('A', "Result: Wrong")]
        [InlineData(null, "Result: No answer")]
        public async Task PromptMarksEachQuestionWrongOrUnanswered(char? studentOption, string expected)
        {
            coreProviderProxy.ExamResult.Data!.Questions[0].StudentOption = studentOption;

            _ = await CreateService().PrepareAsync(Request());

            Assert.Contains(expected, aiService.LastRequest!.UserPrompt, StringComparison.Ordinal);
        }

        [Fact]
        public async Task AiHtmlIsSanitizedBeforeItIsStored()
        {
            aiService.CompletionResult = new(OperationResult.Succeeded)
            {
                Data = new() { Content = "<script>alert('x')</script><b>Good work!</b>" },
            };

            var result = await CreateService().PrepareAsync(Request());

            Assert.Equal("Good work!", result.Data!.Analysis!.Result);
            Assert.Equal("Good work!", (await context.Set<ExamAnalysis>().SingleAsync()).Result);
        }

        [Fact]
        public void AnalysisIsAnAuthenticatedPostUnderExamsThatTakesOnlyTheExamId()
        {
            var action = typeof(ExamsController).GetMethod(nameof(ExamsController.AnalyzeExam))!;
            var route = action.GetCustomAttribute<HttpPostAttribute>();
            var parameter = Assert.Single(action.GetParameters());

            Assert.NotNull(typeof(ExamsController).GetCustomAttribute<PermissionAttribute>());
            Assert.Null(action.GetCustomAttribute<Microsoft.AspNetCore.Authorization.AllowAnonymousAttribute>());
            Assert.Equal("{id:long}/analysis", route!.Template);
            Assert.Equal(typeof(long), parameter.ParameterType);
            Assert.NotNull(parameter.GetCustomAttribute<FromRouteAttribute>());
        }

        [Fact]
        public void ExamAnalysisUsesItsOwnSubscriptionFeatureAndTransactionType()
        {
            var method = typeof(GameService).GetMethod("MapContentType", BindingFlags.NonPublic | BindingFlags.Static);

            var mapping = Assert.IsType<ValueTuple<string, TransactionType>>(method!.Invoke(null, [ContentType.ExamAnalysis]));

            Assert.Equal(FeatureCodes.ExamAnalysis, mapping.Item1);
            Assert.Equal(TransactionType.AnalyzeExam, mapping.Item2);
        }

        private ExamAnalysisService CreateService() => new(
            new(serviceProvider.GetRequiredService<IUnitOfWorkProvider>),
            new(serviceProvider.GetRequiredService<IHttpContextAccessor>),
            new(() => new KeyLocalizer()),
            new(() => NullLogger<ExamAnalysisService>.Instance),
            new(() => contentPurchaseService),
            new(() => coreProvider),
            new(() => aiService),
            new(() => applicationSettingsService));

        private static PrepareExamAnalysisRequestDto Request() => new()
        {
            UserId = UserId,
            ExamId = ExamId,
            SecretKey = SecretKey,
        };

        private async Task<ExamAnalysis> AddAnalysisAsync(string? result)
        {
            var analysis = new ExamAnalysis
            {
                UserId = UserId,
                ExamId = ExamId,
                Result = result,
                Points = ApplicationSettingsDto.DefaultExamAnalysisPoints,
                CreationDate = DateTimeOffset.UtcNow,
            };
            _ = context.Set<ExamAnalysis>().Add(analysis);
            _ = await context.SaveChangesAsync();
            context.ChangeTracker.Clear();
            return analysis;
        }

        private sealed class FakeContentPurchaseService : IContentPurchaseService
        {
            public ResultData<ContentChargeResponseDto> ChargeResult { get; set; } = new(OperationResult.Succeeded)
            {
                Data = new()
                {
                    Points = ApplicationSettingsDto.DefaultExamAnalysisPoints,
                    Spend = new() { Spent = true, PaidBy = SpendSource.Points },
                },
            };
            public Exception? CompleteException { get; set; }
            public ContentPurchaseRequestDto? LastChargeRequest { get; private set; }
            public int ChargeCalls { get; private set; }
            public int CompleteCalls { get; private set; }
            public int RefundCalls { get; private set; }

            public Task<ResultData<ContentChargeResponseDto>> ChargeAsync([NotNull] ContentPurchaseRequestDto requestDto)
            {
                ChargeCalls++;
                LastChargeRequest = requestDto;
                return Task.FromResult(ChargeResult);
            }

            public Task CompletePurchaseAsync([NotNull] ContentPurchaseRequestDto requestDto, [NotNull] ContentChargeResponseDto charge)
            {
                CompleteCalls++;
                return CompleteException is null ? Task.CompletedTask : Task.FromException(CompleteException);
            }

            public Task RefundAsync([NotNull] ContentPurchaseRequestDto requestDto, [NotNull] ContentChargeResponseDto charge)
            {
                RefundCalls++;
                return Task.CompletedTask;
            }

            public Task<ResultData<IReadOnlyCollection<string>>> GetPurchasedVariantsAsync(long userId,
                PurchasableContentType contentType, long contentId) => throw new NotSupportedException();
        }

        private sealed class FakeApplicationSettingsService : IApplicationSettingsService
        {
            public ResultData<ApplicationSettingsDto> SettingsResult { get; set; } = new(OperationResult.Succeeded)
            {
                Data = new(),
            };

            public Task<ResultData<ApplicationSettingsDto>> GetApplicationSettingsAsync() => Task.FromResult(SettingsResult);

            public Task<ResultData<T?>> GetSettingAsync<T>(string key) => throw new NotSupportedException();

            public Task<ResultData<bool>> ModifyApplicationSettingsAsync(ApplicationSettingsDto settingsDto) => throw new NotSupportedException();
        }

        private sealed class FakeAiService : IAiService
        {
            public ResultData<ChatCompletionResponseDto> CompletionResult { get; set; } = new(OperationResult.Succeeded)
            {
                Data = new() { Content = "AI analysis" },
            };

            public int Calls { get; private set; }
            public ChatCompletionRequestDto? LastRequest { get; private set; }

            public Task<ResultData<ChatCompletionResponseDto>> CompleteAsync([NotNull] ChatCompletionRequestDto requestDto)
            {
                Calls++;
                LastRequest = requestDto;
                return Task.FromResult(CompletionResult);
            }
        }

        private sealed class KeyLocalizer : IStringLocalizer<ExamAnalysisService>
        {
            public LocalizedString this[string name] => new(name, name);
            public LocalizedString this[string name, params object[] arguments] => new(name, name);
            public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures) => [];
        }

        public class CoreProviderProxy : DispatchProxy
        {
            public ResultData<ExamResultDetailResponseDto> ExamResult { get; set; } = new(OperationResult.Succeeded)
            {
                Data = new()
                {
                    Stats = new() { Total = 1, Valid = 1, Percent = 100 },
                    StudentFirstName = "Sara",
                    ExamTitle = "Arithmetic",
                    ExamTime = "45",
                    TimeTakenSeconds = 725,
                    Ranks = [new("total", 3, 120)],
                    Questions =
                    [
                        new()
                        {
                            Question = "<p>Add the numbers.</p><p>2 + 2 = ?</p>",
                            Type = "fourchoice",
                            Options = [new("3", null), new("<p>4</p>", null), new("5", null), new("6", null)],
                            CorrectOption = 'B',
                            StudentOption = 'B',
                            Topics = "Addition",
                        },
                    ],
                },
            };

            public int GetExamResultDetailCalls { get; private set; }

            protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
            {
                if (targetMethod?.Name == nameof(ICoreProvider.GetExamResultDetailAsync))
                {
                    GetExamResultDetailCalls++;
                    return Task.FromResult(ExamResult);
                }

                throw new NotSupportedException(targetMethod?.Name);
            }
        }

        public sealed class TestApplicationDBContext(IServiceProvider serviceProvider) : ApplicationDBContext(serviceProvider)
        {
            public bool FailOnSave { get; set; }

            public override int SaveChanges()
                => FailOnSave ? throw new InvalidOperationException("Save failed") : base.SaveChanges();

            public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
                => FailOnSave ? Task.FromException<int>(new InvalidOperationException("Save failed")) : base.SaveChangesAsync(cancellationToken);
        }
    }
}
