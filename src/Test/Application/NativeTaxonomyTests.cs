namespace GamaEdtech.Test.Application
{
    using System.Collections.ObjectModel;
    using System.Reflection;

    using GamaEdtech.Application.Service;
    using GamaEdtech.Common.Data;
    using GamaEdtech.Common.DataAccess.Context;
    using GamaEdtech.Common.DataAccess.Repositories;
    using GamaEdtech.Common.DataAccess.UnitOfWork;
    using GamaEdtech.Data.Dto.Taxonomy;
    using GamaEdtech.Domain.Entity;
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
    /// The Types domain answered from this backend's copy of gama-api's lookup lists. It must give the same lists gama-api
    /// gives for the same question (same items, same order, same required parents), and the copy must follow gama-api
    /// without ever losing data because a read failed.
    /// </summary>
    public sealed class NativeTaxonomyTests : IDisposable
    {
        private const int Cambridge = 6627;
        private const int Edexcel = 6657;
        private const int Igcse = 22;
        private const int Primary = 6635;
        private const int Maths = 6529;

        private readonly ServiceProvider serviceProvider;
        private readonly TestApplicationDBContext context;
        private readonly NativeTaxonomySource native;
        private readonly LegacyTaxonomySyncService sync;
        private readonly Collection<Dictionary<string, object?>> typeSlave = [];
        private readonly Collection<string> columnsRead = [];
        private bool readFails;
        private bool configured = true;

        public NativeTaxonomyTests()
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

            native = new(
                new(serviceProvider.GetRequiredService<IUnitOfWorkProvider>),
                new(serviceProvider.GetRequiredService<IHttpContextAccessor>),
                new(() => NullLogger<NativeTaxonomySource>.Instance));

            sync = new(
                new(serviceProvider.GetRequiredService<IUnitOfWorkProvider>),
                new(serviceProvider.GetRequiredService<IHttpContextAccessor>),
                new(() => NullLogger<LegacyTaxonomySyncService>.Instance),
                new(() => TypeSlaveFake.Create(typeSlave, columnsRead, () => readFails, () => configured)));
        }

        [Theory]
        [InlineData("6627", new[] { "6627" })]
        [InlineData("6627+6657+6658", new[] { "6627", "6657", "6658" })]
        [InlineData("0,6635+3,6635", new[] { "0,6635", "3,6635" })]
        [InlineData("", new string[0])]
        [InlineData(null, new string[0])]
        public void ParentFieldSplitsLikeGamaApisFilter(string? parent, string[] keys) =>
            Assert.Equal(keys, LegacyTaxonomySyncService.ParentKeys(parent));

        [Fact]
        public async Task ListsFollowGamaApisRules()
        {
            Seed(
                Item(Cambridge, LegacyTaxonomyKinds.Board, "Cambridge", 0),
                Item(Edexcel, LegacyTaxonomyKinds.Board, "Edexcel", 1),
                Item(9999, LegacyTaxonomyKinds.Board, "Old board", 2, deleted: true),
                Item(Igcse, LegacyTaxonomyKinds.Grade, "IGCSE", 3, $"{Cambridge}"),
                Item(Primary, LegacyTaxonomyKinds.Grade, " Primary", 1, $"{Cambridge}"),
                Item(310, LegacyTaxonomyKinds.Classification, "Worksheets", 0, $"{Cambridge}+{Edexcel}"),
                Item(Maths, LegacyTaxonomyKinds.Subject, "Checkpoint Mathematics (0096)", 1, $"0,{Primary}"),
                Item(6530, LegacyTaxonomyKinds.Subject, "In a course", 1, $"5,{Primary}"),
                Item(10401, LegacyTaxonomyKinds.Topic, "Number", 1, $"{Maths}"));

            var boards = await native.GetBoardsAsync();
            Assert.Equal([new(Cambridge, "Cambridge"), new(Edexcel, "Edexcel")], boards.Data!);

            // Ordered by list order, not by id; the title is trimmed only for the MCP options, as gama-api's proxy does.
            var grades = await native.GetTypeItemsAsync(new() { Type = "base", SectionId = Cambridge });
            Assert.Equal([Primary, Igcse], grades.Data!.Select(t => t.Id));
            var gradeOptions = await native.GetExamOptionsAsync(new() { SecretKey = "x", Kind = "grade", ParentId = Cambridge });
            Assert.Equal("Primary", gradeOptions.Data!.First().Title);

            // An item with several parents is under each of them.
            _ = Assert.Single((await native.GetTypeItemsAsync(new() { Type = "test_type", SectionId = Edexcel })).Data!);

            // A subject's parent is "course,grade"; no course means course 0.
            var subjects = await native.GetExamOptionsAsync(new() { SecretKey = "x", Kind = "subject", ParentId = Primary });
            Assert.Equal([Maths], subjects.Data!.Select(t => t.Id));
            var inCourse = await native.GetExamOptionsAsync(new() { SecretKey = "x", Kind = "subject", ParentId = Primary, CourseId = 5 });
            Assert.Equal([6530], inCourse.Data!.Select(t => t.Id));

            Assert.Equal([10401], (await native.GetExamOptionsAsync(new() { SecretKey = "x", Kind = "topic", ParentId = Maths })).Data!.Select(t => t.Id));
        }

        [Theory]
        [InlineData("base")]
        [InlineData("course")]
        [InlineData("lesson")]
        [InlineData("topic")]
        public async Task AListWithoutItsParentFailsAsGamaApiDoes(string type)
        {
            var result = await native.GetTypeItemsAsync(new() { Type = type });

            Assert.Equal(OperationResult.Failed, result.OperationResult);
        }

        [Fact]
        public async Task SyncCopiesEveryListFromGamaApisTableWithItsMetadata()
        {
            GamaApiTable();

            var result = await sync.SyncAsync();

            Assert.True(result.OperationResult == OperationResult.Succeeded, string.Join("; ", result.Errors?.Select(t => t.Message) ?? []));
            Assert.Equal((8, 8), (result.Data!.Read, result.Data.Added));
            // gama-api's other lists (here 7, areas) are not copied.
            Assert.Equal(
                [6042, 6351, 6529, 6627, 6635, 9303, 9999, 10401],
                await context.Set<LegacyTaxonomyItem>().OrderBy(t => t.Id).Select(t => t.Id).ToListAsync());

            // A deleted item is copied as deleted and left out of the lists; the metadata flags are read.
            Assert.True((await context.Set<LegacyTaxonomyItem>().SingleAsync(t => t.Id == 9999)).IsDeleted);
            Assert.Equal([Cambridge], (await native.GetBoardsAsync()).Data!.Select(t => t.Key));
            var topic = Assert.Single((await native.GetTypeItemsAsync(new() { Type = "topic", LessonId = Maths })).Data!);
            Assert.True(topic.IsSeason);
            Assert.False((await context.Set<LegacyTaxonomyItem>().SingleAsync(t => t.Id == 9303)).IsPaper);

            // The extra-file types are a list too, with the price multiplier in their metadata.
            var extra = Assert.Single((await native.GetTypeItemsAsync(new() { Type = "test_extra_file" })).Data!);
            Assert.Equal(("Insert", "{\"price_multiplier\":\"0\"}"), (extra.Title, extra.Metadata));

            // Only the columns the copy needs are read.
            Assert.Equal(["id", "master_", "list_order", "title", "parent", "parent2", "metadata", "bit_delete"], columnsRead.Distinct());
        }

        [Fact]
        public async Task SyncUpdatesChangesAndFollowsGamaApisDeletions()
        {
            GamaApiTable();
            _ = await sync.SyncAsync();

            // The topic is renamed and moved; the paper type is deleted in gama-api; the extra type is removed from its table.
            Row(10401)["title"] = "Numbers";
            Row(10401)["parent"] = $"{Maths}+6530";
            Row(6042)["bit_delete"] = 1;
            _ = typeSlave.Remove(Row(6351));
            context.ChangeTracker.Clear();

            var result = await sync.SyncAsync();

            Assert.Equal((3, 0), (result.Data!.Updated + result.Data.Deleted, result.Data.Added));
            context.ChangeTracker.Clear();
            var topic = await context.Set<LegacyTaxonomyItem>().Include(t => t.Parents).SingleAsync(t => t.Id == 10401);
            Assert.Equal("Numbers", topic.Title);
            Assert.Equal([$"{Maths}", "6530"], topic.Parents!.Select(t => t.ParentKey).Order(StringComparer.Ordinal));
            Assert.True((await context.Set<LegacyTaxonomyItem>().SingleAsync(t => t.Id == 6042)).IsDeleted);
            Assert.True((await context.Set<LegacyTaxonomyItem>().SingleAsync(t => t.Id == 6351)).IsDeleted);
            Assert.Empty((await native.GetTypeItemsAsync(new() { Type = "exam_type" })).Data!);
        }

        [Fact]
        public async Task ALongTitleIsKeptUpToAThousandCharacters()
        {
            GamaApiTable();
            Row(10401)["title"] = new string('t', 1200);

            _ = await sync.SyncAsync();

            Assert.Equal(1000, (await context.Set<LegacyTaxonomyItem>().SingleAsync(t => t.Id == 10401)).Title!.Length);
        }

        [Fact]
        public async Task AFailedReadChangesNothing()
        {
            GamaApiTable();
            _ = await sync.SyncAsync();
            Row(10401)["bit_delete"] = 1;
            readFails = true;
            context.ChangeTracker.Clear();

            var result = await sync.SyncAsync();

            Assert.Equal(OperationResult.Failed, result.OperationResult);
            context.ChangeTracker.Clear();
            Assert.False((await context.Set<LegacyTaxonomyItem>().SingleAsync(t => t.Id == 10401)).IsDeleted);
        }

        [Fact]
        public async Task NothingIsReadWithoutAConfiguredDatabase()
        {
            configured = false;
            GamaApiTable();

            var result = await sync.SyncAsync();

            Assert.Equal(OperationResult.NotValid, result.OperationResult);
            Assert.Empty(context.Set<LegacyTaxonomyItem>());
        }

        public void Dispose()
        {
            context.Dispose();
            serviceProvider.Dispose();
        }

        private static TaxonomyItemDto Item(int id, short kind, string title, int order, string parent = "", bool deleted = false, bool? season = null) => new()
        {
            Id = id,
            Kind = kind,
            Title = title,
            ListOrder = order,
            Parent = parent,
            IsSeason = season,
            Metadata = deleted ? "deleted" : null,
        };

        private void Seed(params TaxonomyItemDto[] items)
        {
            foreach (var item in items)
            {
                _ = context.Set<LegacyTaxonomyItem>().Add(new()
                {
                    Id = item.Id,
                    Kind = item.Kind,
                    Title = item.Title,
                    ListOrder = item.ListOrder,
                    Parent = item.Parent,
                    IsDeleted = item.Metadata == "deleted",
                    SyncDate = DateTimeOffset.UtcNow,
                    Parents = [.. LegacyTaxonomyService(item.Parent)],
                });
            }

            _ = context.SaveChanges();
            context.ChangeTracker.Clear();

            static IEnumerable<LegacyTaxonomyItemParent> LegacyTaxonomyService(string? parent) =>
                LegacyTaxonomySyncService.ParentKeys(parent).Select(t => new LegacyTaxonomyItemParent { ParentKey = t });
        }

        // gama-api's type_slave rows: board, grade, subject, topic, classification, paper type, extra-file type, a deleted
        // board, and an area (a list this backend does not copy).
        private void GamaApiTable()
        {
            void Add(int id, int master, string title, int order, string parent = "", string? metadata = null, int deleted = 0) => typeSlave.Add(new()
            {
                ["id"] = (long)id,
                ["master_"] = master,
                ["list_order"] = order,
                ["title"] = title,
                ["parent"] = parent,
                ["parent2"] = "",
                ["metadata"] = metadata ?? "",
                ["bit_delete"] = deleted,
                ["test_link"] = "never read",
            });

            Add(Cambridge, LegacyTaxonomyKinds.Board, "Cambridge", 0);
            Add(9999, LegacyTaxonomyKinds.Board, "Old board", 1, deleted: 1);
            Add(Primary, LegacyTaxonomyKinds.Grade, " Primary", 1, $"{Cambridge}");
            Add(Maths, LegacyTaxonomyKinds.Subject, "Checkpoint Mathematics (0096)", 1, $"0,{Primary}", "{\"code\":\"0096\"}");
            Add(10401, LegacyTaxonomyKinds.Topic, "Number", 1, $"{Maths}", "{\"season\":1,\"preTitle\":\"UNIT 1\"}");
            Add(9303, LegacyTaxonomyKinds.Classification, "MCQ", 1, $"{Cambridge}", "{\"is_paper\":false}");
            Add(6042, LegacyTaxonomyKinds.Paper, "Paper 1", 1);
            Add(6351, LegacyTaxonomyKinds.ExtraFileType, "Insert", 1, metadata: "{\"price_multiplier\":\"0\"}");
            Add(7001, 7, "An area", 1);
        }

        private Dictionary<string, object?> Row(int id) => typeSlave.Single(t => (long)t["id"]! == id);

        /// <summary>gama-api's <c>type_slave</c> table: rows in id order after an id, a batch at a time, only the asked columns.</summary>
        public class TypeSlaveFake : DispatchProxy
        {
            private Collection<Dictionary<string, object?>> rows = [];
            private Collection<string> columnsRead = [];
            private Func<bool> fails = () => false;
            private Func<bool> isConfigured = () => true;

            public static ILegacyDatabaseReader Create(Collection<Dictionary<string, object?>> rows, Collection<string> columnsRead, Func<bool> fails, Func<bool> isConfigured)
            {
                var fake = Create<ILegacyDatabaseReader, TypeSlaveFake>();
                ((TypeSlaveFake)(object)fake).rows = rows;
                ((TypeSlaveFake)(object)fake).columnsRead = columnsRead;
                ((TypeSlaveFake)(object)fake).fails = fails;
                ((TypeSlaveFake)(object)fake).isConfigured = isConfigured;
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
                Assert.Equal(4, args.Length);
                Assert.Equal("type_slave", args[0]);
                if (fails())
                {
                    return Task.FromResult(new ResultData<IReadOnlyList<IReadOnlyDictionary<string, object?>>>(OperationResult.Failed) { Errors = [new() { Message = "gama-api's database is down" }] });
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
