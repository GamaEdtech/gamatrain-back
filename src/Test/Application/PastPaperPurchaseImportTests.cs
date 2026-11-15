namespace GamaEdtech.Test.Application
{
    using System.Collections.ObjectModel;
    using System.Reflection;

    using GamaEdtech.Application.Service;
    using GamaEdtech.Common.Data;
    using GamaEdtech.Common.DataAccess.Context;
    using GamaEdtech.Common.DataAccess.Repositories;
    using GamaEdtech.Common.DataAccess.UnitOfWork;
    using GamaEdtech.Domain.Entity;
    using GamaEdtech.Domain.Entity.Identity;
    using GamaEdtech.Domain.Enumeration;
    using GamaEdtech.Infrastructure.Interface;

    using Microsoft.AspNetCore.Http;
    using Microsoft.EntityFrameworkCore;
    using Microsoft.Extensions.Configuration;
    using Microsoft.Extensions.DependencyInjection;
    using Microsoft.Extensions.Logging.Abstractions;

    using Xunit;

    using static GamaEdtech.Common.Core.Constants;

    using TestApplicationDBContext = GamaEdtech.Test.Application.ExamAnalysisServiceTests.TestApplicationDBContext;

    /// <summary>
    /// Copying the past paper files bought on gama-api: only paid paper files are kept, card and IP columns are never read,
    /// each purchase becomes one ownership row once its paper and buyer are here (a buyer or paper that arrives later is
    /// picked up by the next link), and the same file bought twice is owned once. User and paper ids are made up.
    /// </summary>
    public sealed class PastPaperPurchaseImportTests : IDisposable
    {
        private readonly ServiceProvider serviceProvider;
        private readonly TestApplicationDBContext context;
        private readonly Collection<Dictionary<string, object?>> payments = [];
        private readonly Collection<string> columnsRead = [];
        private readonly PastPaperPurchaseImportService service;
        private bool configured = true;

        public PastPaperPurchaseImportTests()
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

            service = new(
                new(serviceProvider.GetRequiredService<IUnitOfWorkProvider>),
                new(serviceProvider.GetRequiredService<IHttpContextAccessor>),
                new(() => NullLogger<PastPaperPurchaseImportService>.Instance),
                new(() => PaymentsFake.Create(payments, columnsRead, () => configured)),
                new(serviceProvider.GetRequiredService<IConfiguration>));
        }

        [Fact]
        public async Task PaidPaperFilesBecomeOwnedAndNothingElseIsKept()
        {
            AddUser(10, coreId: 91001);
            AddPaper(31617, extraFileId: 5);
            Payment(1, user: 91001, usage: 31617, type: 1, status: 2);
            Payment(2, user: 91001, usage: 31617, type: 2, status: 2);
            Payment(3, user: 91001, usage: 31617, type: 8, status: 1);
            Payment(4, user: 91001, usage: 5, type: 12, status: 2);
            Payment(5, user: 91001, usage: 31617, type: 3, status: 2);   // a credit top-up, not a file
            Payment(6, user: 91001, usage: 31617, type: 1, status: -10); // never paid

            var result = await service.ImportAsync();

            Assert.Equal(OperationResult.Succeeded, result.OperationResult);
            Assert.Equal((6, 4, 4, 4), (result.Data!.PaymentsRead, result.Data.Purchases, result.Data.Added, result.Data.Owned));
            var owned = await context.Set<ContentPurchase>().OrderBy(t => t.Variant).ToListAsync();
            Assert.Equal(["Answer", "Extra:5", "Question", "QuestionWord"], owned.Select(t => t.Variant));
            Assert.All(owned, t => Assert.Equal((10L, PurchasableContentType.PastPaper, 31617L, SpendSource.GamaApi, 0L), (t.UserId, t.ContentType, t.ContentId, t.PaidBy, t.Points)));

            // Only the columns ownership needs are read; never a card number, IP or bank field.
            Assert.Equal(["id", "user_", "usage_id", "type", "status", "price", "subdate"], columnsRead.Distinct());
        }

        [Fact]
        public async Task ABuyerWhoJoinsLaterGetsTheirFiles()
        {
            AddPaper(31617);
            Payment(1, user: 91001, usage: 31617, type: 1, status: 2);

            var first = await service.ImportAsync();
            Assert.Equal((0, 1), (first.Data!.Owned, first.Data.WithoutUser));

            AddUser(11, coreId: 91001);
            var link = await service.LinkAsync();

            Assert.Equal(1, link.Data!.Owned);
            Assert.Equal(11L, (await context.Set<ContentPurchase>().SingleAsync()).UserId);
            _ = Assert.NotNull((await CopyAsync(1)).ContentPurchaseId);
        }

        [Fact]
        public async Task APaperCopiedLaterGetsItsPurchases()
        {
            AddUser(10, coreId: 91001);
            Payment(1, user: 91001, usage: 31617, type: 2, status: 2);

            var first = await service.ImportAsync();
            Assert.Equal(1, first.Data!.WithoutPaper);

            AddPaper(31617);
            var link = await service.LinkAsync();

            Assert.Equal(1, link.Data!.Owned);
            Assert.Equal("Answer", (await context.Set<ContentPurchase>().SingleAsync()).Variant);
        }

        [Fact]
        public async Task AFileBoughtTwiceIsOwnedOnceAndARerunAddsNothing()
        {
            AddUser(10, coreId: 91001);
            AddPaper(31617);
            Payment(1, user: 91001, usage: 31617, type: 1, status: 2);
            Payment(2, user: 91001, usage: 31617, type: 1, status: 2);

            var first = await service.ImportAsync();
            var second = await service.ImportAsync();

            Assert.Equal((1, 1), (first.Data!.Owned, first.Data.AlreadyOwned));
            Assert.Equal((0, 2, 0), (second.Data!.Added, second.Data.Updated, second.Data.Owned));
            var ownership = await context.Set<ContentPurchase>().SingleAsync();
            Assert.Equal([ownership.Id, ownership.Id], await context.Set<LegacyPastPaperPurchase>().OrderBy(t => t.Id).Select(t => t.ContentPurchaseId!.Value).ToListAsync());
        }

        [Fact]
        public async Task NothingIsReadWithoutAConfiguredDatabase()
        {
            configured = false;
            Payment(1, user: 91001, usage: 31617, type: 1, status: 2);

            var result = await service.ImportAsync();

            Assert.Equal(OperationResult.NotValid, result.OperationResult);
            Assert.Empty(context.Set<LegacyPastPaperPurchase>());
        }

        public void Dispose()
        {
            context.Dispose();
            serviceProvider.Dispose();
        }

        private void AddUser(long id, long coreId)
        {
            _ = context.Set<ApplicationUser>().Add(new() { Id = id, UserName = $"user{id}", NormalizedUserName = $"USER{id}", ProfileVisibility = ProfileVisibility.Private, CoreId = coreId, Enabled = true });
            _ = context.SaveChanges();
            context.ChangeTracker.Clear();
        }

        private void AddPaper(long id, long? extraFileId = null)
        {
            List<PastPaperFile> files = [new() { Kind = PastPaperFileKind.Question, Path = "q.pdf" }];
            if (extraFileId is not null)
            {
                files.Add(new() { Kind = PastPaperFileKind.Extra, Path = "x.pdf", CoreExtraFileId = extraFileId });
            }

            _ = context.Set<PastPaper>().Add(new() { Id = id, CoreOwnerId = 91999, Title = "Physics", Files = files });
            _ = context.SaveChanges();
            context.ChangeTracker.Clear();
        }

        private void Payment(long id, long user, long usage, int type, int status) => payments.Add(new()
        {
            ["id"] = id,
            ["user_"] = user,
            ["usage_id"] = usage,
            ["type"] = type,
            ["status"] = status,
            ["price"] = 50000,
            ["subdate"] = new DateTime(2025, 6, 1, 12, 0, 0, DateTimeKind.Unspecified),
            ["card_number"] = "never read",
        });

        private async Task<LegacyPastPaperPurchase> CopyAsync(long id)
        {
            context.ChangeTracker.Clear();
            return await context.Set<LegacyPastPaperPurchase>().SingleAsync(t => t.Id == id);
        }

        /// <summary>gama-api's payments table, read in id order after an id, a batch at a time, only the asked columns.</summary>
        public class PaymentsFake : DispatchProxy
        {
            private Collection<Dictionary<string, object?>> rows = [];
            private Collection<string> columnsRead = [];
            private Func<bool> isConfigured = () => true;

            public static ILegacyDatabaseReader Create(Collection<Dictionary<string, object?>> rows, Collection<string> columnsRead, Func<bool> isConfigured)
            {
                var fake = Create<ILegacyDatabaseReader, PaymentsFake>();
                ((PaymentsFake)(object)fake).rows = rows;
                ((PaymentsFake)(object)fake).columnsRead = columnsRead;
                ((PaymentsFake)(object)fake).isConfigured = isConfigured;
                return fake;
            }

            protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
            {
                ArgumentNullException.ThrowIfNull(targetMethod);
                if (targetMethod.Name == "get_IsConfigured")
                {
                    return isConfigured();
                }

                ArgumentNullException.ThrowIfNull(args);
                if (args.Length != 4)
                {
                    throw new InvalidOperationException("payments must be read with a column list.");
                }

                var (columns, afterId, batchSize) = ((IReadOnlyList<string>)args[1]!, (long)args[2]!, (int)args[3]!);
                foreach (var column in columns)
                {
                    columnsRead.Add(column);
                }
                IReadOnlyList<IReadOnlyDictionary<string, object?>> batch = [.. rows
                    .Where(t => (long)t["id"]! > afterId)
                    .OrderBy(t => (long)t["id"]!)
                    .Take(batchSize)
                    .Select(t => (IReadOnlyDictionary<string, object?>)columns.ToDictionary(c => c, c => t[c], StringComparer.Ordinal))];
                return Task.FromResult(new ResultData<IReadOnlyList<IReadOnlyDictionary<string, object?>>>(OperationResult.Succeeded) { Data = batch });
            }
        }
    }
}
