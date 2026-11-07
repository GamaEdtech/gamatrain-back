namespace GamaEdtech.Test.Application
{
    using System.Collections.ObjectModel;
    using System.Reflection;

    using GamaEdtech.Application.Interface;
    using GamaEdtech.Application.Service;
    using GamaEdtech.Common.Data;
    using GamaEdtech.Common.DataAccess.Context;
    using GamaEdtech.Common.DataAccess.Repositories;
    using GamaEdtech.Common.DataAccess.UnitOfWork;
    using GamaEdtech.Common.Identity;
    using GamaEdtech.Data.Dto.Subscription;
    using GamaEdtech.Data.Dto.TwoFactor;
    using GamaEdtech.Domain.Entity;
    using GamaEdtech.Domain.Enumeration;
    using GamaEdtech.Presentation.ViewModel.Subscription;

    using Microsoft.AspNetCore.Authorization;
    using Microsoft.AspNetCore.Http;
    using Microsoft.AspNetCore.Mvc;
    using Microsoft.Extensions.Configuration;
    using Microsoft.Extensions.DependencyInjection;
    using Microsoft.Extensions.Localization;
    using Microsoft.Extensions.Logging.Abstractions;

    using Xunit;

    using AdminSubscriptionsController = GamaEdtech.Presentation.Api.Areas.Admin.Controllers.SubscriptionsController;

    using static GamaEdtech.Common.Core.Constants;

    /// <summary>
    /// Admin refund of one usage event (POST admin/subscriptions/usage/{id}/refund): step-up code first, a charge can be
    /// refunded once, and the credit goes through the same RefundQuotaAsync as the failed-download refund.
    /// </summary>
    public sealed class UsageRefundTests : IDisposable
    {
        private const long AdminId = 5;
        private const long UserId = 10;
        private const int FeatureId = 1;
        private const string FeatureCode = "PastpaperDownload";

        private readonly ExamAnalysisServiceTests.TestApplicationDBContext context;
        private readonly ServiceProvider serviceProvider;

        public UsageRefundTests()
        {
            Environment.SetEnvironmentVariable("Test", "True");

            ServiceCollection services = new();
            _ = services.AddLogging();
            _ = services.AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["EnableAudit"] = bool.FalseString,
            }).Build());
            _ = services.AddSingleton<IHttpContextAccessor, HttpContextAccessor>();
            _ = services.AddSingleton<ExamAnalysisServiceTests.TestApplicationDBContext>();
            _ = services.AddSingleton<IEntityContext>(provider => provider.GetRequiredService<ExamAnalysisServiceTests.TestApplicationDBContext>());
            _ = services.AddTransient(typeof(IRepository<,>), typeof(GenericEntityRepository<,>));
            _ = services.AddSingleton<IUnitOfWorkProvider, UnitOfWorkProvider>();

            serviceProvider = services.BuildServiceProvider();
            context = serviceProvider.GetRequiredService<ExamAnalysisServiceTests.TestApplicationDBContext>();
        }

        public void Dispose()
        {
            context.Dispose();
            serviceProvider.Dispose();
            GC.SuppressFinalize(this);
        }

        [Fact]
        public async Task WrongTwoFactorCodeRefundsNothing()
        {
            // Database and quota service throw if touched: a refused code must stop before any read or write.
            var service = CreateService(new FakeTwoFactorService(accept: false), out _, touchDatabase: false);

            var result = await service.RefundUsageEventAsync(1, AdminId, "000000");

            Assert.Equal(OperationResult.NotValid, result.OperationResult);
            Assert.Equal("InvalidTwoFactorCode", Assert.Single(result.Errors!).Message);
        }

        [Fact]
        public async Task ChargeIsRefundedThroughTheSharedRefundWithItsLinkAndTheAdmin()
        {
            var subscriptionId = await AddSubscriptionAsync(active: true);
            var chargeId = await AddLogAsync(subscriptionId, amount: 3, identifierId: 77);
            var service = CreateService(new FakeTwoFactorService(accept: true), out var quota);

            var result = await service.RefundUsageEventAsync(chargeId, AdminId, "123456");

            Assert.Equal(OperationResult.Succeeded, result.OperationResult);
            Assert.Equal(3, result.Data);
            var request = Assert.Single(quota.Requests);
            Assert.Equal(UserId, request.UserId);
            Assert.Equal(subscriptionId, request.UserSubscriptionId);
            Assert.Equal(FeatureCode, request.FeatureCode);
            Assert.Equal(3, request.Amount);
            Assert.Equal(77, request.IdentifierId);
            Assert.Equal(chargeId, request.RefundOfLogId);
            Assert.Equal(AdminId, request.RefundedByUserId);
        }

        [Fact]
        public async Task UnknownEventIsNotFound()
        {
            var service = CreateService(new FakeTwoFactorService(accept: true), out var quota);

            var result = await service.RefundUsageEventAsync(999, AdminId, "123456");

            Assert.Equal(OperationResult.NotFound, result.OperationResult);
            Assert.Empty(quota.Requests);
        }

        [Fact]
        public async Task RefundRowCannotBeRefunded()
        {
            var subscriptionId = await AddSubscriptionAsync(active: true);
            var chargeId = await AddLogAsync(subscriptionId, amount: 1);
            var refundId = await AddLogAsync(subscriptionId, amount: -1, refundOfLogId: chargeId);
            var service = CreateService(new FakeTwoFactorService(accept: true), out var quota);

            var result = await service.RefundUsageEventAsync(refundId, AdminId, "123456");

            Assert.Equal(OperationResult.NotValid, result.OperationResult);
            Assert.Equal("UsageEventNotRefundable", Assert.Single(result.Errors!).Message);
            Assert.Empty(quota.Requests);
        }

        [Fact]
        public async Task ChargeAlreadyRefundedIsNotRefundedAgain()
        {
            // Covers the failed-download refund too: it links its row to the charge, so an admin can't add a second.
            var subscriptionId = await AddSubscriptionAsync(active: true);
            var chargeId = await AddLogAsync(subscriptionId, amount: 1);
            _ = await AddLogAsync(subscriptionId, amount: -1, refundOfLogId: chargeId);
            var service = CreateService(new FakeTwoFactorService(accept: true), out var quota);

            var result = await service.RefundUsageEventAsync(chargeId, AdminId, "123456");

            Assert.Equal(OperationResult.NotValid, result.OperationResult);
            Assert.Equal("UsageEventAlreadyRefunded", Assert.Single(result.Errors!).Message);
            Assert.Empty(quota.Requests);
        }

        [Fact]
        public async Task ChargeOnAnInactiveSubscriptionIsNotRefunded()
        {
            var subscriptionId = await AddSubscriptionAsync(active: false);
            var chargeId = await AddLogAsync(subscriptionId, amount: 1);
            var service = CreateService(new FakeTwoFactorService(accept: true), out var quota);

            var result = await service.RefundUsageEventAsync(chargeId, AdminId, "123456");

            Assert.Equal(OperationResult.NotValid, result.OperationResult);
            Assert.Equal("UsageEventSubscriptionNotActive", Assert.Single(result.Errors!).Message);
            Assert.Empty(quota.Requests);
        }

        [Fact]
        public async Task NothingCreditedIsReportedNotSucceeded()
        {
            var subscriptionId = await AddSubscriptionAsync(active: true);
            var chargeId = await AddLogAsync(subscriptionId, amount: 2);
            var service = CreateService(new FakeTwoFactorService(accept: true), out var quota);
            quota.Credit = 0;

            var result = await service.RefundUsageEventAsync(chargeId, AdminId, "123456");

            Assert.Equal(OperationResult.NotValid, result.OperationResult);
            Assert.Equal("UsageEventNothingToRefund", Assert.Single(result.Errors!).Message);
        }

        [Fact]
        public void EndpointIsAdminOnlyPostOnTheEventAndTakesTheCallersCode()
        {
            var controller = typeof(AdminSubscriptionsController);
            Assert.Equal([nameof(Role.Admin)], controller.GetCustomAttribute<PermissionAttribute>()!.Roles!);

            var action = controller.GetMethod(nameof(AdminSubscriptionsController.RefundUsageEvent))!;
            Assert.Equal("usage/{id:long}/refund", action.GetCustomAttribute<HttpPostAttribute>()!.Template);
            Assert.Null(action.GetCustomAttribute<AllowAnonymousAttribute>());
            var parameters = action.GetParameters();
            Assert.NotNull(parameters[0].GetCustomAttribute<FromRouteAttribute>());
            Assert.NotNull(parameters[1].GetCustomAttribute<FromBodyAttribute>());
            Assert.NotNull(typeof(RefundUsageEventRequestViewModel).GetProperty(nameof(RefundUsageEventRequestViewModel.TwoFactorCode))!
                .GetCustomAttribute<Common.DataAnnotation.RequiredAttribute>());
        }

        private SubscriptionService CreateService(ITwoFactorService twoFactor, out QuotaServiceProxy quota, bool touchDatabase = true)
        {
            var quotaService = DispatchProxy.Create<ISubscriptionQuotaService, QuotaServiceProxy>();
            quota = (QuotaServiceProxy)quotaService;
            Lazy<IUnitOfWorkProvider> unitOfWorkProvider = touchDatabase
                ? new(serviceProvider.GetRequiredService<IUnitOfWorkProvider>)
                : new(() => throw new InvalidOperationException("database touched"));
            Lazy<ISubscriptionQuotaService> quotaLazy = touchDatabase
                ? new(() => quotaService)
                : new(() => throw new InvalidOperationException("quota service touched"));

            return new(
                unitOfWorkProvider,
                new(() => throw new InvalidOperationException("http context touched")),
                new(() => new KeyLocalizer()),
                new(() => NullLogger<SubscriptionService>.Instance),
                new(() => throw new InvalidOperationException("payment service touched")),
                new(() => throw new InvalidOperationException("configuration touched")),
                quotaLazy,
                new(() => throw new InvalidOperationException("gateway touched")),
                new(() => throw new InvalidOperationException("identity touched")),
                new(() => throw new InvalidOperationException("email touched")),
                new(() => throw new InvalidOperationException("settings touched")),
                new(() => throw new InvalidOperationException("data protection touched")),
                new(() => twoFactor));
        }

        private async Task<long> AddSubscriptionAsync(bool active)
        {
            if (!context.Set<Feature>().Any())
            {
                _ = context.Set<Feature>().Add(new() { Id = FeatureId, Code = FeatureCode, Name = FeatureCode, Description = FeatureCode, IsActive = true });
            }

            var subscription = new UserSubscription
            {
                UserId = UserId,
                SubscriptionPlanId = 1,
                Status = active ? UserSubscriptionStatus.Active : UserSubscriptionStatus.Expired,
                CreationDate = DateTimeOffset.UtcNow,
                ExpirationDate = active ? DateTimeOffset.UtcNow.AddDays(10) : DateTimeOffset.UtcNow.AddDays(-1),
                Currency = Currency.USD,
                BillingInterval = BillingInterval.Monthly,
            };
            _ = context.Set<UserSubscription>().Add(subscription);
            _ = await context.SaveChangesAsync();
            context.ChangeTracker.Clear();
            return subscription.Id;
        }

        private async Task<long> AddLogAsync(long subscriptionId, int amount, long? identifierId = null, long? refundOfLogId = null)
        {
            var log = new SubscriptionQuotaConsumptionLog
            {
                UserId = UserId,
                UserSubscriptionId = subscriptionId,
                FeatureId = FeatureId,
                Amount = amount,
                IdentifierId = identifierId,
                RefundOfLogId = refundOfLogId,
                CreationDate = DateTimeOffset.UtcNow,
            };
            _ = context.Set<SubscriptionQuotaConsumptionLog>().Add(log);
            _ = await context.SaveChangesAsync();
            context.ChangeTracker.Clear();
            return log.Id;
        }

        private sealed class KeyLocalizer : IStringLocalizer<SubscriptionService>
        {
            public LocalizedString this[string name] => new(name, name);
            public LocalizedString this[string name, params object[] arguments] => new(name, name);
            public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures) => [];
        }

        private sealed class FakeTwoFactorService(bool accept) : ITwoFactorService
        {
            public Task<ResultData<bool>> VerifyCodeAsync(long userId, string? code) => Task.FromResult(accept && userId == AdminId
                ? new ResultData<bool>(OperationResult.Succeeded) { Data = true }
                : new ResultData<bool>(OperationResult.NotValid) { Data = false, Errors = [new() { Message = "InvalidTwoFactorCode" }] });

            public Task<ResultData<TwoFactorStatusDto>> GetStatusAsync(long userId) => throw new NotSupportedException();
            public Task<ResultData<string>> SendSetupEmailCodeAsync(long userId) => throw new NotSupportedException();
            public Task<ResultData<AuthenticatorSetupDto>> BeginSetupAsync(long userId, string? emailCode) => throw new NotSupportedException();
            public Task<ResultData<bool>> EnableAsync(long userId, string? code) => throw new NotSupportedException();
            public Task<ResultData<bool>> DisableAsync(long userId, string? code) => throw new NotSupportedException();
            public Task<ResultData<bool>> ResetForUserAsync(long adminUserId, string? adminCode, long targetUserId) => throw new NotSupportedException();
        }

        public class QuotaServiceProxy : DispatchProxy
        {
            public Collection<RefundQuotaRequestDto> Requests { get; } = [];

            /// <summary>What the fake RefundQuotaAsync reports as credited; -1 means the full requested amount.</summary>
            public int Credit { get; set; } = -1;

            protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
            {
                if (targetMethod?.Name == nameof(ISubscriptionQuotaService.RefundQuotaAsync))
                {
                    ArgumentNullException.ThrowIfNull(args);
                    var request = (RefundQuotaRequestDto)args[0]!;
                    Requests.Add(request);
                    return Task.FromResult(new ResultData<int>(OperationResult.Succeeded) { Data = Credit < 0 ? request.Amount : Credit });
                }

                throw new NotSupportedException(targetMethod?.Name);
            }
        }
    }
}
