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
    using Microsoft.EntityFrameworkCore;
    using Microsoft.Extensions.Configuration;
    using Microsoft.Extensions.DependencyInjection;
    using Microsoft.Extensions.Localization;
    using Microsoft.Extensions.Logging.Abstractions;

    using Xunit;

    using AdminSubscriptionsController = GamaEdtech.Presentation.Api.Areas.Admin.Controllers.SubscriptionsController;

    using static GamaEdtech.Common.Core.Constants;

    /// <summary>
    /// A feature an admin adds to a plan reaches existing subscribers only through SyncPlanFeaturesAsync (2026-10-30:
    /// ExamAnalysis was pooled into the plans' download quota after people had already subscribed).
    /// </summary>
    public sealed class SubscriptionPlanFeatureSyncTests : IDisposable
    {
        private const long PlanId = 1;
        private const long OtherPlanId = 2;
        private const int PastpaperDownload = 1;
        private const int ExamDownload = 6;
        private const int ExamAnalysis = 7;
        private const int TestSubmission = 3;
        private const int Multimedia = 5;
        private const string DownloadsGroup = "downloads";
        private const string NewDescription = "Download Past Papers and Exam Files, plus AI Exam Analysis";

        private readonly ExamAnalysisServiceTests.TestApplicationDBContext context;
        private readonly ServiceProvider serviceProvider;

        public SubscriptionPlanFeatureSyncTests()
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
        public async Task FeatureAddedToAGroupJoinsTheSubscribersExistingBucket()
        {
            await SeedPlanAsync();
            var subscriptionId = await AddSubscriptionAsync(PlanId, BillingInterval.Monthly, used: 40, PastpaperDownload, ExamDownload);

            var result = await CreateQuotaService().SyncPlanFeaturesAsync(PlanId);

            Assert.Equal(OperationResult.Succeeded, result.OperationResult);
            Assert.Equal(1, result.Data!.SubscriptionCount);
            Assert.Equal(1, result.Data.FeatureCount);
            Assert.Equal(0, result.Data.QuotaCount);

            var quota = Assert.Single(await QuotasAsync(subscriptionId));
            Assert.Equal(1000, quota.Limit);
            Assert.Equal(40, quota.Used);
            Assert.Equal(NewDescription, quota.Description);
            Assert.Equal([PastpaperDownload, ExamDownload, ExamAnalysis], quota.Features.Select(f => f.FeatureId).Order());
        }

        [Fact]
        public async Task NewStandaloneFeatureGetsItsOwnBucketAtThePlanLimit()
        {
            await SeedPlanAsync();
            AddPlanFeature(PlanId, TestSubmission, BillingInterval.Monthly, limit: null, groupKey: null);
            _ = await context.SaveChangesAsync();
            var subscriptionId = await AddSubscriptionAsync(PlanId, BillingInterval.Monthly, used: 0, PastpaperDownload, ExamDownload, ExamAnalysis);

            var result = await CreateQuotaService().SyncPlanFeaturesAsync(PlanId);

            Assert.Equal(1, result.Data!.SubscriptionCount);
            Assert.Equal(1, result.Data.QuotaCount);
            var quota = (await QuotasAsync(subscriptionId)).Single(q => q.Features.Any(f => f.FeatureId == TestSubmission));
            Assert.Null(quota.Limit);
            Assert.Equal(0, quota.Used);
            _ = Assert.Single(quota.Features);
        }

        [Fact]
        public async Task SecondRunChangesNothing()
        {
            await SeedPlanAsync();
            _ = await AddSubscriptionAsync(PlanId, BillingInterval.Monthly, used: 0, PastpaperDownload, ExamDownload);
            var service = CreateQuotaService();
            _ = await service.SyncPlanFeaturesAsync(PlanId);

            var second = await service.SyncPlanFeaturesAsync(PlanId);

            Assert.Equal(OperationResult.Succeeded, second.OperationResult);
            Assert.Equal(0, second.Data!.SubscriptionCount);
            Assert.Equal(0, second.Data.FeatureCount);
            Assert.Equal(0, second.Data.QuotaCount);
        }

        [Fact]
        public async Task OnlyActiveSubscriptionsOnThePlanAreSyncedAtTheirOwnInterval()
        {
            await SeedPlanAsync();
            var annual = await AddSubscriptionAsync(PlanId, BillingInterval.Annual, used: 0);
            var cancelled = await AddSubscriptionAsync(PlanId, BillingInterval.Monthly, used: 0, status: UserSubscriptionStatus.Cancelled);
            var otherPlan = await AddSubscriptionAsync(OtherPlanId, BillingInterval.Monthly, used: 0);

            var result = await CreateQuotaService().SyncPlanFeaturesAsync(PlanId);

            Assert.Equal(1, result.Data!.SubscriptionCount);
            Assert.Equal(8000, Assert.Single(await QuotasAsync(annual)).Limit);
            Assert.Empty(await QuotasAsync(cancelled));
            Assert.Empty(await QuotasAsync(otherPlan));
        }

        [Fact]
        public async Task UnknownPlanIsNotFound()
        {
            var result = await CreateQuotaService().SyncPlanFeaturesAsync(99);

            Assert.Equal(OperationResult.NotFound, result.OperationResult);
        }

        [Fact]
        public async Task WrongTwoFactorCodeSyncsNothing()
        {
            // The quota service throws if touched: a refused code must stop before any read or write.
            var twoFactor = new FakeTwoFactorService(accept: false);
            var service = CreateSubscriptionService(twoFactor, new(() => throw new InvalidOperationException("quota service touched")));

            var result = await service.SyncPlanFeaturesAsync(PlanId, adminUserId: 5, twoFactorCode: "000000");

            Assert.Equal(OperationResult.NotValid, result.OperationResult);
            Assert.Null(result.Data);
            Assert.Equal("InvalidTwoFactorCode", Assert.Single(result.Errors!).Message);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        public async Task MissingTwoFactorCodeIsCheckedLikeAnyOther(string? code)
        {
            var twoFactor = new FakeTwoFactorService(accept: false);
            var service = CreateSubscriptionService(twoFactor, new(() => throw new InvalidOperationException("quota service touched")));

            var result = await service.SyncPlanFeaturesAsync(PlanId, adminUserId: 5, twoFactorCode: code);

            Assert.NotEqual(OperationResult.Succeeded, result.OperationResult);
            Assert.Equal(1, twoFactor.Calls);
            Assert.Equal(code, twoFactor.LastCode);
        }

        [Fact]
        public async Task AcceptedCodeIsTheCallingAdminsAndThenSyncsThatPlan()
        {
            var twoFactor = new FakeTwoFactorService(accept: true);
            var quotaService = DispatchProxy.Create<ISubscriptionQuotaService, QuotaServiceProxy>();
            var proxy = (QuotaServiceProxy)quotaService;
            var service = CreateSubscriptionService(twoFactor, new(() => quotaService));

            var result = await service.SyncPlanFeaturesAsync(PlanId, adminUserId: 5, twoFactorCode: "123456");

            Assert.Equal(OperationResult.Succeeded, result.OperationResult);
            Assert.Equal(5, twoFactor.LastUserId);
            Assert.Equal("123456", twoFactor.LastCode);
            Assert.Equal([PlanId], proxy.SyncedPlanIds);
            Assert.Equal(3, result.Data!.SubscriptionCount);
        }

        [Fact]
        public void EndpointIsAdminOnlyPostOnThePlanAndPassesTheCallersCode()
        {
            var controller = typeof(AdminSubscriptionsController);
            var permission = controller.GetCustomAttribute<PermissionAttribute>();
            Assert.Equal([nameof(Role.Admin)], permission!.Roles!);

            var action = controller.GetMethod(nameof(AdminSubscriptionsController.SyncPlanFeatures))!;
            Assert.Equal("plans/{id:long}/sync-features", action.GetCustomAttribute<HttpPostAttribute>()!.Template);
            Assert.Null(action.GetCustomAttribute<AllowAnonymousAttribute>());
            var parameters = action.GetParameters();
            Assert.NotNull(parameters[0].GetCustomAttribute<FromRouteAttribute>());
            Assert.NotNull(parameters[1].GetCustomAttribute<FromBodyAttribute>());
            Assert.NotNull(typeof(SyncPlanFeaturesRequestViewModel).GetProperty(nameof(SyncPlanFeaturesRequestViewModel.TwoFactorCode))!
                .GetCustomAttribute<Common.DataAnnotation.RequiredAttribute>());
        }

        [Fact]
        public async Task NewPoolOfSeveralFeaturesBecomesOneSharedBucket()
        {
            await SeedPlanAsync();
            AddPlanFeature(PlanId, TestSubmission, BillingInterval.Monthly, 50, "tests", "Tests and multimedia");
            AddPlanFeature(PlanId, Multimedia, BillingInterval.Monthly, 50, "tests", "Tests and multimedia");
            _ = await context.SaveChangesAsync();
            var subscriptionId = await AddSubscriptionAsync(PlanId, BillingInterval.Monthly, used: 0, PastpaperDownload, ExamDownload, ExamAnalysis);

            var result = await CreateQuotaService().SyncPlanFeaturesAsync(PlanId);

            Assert.Equal(1, result.Data!.QuotaCount);
            Assert.Equal(2, result.Data.FeatureCount);
            var pool = (await QuotasAsync(subscriptionId)).Single(q => q.Features.Any(f => f.FeatureId == TestSubmission));
            Assert.Equal(50, pool.Limit);
            Assert.Equal(0, pool.Used);
            Assert.Equal("Tests and multimedia", pool.Description);
            Assert.Equal([TestSubmission, Multimedia], pool.Features.Select(f => f.FeatureId).Order());
        }

        [Fact]
        public async Task OtherBucketsAreLeftExactlyAsTheyWere()
        {
            await SeedPlanAsync();
            AddPlanFeature(PlanId, TestSubmission, BillingInterval.Monthly, limit: 20, groupKey: null);
            _ = await context.SaveChangesAsync();
            var subscriptionId = await AddSubscriptionAsync(PlanId, BillingInterval.Monthly, used: 900, PastpaperDownload, ExamDownload);
            await AddBucketAsync(subscriptionId, limit: 3, used: 3, "Old tests text", TestSubmission);

            _ = await CreateQuotaService().SyncPlanFeaturesAsync(PlanId);

            // The tests bucket's lower limit (3, plan now says 20), exhausted usage and old text all stay.
            var tests = (await QuotasAsync(subscriptionId)).Single(q => q.Features.Any(f => f.FeatureId == TestSubmission));
            Assert.Equal(3, tests.Limit);
            Assert.Equal(3, tests.Used);
            Assert.Equal("Old tests text", tests.Description);
            var downloads = (await QuotasAsync(subscriptionId)).Single(q => q.Features.Any(f => f.FeatureId == ExamDownload));
            Assert.Equal(900, downloads.Used);
            Assert.Equal(1000, downloads.Limit);
        }

        [Fact]
        public async Task FeatureRemovedFromThePlanStaysWithTheSubscriber()
        {
            await SeedPlanAsync();
            var subscriptionId = await AddSubscriptionAsync(PlanId, BillingInterval.Monthly, used: 0, PastpaperDownload, ExamDownload, ExamAnalysis, Multimedia);

            var result = await CreateQuotaService().SyncPlanFeaturesAsync(PlanId);

            Assert.Equal(0, result.Data!.FeatureCount);
            Assert.Contains(Multimedia, Assert.Single(await QuotasAsync(subscriptionId)).Features.Select(f => f.FeatureId));
        }

        [Fact]
        public async Task GroupSplitAcrossTwoBucketsAddsToTheOldestOnly()
        {
            await SeedPlanAsync();
            var subscriptionId = await AddSubscriptionAsync(PlanId, BillingInterval.Monthly, used: 10, PastpaperDownload);
            await AddBucketAsync(subscriptionId, limit: 200, used: 20, "Exam files", ExamDownload);

            var result = await CreateQuotaService().SyncPlanFeaturesAsync(PlanId);

            Assert.Equal(1, result.Data!.FeatureCount);
            Assert.Equal(0, result.Data.QuotaCount);
            var quotas = (await QuotasAsync(subscriptionId)).OrderBy(q => q.Id).ToList();
            Assert.Equal([PastpaperDownload, ExamAnalysis], quotas[0].Features.Select(f => f.FeatureId).Order());
            Assert.Equal([ExamDownload], quotas[1].Features.Select(f => f.FeatureId));
            Assert.Equal(200, quotas[1].Limit);
        }

        [Fact]
        public async Task InactiveFeatureIsNotGiven()
        {
            await SeedPlanAsync();
            var feature = await context.Set<Feature>().SingleAsync(f => f.Id == ExamAnalysis);
            feature.IsActive = false;
            _ = await context.SaveChangesAsync();
            context.ChangeTracker.Clear();
            var subscriptionId = await AddSubscriptionAsync(PlanId, BillingInterval.Monthly, used: 0, PastpaperDownload, ExamDownload);

            var result = await CreateQuotaService().SyncPlanFeaturesAsync(PlanId);

            Assert.Equal(0, result.Data!.FeatureCount);
            Assert.DoesNotContain(ExamAnalysis, Assert.Single(await QuotasAsync(subscriptionId)).Features.Select(f => f.FeatureId));
        }

        [Fact]
        public async Task IntervalThePlanHasNoLimitsForGetsNothing()
        {
            await SeedPlanAsync();
            var subscriptionId = await AddSubscriptionAsync(PlanId, BillingInterval.Quarterly, used: 0, PastpaperDownload);

            var result = await CreateQuotaService().SyncPlanFeaturesAsync(PlanId);

            Assert.Equal(0, result.Data!.SubscriptionCount);
            _ = Assert.Single(Assert.Single(await QuotasAsync(subscriptionId)).Features);
        }

        [Fact]
        public async Task EverySubscriberIsCountedOnce()
        {
            await SeedPlanAsync();
            var ids = new List<long>();
            for (var i = 0; i < 3; i++)
            {
                ids.Add(await AddSubscriptionAsync(PlanId, BillingInterval.Monthly, used: i, PastpaperDownload, ExamDownload));
            }

            var result = await CreateQuotaService().SyncPlanFeaturesAsync(PlanId);

            Assert.Equal(3, result.Data!.SubscriptionCount);
            Assert.Equal(3, result.Data.FeatureCount);
            for (var i = 0; i < 3; i++)
            {
                var quota = Assert.Single(await QuotasAsync(ids[i]));
                Assert.Equal(i, quota.Used);
                Assert.Contains(ExamAnalysis, quota.Features.Select(f => f.FeatureId));
            }
        }

        [Fact]
        public async Task PlanWithoutSubscribersSucceedsWithNothingToDo()
        {
            await SeedPlanAsync();

            var result = await CreateQuotaService().SyncPlanFeaturesAsync(PlanId);

            Assert.Equal(OperationResult.Succeeded, result.OperationResult);
            Assert.Equal(0, result.Data!.SubscriptionCount);
        }

        [Fact]
        public async Task SavedQuotaConsumptionStillWorksForTheAttachedFeature()
        {
            // The attached feature must be found by the same lookup ConsumeQuotaAsync uses (bucket features by code).
            await SeedPlanAsync();
            var subscriptionId = await AddSubscriptionAsync(PlanId, BillingInterval.Monthly, used: 0, PastpaperDownload, ExamDownload);
            _ = await CreateQuotaService().SyncPlanFeaturesAsync(PlanId);
            context.ChangeTracker.Clear();

            var bucket = await context.Set<UserSubscriptionQuota>().AsNoTracking()
                .Where(q => q.UserSubscriptionId == subscriptionId && q.Features.Any(f => f.Feature!.Code == "ExamAnalysis"))
                .SingleAsync();

            Assert.Equal(1000, bucket.Limit);
        }

        private SubscriptionQuotaService CreateQuotaService() => new(
            new(serviceProvider.GetRequiredService<IUnitOfWorkProvider>),
            new(serviceProvider.GetRequiredService<IHttpContextAccessor>),
            new(() => new KeyLocalizer()),
            new(() => NullLogger<SubscriptionQuotaService>.Instance),
            new(() => throw new InvalidOperationException("gateway touched")));

        /// <summary>Plan 1: Pastpaper + Exam download + Exam analysis pooled, Monthly 1000 / Annual 8000.</summary>
        private async Task SeedPlanAsync()
        {
            _ = context.Set<SubscriptionPlan>().Add(new() { Id = PlanId, Title = "ALPHA", IsActive = true });
            _ = context.Set<SubscriptionPlan>().Add(new() { Id = OtherPlanId, Title = "BETA", IsActive = true });
            foreach (var (id, code) in new[] { (PastpaperDownload, "PastpaperDownload"), (ExamDownload, "ExamDownload"), (ExamAnalysis, "ExamAnalysis"), (TestSubmission, "TestSubmission"), (Multimedia, "MultimediaDownload") })
            {
                _ = context.Set<Feature>().Add(new() { Id = id, Code = code, Name = code, Description = code, IsActive = true });
            }

            foreach (var featureId in new[] { PastpaperDownload, ExamDownload, ExamAnalysis })
            {
                AddPlanFeature(PlanId, featureId, BillingInterval.Monthly, 1000, DownloadsGroup);
                AddPlanFeature(PlanId, featureId, BillingInterval.Annual, 8000, DownloadsGroup);
            }

            _ = await context.SaveChangesAsync();
        }

        private void AddPlanFeature(long planId, int featureId, BillingInterval interval, int? limit, string? groupKey, string? groupDescription = NewDescription)
            => _ = context.Set<SubscriptionPlanFeature>().Add(new()
            {
                SubscriptionPlanId = planId,
                FeatureId = featureId,
                BillingInterval = interval,
                Limit = limit,
                FeatureGroupKey = groupKey,
                FeatureGroupDescription = groupKey is null ? null : groupDescription,
            });

        private async Task AddBucketAsync(long subscriptionId, int? limit, int used, string description, params int[] featureIds)
        {
            var quota = new UserSubscriptionQuota { UserSubscriptionId = subscriptionId, Limit = limit, Used = used, Description = description };
            foreach (var featureId in featureIds)
            {
                quota.Features.Add(new() { UserSubscriptionId = subscriptionId, FeatureId = featureId });
            }

            _ = context.Set<UserSubscriptionQuota>().Add(quota);
            _ = await context.SaveChangesAsync();
            context.ChangeTracker.Clear();
        }

        private static SubscriptionService CreateSubscriptionService(ITwoFactorService twoFactor, Lazy<ISubscriptionQuotaService> quotaService) => new(
            new(() => throw new InvalidOperationException("database touched")),
            new(() => throw new InvalidOperationException("http context touched")),
            new(() => throw new InvalidOperationException("localizer touched")),
            new(() => throw new InvalidOperationException("logger touched")),
            new(() => throw new InvalidOperationException("payment service touched")),
            new(() => throw new InvalidOperationException("configuration touched")),
            quotaService,
            new(() => throw new InvalidOperationException("gateway touched")),
            new(() => throw new InvalidOperationException("identity touched")),
            new(() => throw new InvalidOperationException("email touched")),
            new(() => throw new InvalidOperationException("settings touched")),
            new(() => throw new InvalidOperationException("data protection touched")),
            new(() => twoFactor));

        /// <summary>A subscription with one bucket covering <paramref name="featureIds"/> (none when empty).</summary>
        private async Task<long> AddSubscriptionAsync(long planId, BillingInterval interval, int used, params int[] featureIds)
            => await AddSubscriptionAsync(planId, interval, used, UserSubscriptionStatus.Active, featureIds);

        private async Task<long> AddSubscriptionAsync(long planId, BillingInterval interval, int used, UserSubscriptionStatus status, params int[] featureIds)
        {
            var subscription = new UserSubscription
            {
                UserId = 10,
                SubscriptionPlanId = planId,
                Status = status,
                CreationDate = DateTimeOffset.UtcNow,
                Currency = Currency.USD,
                BillingInterval = interval,
            };
            _ = context.Set<UserSubscription>().Add(subscription);
            _ = await context.SaveChangesAsync();

            if (featureIds.Length > 0)
            {
                var quota = new UserSubscriptionQuota
                {
                    UserSubscriptionId = subscription.Id,
                    Limit = interval == BillingInterval.Annual ? 8000 : 1000,
                    Used = used,
                    Description = "Download Past Papers, Multimedia, and Exam Files",
                };
                foreach (var featureId in featureIds)
                {
                    quota.Features.Add(new() { UserSubscriptionId = subscription.Id, FeatureId = featureId });
                }

                _ = context.Set<UserSubscriptionQuota>().Add(quota);
                _ = await context.SaveChangesAsync();
            }

            context.ChangeTracker.Clear();
            return subscription.Id;
        }

        private async Task<List<UserSubscriptionQuota>> QuotasAsync(long subscriptionId)
        {
            context.ChangeTracker.Clear();
            return await context.Set<UserSubscriptionQuota>().AsNoTracking()
                .Include(q => q.Features)
                .Where(q => q.UserSubscriptionId == subscriptionId)
                .ToListAsync();
        }

        private sealed class KeyLocalizer : IStringLocalizer<SubscriptionQuotaService>
        {
            public LocalizedString this[string name] => new(name, name);
            public LocalizedString this[string name, params object[] arguments] => new(name, name);
            public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures) => [];
        }

        private sealed class FakeTwoFactorService(bool accept) : ITwoFactorService
        {
            public int Calls { get; private set; }
            public long? LastUserId { get; private set; }
            public string? LastCode { get; private set; }

            public Task<ResultData<bool>> VerifyCodeAsync(long userId, string? code)
            {
                Calls++;
                LastUserId = userId;
                LastCode = code;
                return Task.FromResult(accept
                    ? new ResultData<bool>(OperationResult.Succeeded) { Data = true }
                    : new ResultData<bool>(OperationResult.NotValid) { Data = false, Errors = [new() { Message = "InvalidTwoFactorCode" }] });
            }

            public Task<ResultData<TwoFactorStatusDto>> GetStatusAsync(long userId) => throw new NotSupportedException();
            public Task<ResultData<string>> SendSetupEmailCodeAsync(long userId) => throw new NotSupportedException();
            public Task<ResultData<AuthenticatorSetupDto>> BeginSetupAsync(long userId, string? emailCode) => throw new NotSupportedException();
            public Task<ResultData<bool>> EnableAsync(long userId, string? code) => throw new NotSupportedException();
            public Task<ResultData<bool>> DisableAsync(long userId, string? code) => throw new NotSupportedException();
            public Task<ResultData<bool>> ResetForUserAsync(long adminUserId, string? adminCode, long targetUserId) => throw new NotSupportedException();
        }

        public class QuotaServiceProxy : DispatchProxy
        {
            public Collection<long> SyncedPlanIds { get; } = [];

            protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
            {
                if (targetMethod?.Name == nameof(ISubscriptionQuotaService.SyncPlanFeaturesAsync))
                {
                    ArgumentNullException.ThrowIfNull(args);
                    SyncedPlanIds.Add((long)args[0]!);
                    return Task.FromResult(new ResultData<SyncPlanFeaturesResponseDto>(OperationResult.Succeeded) { Data = new() { SubscriptionCount = 3 } });
                }

                throw new NotSupportedException(targetMethod?.Name);
            }
        }
    }
}
