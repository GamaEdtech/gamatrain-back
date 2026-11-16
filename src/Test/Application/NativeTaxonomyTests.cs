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
    using GamaEdtech.Data.Dto.File;
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
        private readonly Collection<Dictionary<string, object?>> typeSlave = [];
        private readonly Collection<Dictionary<string, object?>> typesRelation = [];
        private readonly Collection<Dictionary<string, object?>> lessonsPic = [];
        private readonly HashSet<string> storedPictures = new(StringComparer.Ordinal);
        private readonly Collection<string> columnsRead = [];
        private bool readFails;
        private bool configured = true;
        private MigrationRoute typesWrites = MigrationRoute.Legacy;

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
                new(() => TypeSlaveFake.Create(typeSlave, typesRelation, lessonsPic, columnsRead, () => readFails, () => configured)),
                new(() => PicturesFake.Create(storedPictures)),
                new(() => new PassThroughMigrationRouter { WriteRoute = typesWrites }));
        }

        // A new service per route, as the router is read once.
        private LegacyTaxonomyAdminService Admin() => new(
            new(serviceProvider.GetRequiredService<IUnitOfWorkProvider>),
            new(serviceProvider.GetRequiredService<IHttpContextAccessor>),
            new(() => NullLogger<LegacyTaxonomyAdminService>.Instance),
            new(() => new PassThroughMigrationRouter { WriteRoute = typesWrites }));

        [Fact]
        public async Task AdminsEditTheListsOnlyOnceTheyAreEditedHere()
        {
            Seed(new TaxonomyItemDto { Id = Cambridge, Kind = LegacyTaxonomyKinds.Board, Title = "Cambridge", Parent = "" });
            TaxonomyItemEditDto topic = new() { Kind = LegacyTaxonomyKinds.Topic, Title = "**Season 1", ListOrder = 2, Parent = "4164+4165", Metadata = """{"season":true}""" };

            // While gama-api is where the lists change, nothing is edited here.
            Assert.Equal("notNative", (await Admin().CreateItemAsync(topic)).Errors!.Single().Reference);

            typesWrites = MigrationRoute.Native;
            var admin = Admin();
            var created = await admin.CreateItemAsync(topic);
            Assert.Equal(Cambridge + 1, created.Data);
            var item = await context.Set<LegacyTaxonomyItem>().Include(t => t.Parents).SingleAsync(t => t.Id == created.Data);
            Assert.True(item.IsSeason);
            Assert.Null(item.IsPaper);
            Assert.Equal(["4164", "4165"], item.Parents!.Select(t => t.ParentKey).Order(StringComparer.Ordinal));
            context.ChangeTracker.Clear();

            // A new parent replaces the old keys; the list shows it under the new parent only.
            topic.Parent = "4166";
            Assert.True((await admin.UpdateItemAsync(created.Data, topic)).Data);
            Assert.Equal([created.Data], (await admin.GetItemsAsync(LegacyTaxonomyKinds.Topic, "4166", null, false, 1, 50)).Data!.Items.Select(t => t.Id));
            Assert.Empty((await admin.GetItemsAsync(LegacyTaxonomyKinds.Topic, "4164", null, false, 1, 50)).Data!.Items);

            // Bad metadata and other lists are refused; a removed item is hidden, not lost.
            Assert.Equal(OperationResult.NotValid, (await admin.UpdateItemAsync(created.Data, new() { Kind = LegacyTaxonomyKinds.Topic, Title = "x", Metadata = "[1]" })).OperationResult);
            Assert.Equal(OperationResult.NotValid, (await admin.CreateItemAsync(new() { Kind = 9, Title = "School type" })).OperationResult);
            Assert.True((await admin.RemoveItemAsync(created.Data)).Data);
            _ = Assert.Single((await admin.GetItemsAsync(LegacyTaxonomyKinds.Topic, null, null, true, 1, 50)).Data!.Items, t => t.IsDeleted);

            // And the copy from gama-api no longer runs: it would put the old lists back.
            Assert.Equal(OperationResult.NotValid, (await sync.SyncAsync()).OperationResult);
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

        private static readonly System.Text.Json.JsonSerializerOptions Unescaped = new() { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

        [Fact]
        public async Task TheWebsitesTypeListsAreGamaApisItems()
        {
            LegacyTaxonomyItem Row(int id, short kind, string title, int order, string parent, string? metadata = null, bool? isPaper = null, string? parent2 = null) => new()
            {
                Id = id,
                Kind = kind,
                Title = title,
                ListOrder = order,
                Parent = parent,
                Parent2 = parent2,
                Metadata = metadata,
                IsPaper = isPaper,
                SyncDate = DateTimeOffset.UtcNow,
                Parents = [.. LegacyTaxonomySyncService.ParentKeys(parent).Select(t => new LegacyTaxonomyItemParent { ParentKey = t })],
            };
            await context.Set<LegacyTaxonomyItem>().AddRangeAsync(
                Row(Cambridge, LegacyTaxonomyKinds.Board, "Cambridge", 0, ""),
                Row(1, LegacyTaxonomyKinds.Board, "A school board", 1, ""),
                Row(310, LegacyTaxonomyKinds.Classification, "Worksheets", 0, "6627+6657", isPaper: false),
                Row(6320, LegacyTaxonomyKinds.Classification, "Paper 1", 11, "6627", """{"is_paper":true}""", isPaper: true),
                Row(6321, LegacyTaxonomyKinds.Classification, "Paper 2", 12, "6627+6657", """{"is_paper":true}""", isPaper: true),
                Row(4190, LegacyTaxonomyKinds.Subject, "Mathematics (9709)", 70, "0,4161", """{"code":""}""", parent2: "0"),
                Row(4265, LegacyTaxonomyKinds.Topic, "**AS Level", 1, "4164", """{"season":true}"""));
            _ = await context.SaveChangesAsync();
            context.ChangeTracker.Clear();

            async Task<string> ListAsync(string query) => (await native.GetTypeListAsync(query)).Data!.ToJsonString(Unescaped);

            // As gama-api's live answers: every column as text, plus each list's own fields.
            Assert.True(System.Text.Json.Nodes.JsonNode.DeepEquals(
                System.Text.Json.Nodes.JsonNode.Parse("""[{"id":"4190","master_":"4","list_order":"70","title":"Mathematics (9709)","parent":"0,4161","parent2":"0","test_link":null,"book_link":null,"metadata":"{\"code\":\"\"}","bit_delete":"0"}]"""),
                (await native.GetTypeListAsync("type=lesson&base_id=4161")).Data));
            Assert.Equal(string.Empty, (string?)NativeTaxonomySource.ListItem(new() { Id = 6028, Kind = LegacyTaxonomyKinds.Subject, Title = "x", Parent = "0,6026" })["metadata"]);
            Assert.Equal("""[{"id":"4265","master_":"11","list_order":"1","title":"AS Level","parent":"4164","season":true}]""", await ListAsync("type=topic&lesson_id=4164"));
            Assert.Equal("""{"id":"310","master_":"6","list_order":"0","title":"Worksheets","parent":"6627+6657","is_paper":false}""",
                (await native.GetTypeListAsync("type=test_type&section_id=6627")).Data![0]!.ToJsonString(Unescaped));
            Assert.Equal("""[{"id":"6320","master_":"6","list_order":"11","title":"Paper 1","parent":"6627"}]""", await ListAsync("type=test_type&section_id=6627&is_paper=1"));

            // is_school keeps the school boards, or the rest.
            Assert.Equal("""[{"id":"1","master_":"1","list_order":"1","title":"A school board","parent":""}]""", await ListAsync("type=section&is_school=1"));
            Assert.Contains("6627", await ListAsync("type=section&is_school=0"), StringComparison.Ordinal);

            Assert.Equal(OperationResult.Failed, (await native.GetTypeListAsync("type=base")).OperationResult);
            Assert.Equal(OperationResult.NotValid, (await native.GetTypeListAsync("type=school_type")).OperationResult);
        }

        [Fact]
        public async Task SyncCopiesEveryListFromGamaApisTableWithItsMetadata()
        {
            GamaApiTable();

            var result = await sync.SyncAsync();

            Assert.True(result.OperationResult == OperationResult.Succeeded, string.Join("; ", result.Errors?.Select(t => t.Message) ?? []));
            Assert.Equal((9, 9), (result.Data!.Read, result.Data.Added));
            // gama-api's other lists (here 7, areas) are not copied.
            Assert.Equal(
                [6042, 6351, 6529, 6627, 6635, 7813, 9303, 9999, 10401],
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
            Assert.Equal(["id", "master_", "list_order", "title", "parent", "parent2", "metadata", "bit_delete", "from_id", "to_id", "lesson_slave_id", "pic"], columnsRead.Distinct());

            // Variants are a list too.
            Assert.Equal("nv", Assert.Single((await native.GetTypeItemsAsync(new() { Type = "variant" })).Data!).Title);
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
        public async Task LinksBetweenBoardsAreMirrored()
        {
            GamaApiTable();
            typesRelation.Add(Link(1, 10401, 20501));
            typesRelation.Add(Link(2, 10401, 20502));
            var first = await sync.SyncAsync();
            Assert.Equal(2, first.Data!.Relations);

            typesRelation.RemoveAt(0);
            typesRelation[0]["to_id"] = 20599L;
            typesRelation.Add(Link(3, 30001, 30002));
            context.ChangeTracker.Clear();
            _ = await sync.SyncAsync();

            context.ChangeTracker.Clear();
            Assert.Equal([(2, 10401, 20599), (3, 30001, 30002)],
                (await context.Set<LegacyTaxonomyRelation>().OrderBy(t => t.Id).ToListAsync()).Select(t => (t.Id, t.FromId, t.ToId)));
        }

        [Fact]
        public async Task ASubjectsPictureIsResolvedLikeGamaApi()
        {
            GamaApiTable();
            Add(Maths + 1, LegacyTaxonomyKinds.Subject, "Physics", 2, $"0,{Primary}");
            Add(Maths + 2, LegacyTaxonomyKinds.Subject, "Chemistry", 3, $"0,{Primary}");
            lessonsPic.Add(new() { ["id"] = 1L, ["lesson_slave_id"] = (long)Maths, ["pic"] = "lPic_a.gif" });
            lessonsPic.Add(new() { ["id"] = 2L, ["lesson_slave_id"] = (long)(Maths + 1), ["pic"] = "lPic_b.png" });
            lessonsPic.Add(new() { ["id"] = 3L, ["lesson_slave_id"] = (long)(Maths + 2), ["pic"] = "lPic_c.jpg" });
            storedPictures.UnionWith(["lPic_a.jpg", "lPic_a.webp", "lPic_b.webp"]);

            _ = await sync.SyncAsync();

            context.ChangeTracker.Clear();
            var pictures = await context.Set<LegacyTaxonomyItem>().Where(t => t.Kind == LegacyTaxonomyKinds.Subject).OrderBy(t => t.Id).Select(t => t.Picture).ToListAsync();
            // .jpg first whatever the name says, then .webp, else none.
            Assert.Equal(["lPic_a.jpg", "lPic_b.webp", null], pictures);
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

        /// <summary>One gama-api <c>type_slave</c> row.</summary>
        private void Add(int id, int master, string title, int order, string parent = "", string? metadata = null, int deleted = 0) => typeSlave.Add(new()
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

        // gama-api's type_slave rows: board, grade, subject, topic, classification, paper type, extra-file type, a deleted
        // board, and an area (a list this backend does not copy).
        private void GamaApiTable()
        {
            Add(Cambridge, LegacyTaxonomyKinds.Board, "Cambridge", 0);
            Add(9999, LegacyTaxonomyKinds.Board, "Old board", 1, deleted: 1);
            Add(Primary, LegacyTaxonomyKinds.Grade, " Primary", 1, $"{Cambridge}");
            Add(Maths, LegacyTaxonomyKinds.Subject, "Checkpoint Mathematics (0096)", 1, $"0,{Primary}", "{\"code\":\"0096\"}");
            Add(10401, LegacyTaxonomyKinds.Topic, "Number", 1, $"{Maths}", "{\"season\":1,\"preTitle\":\"UNIT 1\"}");
            Add(9303, LegacyTaxonomyKinds.Classification, "MCQ", 1, $"{Cambridge}", "{\"is_paper\":false}");
            Add(6042, LegacyTaxonomyKinds.Paper, "Paper 1", 1);
            Add(6351, LegacyTaxonomyKinds.ExtraFileType, "Insert", 1, metadata: "{\"price_multiplier\":\"0\"}");
            Add(7813, LegacyTaxonomyKinds.Variant, "nv", 1);
            Add(7001, 7, "An area", 1);
        }

        private Dictionary<string, object?> Row(int id) => typeSlave.Single(t => (long)t["id"]! == id);

        private static Dictionary<string, object?> Link(long id, long from, long to) => new() { ["id"] = id, ["from_id"] = from, ["to_id"] = to, ["subdate"] = "never read" };

        /// <summary>gama-api's <c>type_slave</c> table: rows in id order after an id, a batch at a time, only the asked columns.</summary>
        public class TypeSlaveFake : DispatchProxy
        {
            private Collection<Dictionary<string, object?>> rows = [];
            private Collection<Dictionary<string, object?>> relations = [];
            private Collection<Dictionary<string, object?>> pictures = [];
            private Collection<string> columnsRead = [];
            private Func<bool> fails = () => false;
            private Func<bool> isConfigured = () => true;

            public static ILegacyDatabaseReader Create(Collection<Dictionary<string, object?>> rows, Collection<Dictionary<string, object?>> relations, Collection<Dictionary<string, object?>> pictures, Collection<string> columnsRead, Func<bool> fails, Func<bool> isConfigured)
            {
                var fake = Create<ILegacyDatabaseReader, TypeSlaveFake>();
                ((TypeSlaveFake)(object)fake).rows = rows;
                ((TypeSlaveFake)(object)fake).relations = relations;
                ((TypeSlaveFake)(object)fake).pictures = pictures;
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
                var table = (string)args[0]!;
                Assert.True(table is "type_slave" or "types_relation" or "lessons_pic", table);
                if (fails())
                {
                    return Task.FromResult(new ResultData<IReadOnlyList<IReadOnlyDictionary<string, object?>>>(OperationResult.Failed) { Errors = [new() { Message = "gama-api's database is down" }] });
                }

                var (columns, afterId, batchSize) = ((IReadOnlyList<string>)args[1]!, (long)args[2]!, (int)args[3]!);
                foreach (var column in columns)
                {
                    columnsRead.Add(column);
                }
                var source = table switch { "type_slave" => rows, "types_relation" => relations, _ => pictures };
                IReadOnlyList<IReadOnlyDictionary<string, object?>> batch = [.. source
                    .Where(t => (long)t["id"]! > afterId)
                    .OrderBy(t => (long)t["id"]!)
                    .Take(batchSize)
                    .Select(t => (IReadOnlyDictionary<string, object?>)columns.ToDictionary(c => c, c => t[c], StringComparer.Ordinal))];
                return Task.FromResult(new ResultData<IReadOnlyList<IReadOnlyDictionary<string, object?>>>(OperationResult.Succeeded) { Data = batch });
            }
        }

        /// <summary>The subject pictures stored in the <c>SubjectPicture</c> container, listed in one page.</summary>
        public class PicturesFake : DispatchProxy
        {
            private HashSet<string> stored = [];

            public static IFileService Create(HashSet<string> stored)
            {
                var fake = Create<IFileService, PicturesFake>();
                ((PicturesFake)(object)fake).stored = stored;
                return fake;
            }

            protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
            {
                ArgumentNullException.ThrowIfNull(targetMethod);
                ArgumentNullException.ThrowIfNull(args);
                Assert.Equal(nameof(IFileService.ListFilesAsync), targetMethod.Name);
                Assert.Equal(ContainerType.SubjectPicture, ((ListFilesRequestDto)args[0]!).ContainerType);
                return Task.FromResult(new ResultData<ListFilesResponseDto>(OperationResult.Succeeded)
                {
                    Data = new() { Files = [.. stored.Order(StringComparer.Ordinal).Select(t => new StoredFileDto { Name = t, Size = 1 })] },
                });
            }
        }
    }
}
