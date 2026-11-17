namespace GamaEdtech.Test.Application
{
    using GamaEdtech.Application.Service;
    using GamaEdtech.Common.DataAccess.Context;
    using GamaEdtech.Common.DataAccess.Repositories;
    using GamaEdtech.Common.DataAccess.UnitOfWork;
    using GamaEdtech.Domain.Entity;
    using GamaEdtech.Domain.Entity.Identity;
    using GamaEdtech.Domain.Enumeration;

    using Microsoft.AspNetCore.Http;
    using Microsoft.EntityFrameworkCore;
    using Microsoft.Extensions.Configuration;
    using Microsoft.Extensions.DependencyInjection;
    using Microsoft.Extensions.Logging.Abstractions;

    using Xunit;

    using static GamaEdtech.Common.Core.Constants;

    using GamaApiDbFake = GamaEdtech.Test.Application.PastPaperImportTests.GamaApiDbFake;
    using TestApplicationDBContext = GamaEdtech.Test.Application.ExamAnalysisServiceTests.TestApplicationDBContext;

    /// <summary>
    /// Copying gama-api's tutorials (<c>dars</c>) and their images' rows (<c>tutorials_files</c>): ids, topics and content are
    /// kept as gama-api has them, the owner is linked by <c>CoreId</c>, a second run updates instead of duplicating, and the
    /// copy stops once tutorials are edited here. The user and tutorial ids here are made up.
    /// </summary>
    public sealed class TutorialImportTests : IDisposable
    {
        private readonly ServiceProvider serviceProvider;
        private readonly TestApplicationDBContext context;
        private readonly Dictionary<string, List<Dictionary<string, object?>>> gamaApiDb = new(StringComparer.Ordinal);
        private bool configured = true;
        private MigrationRoute tutorialWrites = MigrationRoute.Legacy;

        public TutorialImportTests()
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

        [Fact]
        public async Task ATutorialIsCopiedWithItsTopicsContentAndOwner()
        {
            AddUser(10, coreId: 91001);
            AddUser(56, coreId: 99999);
            AddTutorial(1862, owner: 91001, topic: "10541+10543", content: """<p>Text <img src="https://core.gamatrain.com/uploads/tutorials/2026/09/1862_a.png"></p>""");
            AddTutorial(1863, owner: 91002, status: 0);
            AddImage(15411, 1862, "2026/09/1862_a.png");
            AddImage(15412, 9999, "2026/09/9999_orphan.png");

            var result = await Service(fallbackOwner: "56").ImportAsync();

            Assert.Equal(OperationResult.Succeeded, result.OperationResult);
            Assert.Equal((2, 2, 0, 1), (result.Data!.TutorialsRead, result.Data.TutorialsAdded, result.Data.TutorialsUpdated, result.Data.TutorialsWithoutUser));
            Assert.Equal((2, 1, 1), (result.Data.ImagesRead, result.Data.ImagesAdded, result.Data.ImagesSkipped));

            var tutorial = await TutorialAsync(1862);
            Assert.Equal((91001L, 10L, 10L, "R12"), (tutorial.CoreOwnerId, tutorial.OwnerUserId!.Value, tutorial.DisplayOwnerUserId!.Value, tutorial.CoreReferee));
            Assert.Null(tutorial.CoreCourseId);
            Assert.Equal((6627, 22, 6529, "10541+10543", 52), (tutorial.CoreBoardId!.Value, tutorial.CoreGradeId!.Value, tutorial.CoreSubjectId!.Value, tutorial.CoreTopicIds, tutorial.TopicOrder!.Value));
            Assert.Equal((TutorialStatus.Confirmed, 120, "Forces | Newton's laws"), (tutorial.Status, tutorial.ViewCount, tutorial.Title));
            Assert.Contains("core.gamatrain.com/uploads/tutorials/2026/09/1862_a.png", tutorial.Content, StringComparison.Ordinal);
            Assert.Equal(new DateTimeOffset(2026, 9, 15, 11, 46, 32, TimeSpan.Zero), tutorial.CreationDate);
            Assert.Equal("2026/09/1862_a.png", tutorial.Images!.Single().Path);

            // An owner without an account here is shown as the fallback user, and is nobody's.
            var orphan = await TutorialAsync(1863);
            Assert.Null(orphan.OwnerUserId);
            Assert.Equal((TutorialStatus.Pending, 56L), (orphan.Status, orphan.DisplayOwnerUserId!.Value));
        }

        [Fact]
        public async Task ATestServerCopiesOnlyASampleOfTheNewestTutorials()
        {
            foreach (var id in new long[] { 1700, 1701, 1702, 1703, 1704 })
            {
                AddTutorial(id, owner: 91001);
            }

            AddImage(15411, 1701, "2026/09/1701_a.png");
            AddImage(15412, 1704, "2026/09/1704_a.png");

            var result = await Service(maxTutorials: "2", fromId: "1702").ImportAsync();

            // Two tutorials from 1702 on; images only of the copied ones.
            Assert.Equal([1702L, 1703L], await context.Set<Tutorial>().OrderBy(t => t.Id).Select(t => t.Id).ToListAsync());
            Assert.Equal((2, 0, 2), (result.Data!.TutorialsRead, result.Data.ImagesAdded, result.Data.ImagesSkipped));
        }

        [Fact]
        public async Task ASecondRunUpdatesInsteadOfDuplicating()
        {
            AddTutorial(1862, owner: 91001);
            AddImage(15411, 1862, "2026/09/1862_a.png");
            _ = await Service().ImportAsync();

            gamaApiDb["dars"][0]["title"] = "Renamed";
            gamaApiDb["dars"][0]["views"] = 121;
            gamaApiDb["tutorials_files"][0]["file_name"] = "2026/09/1862_b.png";
            var result = await Service().ImportAsync();

            Assert.Equal((0, 1, 0, 1), (result.Data!.TutorialsAdded, result.Data.TutorialsUpdated, result.Data.ImagesAdded, result.Data.ImagesUpdated));
            var tutorial = await TutorialAsync(1862);
            Assert.Equal(("Renamed", 121, "2026/09/1862_b.png"), (tutorial.Title, tutorial.ViewCount, tutorial.Images!.Single().Path));
            Assert.Equal(1, await context.Set<Tutorial>().CountAsync());
        }

        [Fact]
        public async Task ATutorialsKeywordsAreCopiedInTheOrderTheyWereLinked()
        {
            AddTutorial(1862, owner: 91001);
            AddTutorial(1863, owner: 91001);
            gamaApiDb["keywords"] = [
                new() { ["id"] = 1L, ["display_title"] = "Friction" },
                new() { ["id"] = 2L, ["display_title"] = "Newton's Second Law" },
                new() { ["id"] = 3L, ["display_title"] = "Exam keyword" },
            ];
            // Service 1 is a tutorial; a link of another service (2) and a keyword linked twice are left out.
            gamaApiDb["content_keywords"] = [
                new() { ["id"] = 10L, ["contents_service"] = 1, ["content_id"] = 1862L, ["keyword_id"] = 2L },
                new() { ["id"] = 11L, ["contents_service"] = 1, ["content_id"] = 1862L, ["keyword_id"] = 1L },
                new() { ["id"] = 12L, ["contents_service"] = 2, ["content_id"] = 1862L, ["keyword_id"] = 3L },
                new() { ["id"] = 13L, ["contents_service"] = 1, ["content_id"] = 1862L, ["keyword_id"] = 1L },
            ];

            _ = await Service().ImportAsync();

            Assert.Equal("Newton's Second Law | Friction", (await TutorialAsync(1862)).Keywords);
            Assert.Null((await TutorialAsync(1863)).Keywords);
            Assert.Equal("a | b", TutorialImportService.JoinKeywords([" a ", "", "b|"]));
        }

        [Fact]
        public async Task NothingIsReadWithoutTheDatabaseOrOnceTutorialsAreEditedHere()
        {
            AddTutorial(1862, owner: 91001);

            configured = false;
            Assert.Equal(OperationResult.NotValid, (await Service().ImportAsync()).OperationResult);

            // gama-api's older version would overwrite the edits made here.
            configured = true;
            tutorialWrites = MigrationRoute.Native;
            Assert.Equal(OperationResult.NotValid, (await Service().ImportAsync()).OperationResult);
            Assert.False(await context.Set<Tutorial>().AnyAsync());
        }

        public void Dispose()
        {
            context.Dispose();
            serviceProvider.Dispose();
        }

        private TutorialImportService Service(string? fallbackOwner = null, string? maxTutorials = null, string? fromId = null) => new(
            new(serviceProvider.GetRequiredService<IUnitOfWorkProvider>),
            new(serviceProvider.GetRequiredService<IHttpContextAccessor>),
            new(() => NullLogger<TutorialImportService>.Instance),
            new(() => GamaApiDbFake.Create(gamaApiDb, () => configured)),
            new(() => new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Migration:PastPapers:FallbackOwnerUserId"] = fallbackOwner,
                ["Migration:LegacyDatabase:MaxTutorials"] = maxTutorials,
                ["Migration:LegacyDatabase:TutorialsFromId"] = fromId,
            }).Build()),
            new(() => new PassThroughMigrationRouter { WriteRoute = tutorialWrites }));

        private void AddUser(long id, long coreId)
        {
            _ = context.Set<ApplicationUser>().Add(new() { Id = id, UserName = $"user{id}", NormalizedUserName = $"USER{id}", ProfileVisibility = ProfileVisibility.Private, CoreId = coreId, Enabled = true });
            _ = context.SaveChanges();
            context.ChangeTracker.Clear();
        }

        private void AddTutorial(long id, long owner, string topic = "10541", int status = 1, string content = "<p>Text</p>")
        {
            if (!gamaApiDb.TryGetValue("dars", out var rows))
            {
                gamaApiDb["dars"] = rows = [];
            }

            rows.Add(new()
            {
                ["id"] = id,
                ["user_"] = owner,
                ["referee"] = "R12",
                ["section"] = 6627,
                ["base"] = 22,
                ["course"] = 0,
                ["lesson"] = 6529,
                ["topic"] = topic,
                ["topic_order"] = 52,
                ["title"] = "Forces | Newton's laws",
                ["description"] = "Forces",
                ["content"] = content,
                ["content_size"] = content.Length,
                ["status"] = status,
                ["views"] = 120,
                ["subdate"] = new DateTime(2026, 9, 15, 11, 46, 32, DateTimeKind.Unspecified),
                ["up_date"] = new DateTime(2026, 9, 16, 8, 0, 0, DateTimeKind.Unspecified),
            });
        }

        private void AddImage(long id, long tutorialId, string fileName)
        {
            if (!gamaApiDb.TryGetValue("tutorials_files", out var rows))
            {
                gamaApiDb["tutorials_files"] = rows = [];
            }

            rows.Add(new() { ["id"] = id, ["user_"] = 5138, ["tutorial_"] = tutorialId, ["file_name"] = fileName, ["subdate"] = new DateTime(2026, 9, 15, 0, 0, 0, DateTimeKind.Unspecified) });
        }

        private async Task<Tutorial> TutorialAsync(long id)
        {
            context.ChangeTracker.Clear();
            return await context.Set<Tutorial>().Include(t => t.Images).SingleAsync(t => t.Id == id);
        }
    }
}
