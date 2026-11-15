namespace GamaEdtech.Test.Application
{
    using System.Reflection;

    using GamaEdtech.Application.Service;
    using GamaEdtech.Common.Data;
    using GamaEdtech.Common.DataAccess.Context;
    using GamaEdtech.Common.DataAccess.Repositories;
    using GamaEdtech.Common.DataAccess.UnitOfWork;
    using GamaEdtech.Common.Service.Factory;
    using GamaEdtech.Data.Dto.Taxonomy;
    using GamaEdtech.Domain.Entity;
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
        private readonly Dictionary<string, ResultData<IReadOnlyList<TaxonomyItemDto>>> gamaApi = new(StringComparer.Ordinal);

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

            var legacy = GamaApiFake.Create(gamaApi);
            sync = new(
                new(serviceProvider.GetRequiredService<IUnitOfWorkProvider>),
                new(serviceProvider.GetRequiredService<IHttpContextAccessor>),
                new(() => NullLogger<LegacyTaxonomySyncService>.Instance),
                new(() => new GenericFactory<ITaxonomySource, MigrationRoute>([legacy, native])));
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
        public async Task SyncCopiesTheWholeTreeWithGamaApisIds()
        {
            GamaApiTree();

            var result = await sync.SyncAsync();

            Assert.True(result.OperationResult == OperationResult.Succeeded, string.Join("; ", result.Errors?.Select(t => t.Message) ?? []));
            Assert.Equal(6, result.Data!.Added);
            Assert.Equal(
                [6042, 6529, 6627, 6635, 9303, 10401],
                await context.Set<LegacyTaxonomyItem>().OrderBy(t => t.Id).Select(t => t.Id).ToListAsync());
            Assert.Equal([10401], (await native.GetTypeItemsAsync(new() { Type = "topic", LessonId = Maths })).Data!.Select(t => t.Id));
            Assert.True((await native.GetTypeItemsAsync(new() { Type = "topic", LessonId = Maths })).Data![0].IsSeason);
        }

        [Fact]
        public async Task SyncUpdatesChangesAndMarksWhatGamaApiDroppedAsDeleted()
        {
            GamaApiTree();
            _ = await sync.SyncAsync();

            // The topic is renamed and moved; the paper type is gone.
            gamaApi[$"topic||||{Maths}"] = List(Item(10401, LegacyTaxonomyKinds.Topic, "Numbers", 2, $"{Maths}+6530", season: true));
            gamaApi["exam_type||||"] = List();
            context.ChangeTracker.Clear();

            var result = await sync.SyncAsync();

            Assert.Equal((1, 1, 0), (result.Data!.Updated, result.Data.Deleted, result.Data.Added));
            context.ChangeTracker.Clear();
            var topic = await context.Set<LegacyTaxonomyItem>().Include(t => t.Parents).SingleAsync(t => t.Id == 10401);
            Assert.Equal("Numbers", topic.Title);
            Assert.Equal([$"{Maths}", "6530"], topic.Parents!.Select(t => t.ParentKey).Order(StringComparer.Ordinal));
            Assert.True((await context.Set<LegacyTaxonomyItem>().SingleAsync(t => t.Id == 6042)).IsDeleted);
            Assert.Empty((await native.GetTypeItemsAsync(new() { Type = "exam_type" })).Data!);
        }

        [Fact]
        public async Task AFailedReadChangesNothing()
        {
            GamaApiTree();
            _ = await sync.SyncAsync();
            gamaApi[$"topic||||{Maths}"] = new(OperationResult.Failed) { Errors = [new() { Message = "gama-api is down" }] };
            context.ChangeTracker.Clear();

            var result = await sync.SyncAsync();

            Assert.Equal(OperationResult.Failed, result.OperationResult);
            context.ChangeTracker.Clear();
            Assert.False((await context.Set<LegacyTaxonomyItem>().SingleAsync(t => t.Id == 10401)).IsDeleted);
        }

        public void Dispose()
        {
            context.Dispose();
            serviceProvider.Dispose();
        }

        private static ResultData<IReadOnlyList<TaxonomyItemDto>> List(params TaxonomyItemDto[] items) => new(OperationResult.Succeeded) { Data = items };

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

        // gama-api's lists, keyed by the request's fields: type|section|base|course|lesson.
        private void GamaApiTree()
        {
            gamaApi["exam_type||||"] = List(Item(6042, LegacyTaxonomyKinds.Paper, "Paper 1", 1));
            gamaApi["test_type||||"] = List(Item(9303, LegacyTaxonomyKinds.Classification, "MCQ", 1, $"{Cambridge}"));
            gamaApi["section||||"] = List(Item(Cambridge, LegacyTaxonomyKinds.Board, "Cambridge", 0));
            gamaApi[$"test_type|{Cambridge}|||"] = List(Item(9303, LegacyTaxonomyKinds.Classification, "MCQ", 1, $"{Cambridge}"));
            gamaApi[$"course|{Cambridge}|||"] = List();
            gamaApi[$"base|{Cambridge}|||"] = List(Item(Primary, LegacyTaxonomyKinds.Grade, " Primary", 1, $"{Cambridge}"));
            gamaApi[$"lesson||{Primary}||"] = List(Item(Maths, LegacyTaxonomyKinds.Subject, "Checkpoint Mathematics (0096)", 1, $"0,{Primary}"));
            gamaApi[$"topic||||{Maths}"] = List(Item(10401, LegacyTaxonomyKinds.Topic, "Number", 1, $"{Maths}", season: true));
        }

        /// <summary>gama-api's types source: answers the list the test set up for the request, and nothing else.</summary>
        public class GamaApiFake : DispatchProxy
        {
            private Dictionary<string, ResultData<IReadOnlyList<TaxonomyItemDto>>> lists = [];

            public static ITaxonomySource Create(Dictionary<string, ResultData<IReadOnlyList<TaxonomyItemDto>>> lists)
            {
                var fake = Create<ITaxonomySource, GamaApiFake>();
                ((GamaApiFake)(object)fake).lists = lists;
                return fake;
            }

            protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
            {
                ArgumentNullException.ThrowIfNull(targetMethod);
                if (targetMethod.Name == "get_ProviderType")
                {
                    return MigrationRoute.Legacy;
                }

                if (targetMethod.Name != nameof(ITaxonomySource.GetTypeItemsAsync))
                {
                    throw new NotSupportedException(targetMethod.Name);
                }

                ArgumentNullException.ThrowIfNull(args);
                var request = (TaxonomyListRequestDto)args[0]!;
                var key = $"{request.Type}|{request.SectionId}|{request.BaseId}|{request.CourseId}|{request.LessonId}";
                return Task.FromResult(lists.TryGetValue(key, out var list)
                    ? list
                    : new ResultData<IReadOnlyList<TaxonomyItemDto>>(OperationResult.Failed) { Errors = [new() { Message = $"unexpected {key}" }] });
            }
        }
    }
}
