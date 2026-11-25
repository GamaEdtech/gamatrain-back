namespace GamaEdtech.Test.Application
{
    using System.Reflection;

    using GamaEdtech.Application.Interface;
    using GamaEdtech.Application.Service;
    using GamaEdtech.Common.Caching;
    using GamaEdtech.Common.Data;
    using GamaEdtech.Common.DataAccess.Context;
    using GamaEdtech.Common.DataAccess.Repositories;
    using GamaEdtech.Common.DataAccess.UnitOfWork;
    using GamaEdtech.Data.Dto.TwoFactor;
    using GamaEdtech.Domain.Entity;
    using GamaEdtech.Domain.Enumeration;

    using Microsoft.AspNetCore.Http;
    using Microsoft.Extensions.Configuration;
    using Microsoft.Extensions.DependencyInjection;
    using Microsoft.Extensions.Logging.Abstractions;

    using Xunit;

    using static GamaEdtech.Common.Core.Constants;

    using TestApplicationDBContext = GamaEdtech.Test.Application.ExamAnalysisServiceTests.TestApplicationDBContext;

    // Sale campaigns: created disabled, enabled only with the authenticator code, shown and discounted only while live,
    // never for a subscriber, and never two at once.
    public sealed class CampaignServiceTests : IDisposable
    {
        private const long AdminId = 7;
        private const string GoodCode = "123456";
        private readonly ServiceProvider serviceProvider;
        private readonly TestApplicationDBContext context;
        private readonly CampaignService service;

        public CampaignServiceTests()
        {
            Environment.SetEnvironmentVariable("Test", "True");
            ServiceCollection services = new();
            _ = services.AddLogging();
            _ = services.AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["EnableAudit"] = bool.FalseString }).Build());
            _ = services.AddSingleton<IHttpContextAccessor, HttpContextAccessor>();
            _ = services.AddSingleton<TestApplicationDBContext>();
            _ = services.AddSingleton<IEntityContext>(provider => provider.GetRequiredService<TestApplicationDBContext>());
            _ = services.AddTransient(typeof(IRepository<,>), typeof(GenericEntityRepository<,>));
            _ = services.AddSingleton<IUnitOfWorkProvider, UnitOfWorkProvider>();
            serviceProvider = services.BuildServiceProvider();
            context = serviceProvider.GetRequiredService<TestApplicationDBContext>();
            service = new(new(serviceProvider.GetRequiredService<IUnitOfWorkProvider>), new(serviceProvider.GetRequiredService<IHttpContextAccessor>),
                new(() => NullLogger<CampaignService>.Instance), new(NoCache.Create), new(() => new FakeTwoFactorService()));
        }

        [Fact]
        public async Task ACampaignIsShownAndDiscountedOnlyOnceEnabledAndLive()
        {
            var now = DateTimeOffset.UtcNow;
            var id = (await service.ManageCampaignAsync(new() { Title = "Black Friday", Theme = CampaignTheme.BlackFriday, DiscountPercent = 30, StartsAt = now.AddHours(-1), EndsAt = now.AddDays(3), UserId = AdminId })).Data;

            // Created disabled: nothing shows, nothing is discounted.
            Assert.Null((await service.GetPublicCampaignAsync()).Data);
            Assert.Null(await service.GetCheckoutCampaignAsync(1));

            // Enabling needs the caller's authenticator code.
            Assert.Equal(OperationResult.NotValid, (await service.SetEnabledAsync(id, true, AdminId, "000000")).OperationResult);
            Assert.True((await service.SetEnabledAsync(id, true, AdminId, GoodCode)).Data);

            var shown = (await service.GetPublicCampaignAsync()).Data!;
            Assert.Equal((id, true, 30, CampaignTheme.BlackFriday), (shown.Id, shown.IsLive, shown.DiscountPercent, shown.Theme));
            Assert.Equal(30, (await service.GetCheckoutCampaignAsync(1))?.DiscountPercent);

            // Disabling (the emergency stop) needs no code and stops it at once.
            Assert.True((await service.SetEnabledAsync(id, false, AdminId, null)).Data);
            Assert.Null(await service.GetCheckoutCampaignAsync(1));
        }

        [Fact]
        public async Task ASubscriberGetsNoCampaignDiscount()
        {
            var now = DateTimeOffset.UtcNow;
            _ = await AddEnabledAsync("Sale", now.AddHours(-1), now.AddDays(1));
            _ = context.Set<UserSubscription>().Add(new UserSubscription
            {
                UserId = 2,
                SubscriptionPlanId = 1,
                Status = UserSubscriptionStatus.Active,
                CreationDate = now,
                ExpirationDate = now.AddDays(10),
                Currency = Currency.USD,
                BillingInterval = BillingInterval.Monthly,
            });
            _ = await context.SaveChangesAsync();

            Assert.Null(await service.GetCheckoutCampaignAsync(2));
            Assert.NotNull(await service.GetCheckoutCampaignAsync(3));
        }

        [Fact]
        public async Task AnUpcomingCampaignIsAnnouncedButNotDiscounted()
        {
            var now = DateTimeOffset.UtcNow;
            _ = await AddEnabledAsync("Christmas", now.AddDays(2), now.AddDays(5));

            var shown = (await service.GetPublicCampaignAsync()).Data!;
            Assert.False(shown.IsLive);
            Assert.Null(await service.GetCheckoutCampaignAsync(1));

            // Further than the look-ahead: not announced yet.
            context.Set<Campaign>().Single().StartsAt = now.AddDays(20);
            context.Set<Campaign>().Single().EndsAt = now.AddDays(25);
            _ = await context.SaveChangesAsync();
            Assert.Null((await service.GetPublicCampaignAsync()).Data);
        }

        [Fact]
        public async Task TwoEnabledCampaignsNeverOverlap()
        {
            var now = DateTimeOffset.UtcNow;
            _ = await AddEnabledAsync("Black Friday", now.AddDays(1), now.AddDays(5));
            var id = (await service.ManageCampaignAsync(new() { Title = "Cyber Monday", DiscountPercent = 40, StartsAt = now.AddDays(4), EndsAt = now.AddDays(7), UserId = AdminId })).Data;

            Assert.Equal("overlap", (await service.SetEnabledAsync(id, true, AdminId, GoodCode)).Errors!.Single().Reference);
        }

        [Fact]
        public async Task AnEnabledCampaignsTermsNeedTheCodeToChange()
        {
            var now = DateTimeOffset.UtcNow;
            var id = await AddEnabledAsync("Sale", now.AddDays(1), now.AddDays(3));

            // Wording alone: no code needed.
            Assert.Equal(OperationResult.Succeeded, (await service.ManageCampaignAsync(new() { Id = id, Title = "Big sale", DiscountPercent = 20, StartsAt = now.AddDays(1), EndsAt = now.AddDays(3), UserId = AdminId })).OperationResult);

            // The percent: the code is needed.
            Assert.Equal(OperationResult.NotValid, (await service.ManageCampaignAsync(new() { Id = id, Title = "Big sale", DiscountPercent = 60, StartsAt = now.AddDays(1), EndsAt = now.AddDays(3), UserId = AdminId })).OperationResult);
            Assert.Equal(OperationResult.Succeeded, (await service.ManageCampaignAsync(new() { Id = id, Title = "Big sale", DiscountPercent = 60, StartsAt = now.AddDays(1), EndsAt = now.AddDays(3), UserId = AdminId, TwoFactorCode = GoodCode })).OperationResult);
        }

        public void Dispose()
        {
            context.Dispose();
            serviceProvider.Dispose();
        }

        private async Task<long> AddEnabledAsync(string title, DateTimeOffset startsAt, DateTimeOffset endsAt)
        {
            var id = (await service.ManageCampaignAsync(new() { Title = title, DiscountPercent = 20, StartsAt = startsAt, EndsAt = endsAt, UserId = AdminId })).Data;
            Assert.True((await service.SetEnabledAsync(id, true, AdminId, GoodCode)).Data);
            return id;
        }

        /// <summary>An ICacheProvider that never caches: GetAsync runs the factory every time.</summary>
        public class NoCache : DispatchProxy
        {
            public static ICacheProvider Create() => Create<ICacheProvider, NoCache>();

            protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
            {
                ArgumentNullException.ThrowIfNull(targetMethod);
                ArgumentNullException.ThrowIfNull(args);
                return targetMethod.Name switch
                {
                    nameof(ICacheProvider.GetAsync) => args[1] is Delegate factory ? factory.DynamicInvoke() : null,
                    nameof(ICacheProvider.RemoveAsync) => Task.CompletedTask,
                    _ => throw new NotSupportedException(targetMethod.Name),
                };
            }
        }

        private sealed class FakeTwoFactorService : ITwoFactorService
        {
            public Task<ResultData<bool>> VerifyCodeAsync(long userId, string? code) => Task.FromResult(userId == AdminId && code == GoodCode
                ? new ResultData<bool>(OperationResult.Succeeded) { Data = true }
                : new ResultData<bool>(OperationResult.NotValid) { Data = false, Errors = [new() { Message = "InvalidTwoFactorCode" }] });

            public Task<ResultData<TwoFactorStatusDto>> GetStatusAsync(long userId) => throw new NotSupportedException();
            public Task<ResultData<string>> SendSetupEmailCodeAsync(long userId) => throw new NotSupportedException();
            public Task<ResultData<AuthenticatorSetupDto>> BeginSetupAsync(long userId, string? emailCode) => throw new NotSupportedException();
            public Task<ResultData<bool>> EnableAsync(long userId, string? code) => throw new NotSupportedException();
            public Task<ResultData<bool>> DisableAsync(long userId, string? code) => throw new NotSupportedException();
            public Task<ResultData<bool>> ResetForUserAsync(long adminUserId, string? adminCode, long targetUserId) => throw new NotSupportedException();
        }
    }
}
