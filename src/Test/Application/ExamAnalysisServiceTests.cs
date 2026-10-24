namespace GamaEdtech.Test.Application
{
    using System.Reflection;

    using GamaEdtech.Application.Interface;
    using GamaEdtech.Application.Service;
    using GamaEdtech.Common.Data;
    using GamaEdtech.Common.DataAccess;
    using GamaEdtech.Common.DataAccess.Context;
    using GamaEdtech.Common.DataAccess.Repositories;
    using GamaEdtech.Common.DataAccess.UnitOfWork;
    using GamaEdtech.Common.Identity;
    using GamaEdtech.Data.Dto.ContentPurchase;
    using GamaEdtech.Data.Dto.ExamAnalysis;
    using GamaEdtech.Domain.Entity;
    using GamaEdtech.Domain.Enumeration;
    using GamaEdtech.Infrastructure.EntityFramework.Context;
    using GamaEdtech.Presentation.Api.Controllers;
    using GamaEdtech.Presentation.ViewModel.ExamAnalysis;

    using Microsoft.AspNetCore.Http;
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

        private readonly TestApplicationDBContext context;
        private readonly FakeContentPurchaseService contentPurchaseService = new();
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
        }

        public void Dispose()
        {
            serviceProvider.Dispose();
            GC.SuppressFinalize(this);
        }

        [Fact]
        public async Task CompletedAnalysisIsReturnedWithoutChargingAgain()
        {
            _ = await AddAnalysisAsync(UserId, ExamId, "existing result");

            var result = await CreateService().PrepareAsync(new() { UserId = UserId, ExamId = ExamId });

            Assert.Equal(OperationResult.Succeeded, result.OperationResult);
            Assert.Equal("existing result", result.Data!.Analysis!.Result);
            Assert.False(result.Data.RequiresAnalysis);
            Assert.False(result.Data.PaymentSucceeded);
            Assert.Equal(0, contentPurchaseService.ChargeCalls);
        }

        [Fact]
        public async Task PendingAnalysisIsReturnedWithoutChargingAgain()
        {
            _ = await AddAnalysisAsync(UserId, ExamId, null);

            var result = await CreateService().PrepareAsync(new() { UserId = UserId, ExamId = ExamId });

            Assert.Equal(OperationResult.Succeeded, result.OperationResult);
            Assert.True(result.Data!.RequiresAnalysis);
            Assert.False(result.Data.PaymentSucceeded);
            Assert.Equal(0, contentPurchaseService.ChargeCalls);
        }

        [Fact]
        public async Task SuccessfulChargeCreatesPendingAnalysisAndCompletesPurchase()
        {
            contentPurchaseService.ChargeResult = SuccessfulCharge();

            var result = await CreateService().PrepareAsync(new() { UserId = UserId, ExamId = ExamId });

            Assert.Equal(OperationResult.Succeeded, result.OperationResult);
            Assert.True(result.Data!.RequiresAnalysis);
            Assert.True(result.Data.PaymentSucceeded);
            Assert.Equal(1, contentPurchaseService.ChargeCalls);
            Assert.Equal(1, contentPurchaseService.CompleteCalls);
            Assert.Equal(0, contentPurchaseService.RefundCalls);
            Assert.Equal(UserId, contentPurchaseService.LastChargeRequest!.UserId);
            Assert.Equal(ExamId, contentPurchaseService.LastChargeRequest.ContentId);
            Assert.Equal(ExamAnalysisPricing.Price, contentPurchaseService.LastChargeRequest.Points);
            Assert.Equal(PurchasableContentType.ExamAnalysis, contentPurchaseService.LastChargeRequest.ContentType);
            Assert.Equal(ContentType.Exam, contentPurchaseService.LastChargeRequest.SpendContentType);

            var analysis = Assert.Single(await context.Set<ExamAnalysis>().ToListAsync());
            Assert.Equal(UserId, analysis.UserId);
            Assert.Equal(ExamId, analysis.ExamId);
            Assert.Equal(ExamAnalysisPricing.Price, analysis.Points);
            Assert.Null(analysis.Result);
        }

        [Fact]
        public async Task SubscriptionQuotaChargeIsReportedAsSuccessful()
        {
            contentPurchaseService.ChargeResult = new(OperationResult.Succeeded)
            {
                Data = new()
                {
                    Points = ExamAnalysisPricing.Price,
                    Spend = new() { Spent = true, PaidBy = SpendSource.SubscriptionQuota, RemainingQuota = 4 },
                },
            };

            var result = await CreateService().PrepareAsync(new() { UserId = UserId, ExamId = ExamId });

            Assert.Equal(OperationResult.Succeeded, result.OperationResult);
            Assert.True(result.Data!.PaymentSucceeded);
            Assert.Equal(SpendSource.SubscriptionQuota, result.Data.Charge!.PaidBy);
            Assert.Equal(4, result.Data.Charge.RemainingQuota);
        }

        [Fact]
        public async Task ExistingPurchaseWithoutAnalysisCreatesPendingRowWithoutASecondCharge()
        {
            contentPurchaseService.ChargeResult = new(OperationResult.Succeeded)
            {
                Data = new() { Points = ExamAnalysisPricing.Price, AlreadyPurchased = true },
            };

            var result = await CreateService().PrepareAsync(new() { UserId = UserId, ExamId = ExamId });

            Assert.Equal(OperationResult.Succeeded, result.OperationResult);
            Assert.True(result.Data!.RequiresAnalysis);
            Assert.False(result.Data.PaymentSucceeded);
            Assert.Null(result.Data.Charge);
            Assert.Single(await context.Set<ExamAnalysis>().ToListAsync());
        }

        [Fact]
        public async Task RefusedChargeDoesNotCreateAnalysis()
        {
            contentPurchaseService.ChargeResult = new(OperationResult.Failed)
            {
                Errors = [new() { Message = "Insufficient balance" },],
                Data = new() { Points = ExamAnalysisPricing.Price, Refused = new() { Spent = false } },
            };

            var result = await CreateService().PrepareAsync(new() { UserId = UserId, ExamId = ExamId });

            Assert.Equal(OperationResult.Failed, result.OperationResult);
            Assert.NotNull(result.Data!.Charge);
            Assert.Empty(await context.Set<ExamAnalysis>().ToListAsync());
            Assert.Equal(0, contentPurchaseService.CompleteCalls);
            Assert.Equal(0, contentPurchaseService.RefundCalls);
        }

        [Fact]
        public async Task SuccessfulChargeIsRefundedWhenPendingAnalysisCannotBeSaved()
        {
            contentPurchaseService.ChargeResult = SuccessfulCharge();
            context.FailOnSave = true;

            var result = await CreateService().PrepareAsync(new() { UserId = UserId, ExamId = ExamId });

            Assert.Equal(OperationResult.Failed, result.OperationResult);
            Assert.Equal(1, contentPurchaseService.ChargeCalls);
            Assert.Equal(0, contentPurchaseService.CompleteCalls);
            Assert.Equal(1, contentPurchaseService.RefundCalls);
        }

        [Fact]
        public async Task PendingAnalysisAcceptsItsFirstResult()
        {
            _ = await AddAnalysisAsync(UserId, ExamId, null);

            var result = await CreateService().SaveResultAsync(new() { UserId = UserId, ExamId = ExamId, Result = "AI result" });

            Assert.Equal(OperationResult.Succeeded, result.OperationResult);
            Assert.Equal("AI result", result.Data!.Result);
            Assert.Equal("AI result", (await context.Set<ExamAnalysis>().SingleAsync()).Result);
        }

        [Fact]
        public async Task ExistingResultIsNotOverwritten()
        {
            _ = await AddAnalysisAsync(UserId, ExamId, "first result");

            var result = await CreateService().SaveResultAsync(new() { UserId = UserId, ExamId = ExamId, Result = "second result" });

            Assert.Equal(OperationResult.Succeeded, result.OperationResult);
            Assert.Equal("first result", result.Data!.Result);
            Assert.Equal("first result", (await context.Set<ExamAnalysis>().SingleAsync()).Result);
        }

        [Fact]
        public async Task AnotherUserCannotSaveTheResult()
        {
            _ = await AddAnalysisAsync(UserId + 1, ExamId, null);

            var result = await CreateService().SaveResultAsync(new() { UserId = UserId, ExamId = ExamId, Result = "AI result" });

            Assert.Equal(OperationResult.NotFound, result.OperationResult);
            Assert.Null(result.Data);
        }

        [Fact]
        public async Task EmptyResultIsRejectedBeforeAccessingTheDatabase()
        {
            var service = new ExamAnalysisService(
                new Lazy<IUnitOfWorkProvider>(() => throw new InvalidOperationException("Database accessed")),
                new(() => throw new InvalidOperationException("HTTP context accessed")),
                new(() => new KeyLocalizer()),
                new(() => NullLogger<ExamAnalysisService>.Instance),
                new(() => contentPurchaseService));

            var result = await service.SaveResultAsync(new() { UserId = UserId, ExamId = ExamId, Result = " " });

            Assert.Equal(OperationResult.NotValid, result.OperationResult);
        }

        [Fact]
        public void ControllerRequiresAuthenticationAndRequestDoesNotExposePaymentOrUserId()
        {
            var permission = typeof(ExamAnalysesController).GetCustomAttribute<PermissionAttribute>();
            var properties = typeof(PrepareExamAnalysisRequestViewModel).GetProperties().Select(property => property.Name).ToArray();

            Assert.NotNull(permission);
            Assert.DoesNotContain("UserId", properties);
            Assert.DoesNotContain("Points", properties);
        }

        private ExamAnalysisService CreateService() => new(
            new(() => serviceProvider.GetRequiredService<IUnitOfWorkProvider>()),
            new(() => serviceProvider.GetRequiredService<IHttpContextAccessor>()),
            new(() => new KeyLocalizer()),
            new(() => NullLogger<ExamAnalysisService>.Instance),
            new(() => contentPurchaseService));

        private async Task<ExamAnalysis> AddAnalysisAsync(long userId, long examId, string? result)
        {
            var analysis = new ExamAnalysis
            {
                UserId = userId,
                ExamId = examId,
                Result = result,
                Points = ExamAnalysisPricing.Price,
                CreationDate = DateTimeOffset.UtcNow,
            };
            _ = context.Set<ExamAnalysis>().Add(analysis);
            _ = await context.SaveChangesAsync();
            context.ChangeTracker.Clear();
            return analysis;
        }

        private static ResultData<ContentChargeResponseDto> SuccessfulCharge() => new(OperationResult.Succeeded)
        {
            Data = new()
            {
                Points = ExamAnalysisPricing.Price,
                Spend = new() { Spent = true, PaidBy = SpendSource.Points },
            },
        };

        private sealed class FakeContentPurchaseService : IContentPurchaseService
        {
            public ResultData<ContentChargeResponseDto> ChargeResult { get; set; } = SuccessfulCharge();
            public ContentPurchaseRequestDto? LastChargeRequest { get; private set; }
            public int ChargeCalls { get; private set; }
            public int CompleteCalls { get; private set; }
            public int RefundCalls { get; private set; }

            public Task<ResultData<ContentChargeResponseDto>> ChargeAsync(ContentPurchaseRequestDto requestDto)
            {
                ChargeCalls++;
                LastChargeRequest = requestDto;
                return Task.FromResult(ChargeResult);
            }

            public Task CompletePurchaseAsync(ContentPurchaseRequestDto requestDto, ContentChargeResponseDto charge)
            {
                CompleteCalls++;
                return Task.CompletedTask;
            }

            public Task RefundAsync(ContentPurchaseRequestDto requestDto, ContentChargeResponseDto charge)
            {
                RefundCalls++;
                return Task.CompletedTask;
            }

            public Task<ResultData<IReadOnlyCollection<string>>> GetPurchasedVariantsAsync(long userId,
                PurchasableContentType contentType, long contentId) => throw new NotSupportedException();
        }

        private sealed class KeyLocalizer : IStringLocalizer<ExamAnalysisService>
        {
            public LocalizedString this[string name] => new(name, name);
            public LocalizedString this[string name, params object[] arguments] => new(name, name);
            public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures) => [];
        }

        private sealed class TestApplicationDBContext(IServiceProvider serviceProvider) : ApplicationDBContext(serviceProvider)
        {
            public bool FailOnSave { get; set; }

            public override int SaveChanges()
                => FailOnSave ? throw new InvalidOperationException("Save failed") : base.SaveChanges();

            public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
                => FailOnSave ? Task.FromException<int>(new InvalidOperationException("Save failed")) : base.SaveChangesAsync(cancellationToken);
        }
    }
}
