namespace GamaEdtech.Test.Application
{
    using GamaEdtech.Application.Service;
    using GamaEdtech.Common.DataAccess.Context;
    using GamaEdtech.Common.DataAccess.Repositories;
    using GamaEdtech.Common.DataAccess.UnitOfWork;
    using GamaEdtech.Data.Dto.Taxonomy;
    using GamaEdtech.Domain.Entity;
    using GamaEdtech.Domain.Entity.Identity;
    using GamaEdtech.Domain.Enumeration;

    using Microsoft.AspNetCore.Http;
    using Microsoft.Extensions.Configuration;
    using Microsoft.Extensions.DependencyInjection;
    using Microsoft.Extensions.Logging.Abstractions;

    using Xunit;

    using static GamaEdtech.Common.Core.Constants;

    using CdnFake = GamaEdtech.Test.Application.NativePastPaperSourceTests.CdnFake;
    using TestApplicationDBContext = GamaEdtech.Test.Application.ExamAnalysisServiceTests.TestApplicationDBContext;

    /// <summary>
    /// The native tutorial page, lesson tree and search from our copy, in gama-api's shapes: only published tutorials, the
    /// content's images from our storage and its HTML sanitized, the tree's seasons with their chapters, and a topic search
    /// that also takes the topics linked from another board. The ids and names here are made up.
    /// </summary>
    public sealed class NativeTutorialSourceTests : IDisposable
    {
        private const int Board = 6627;
        private const int Grade = 22;
        private const int Physics = 6529;
        private const int Season1 = 10541;
        private const int Chapter1 = 10543;
        private const int Chapter2 = 10544;
        private const int Season2 = 10550;
        private const int OtherBoardTopic = 20001;

        private readonly ServiceProvider serviceProvider;
        private readonly TestApplicationDBContext context;
        private readonly NativeTutorialSource source;

        public NativeTutorialSourceTests()
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

            source = new(
                new(serviceProvider.GetRequiredService<IUnitOfWorkProvider>),
                new(serviceProvider.GetRequiredService<IHttpContextAccessor>),
                new(() => NullLogger<NativeTutorialSource>.Instance),
                new(CdnFake.Create));
            Seed();
        }

        [Fact]
        public async Task ATutorialsPageIsWhatGamaApiShows()
        {
            var result = await source.GetTutorialAsync(1862);

            Assert.Equal(OperationResult.Succeeded, result.OperationResult);
            var data = result.Data!;
            Assert.Equal(("1862", "6627", "22", "0", "6529", "10541+10543"),
                ((string?)data["id"], (string?)data["section"], (string?)data["base"], (string?)data["course"], (string?)data["lesson"], (string?)data["topic"]));
            Assert.Equal(("Board", "Grade 10", "Physics (1)", "1"), ((string?)data["section_title"], (string?)data["base_title"], (string?)data["lesson_title"], (string?)data["status"]));
            Assert.False(data.ContainsKey("course_title"));
            Assert.Equal("https://cdn.test/subjectpicture/phys.jpg", (string?)data["lesson_pic"]);
            Assert.Equal("2026-09-16 08:00:00", (string?)data["up_date"]);
            Assert.Equal("Forces | Newton's laws", (string?)data["pageTitle"]);

            // The season first (its marks taken off), each with its page range.
            Assert.Equal("""[{"id":"10541","order":"1","title":"Mechanics","season":true,"pages":{"from":"","to":""}},{"id":"10543","order":"2","title":"Forces","season":false,"pages":{"from":"3","to":"9"}}]""",
                data["topic_info"]!.ToJsonString());

            // gama-api's image links point at our storage; the script is removed.
            var content = (string?)data["content"];
            Assert.Contains("https://cdn.test/tutorial/2026/09/1862_a.png", content, StringComparison.Ordinal);
            Assert.DoesNotContain("core.gamatrain.com", content, StringComparison.Ordinal);
            Assert.DoesNotContain("<script", content, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void ThePageDescriptionIsGamaApisMeta()
        {
            Assert.Equal("درسنامه آموزشی Newton's laws Forces شامل First law، Second law در گاما",
                NativeTutorialSource.PageDescription("درسنامه آموزشی Forces | Newton's laws", "<p>- First law</p>|Second law|First law", "Physics (1)", "Grade 10"));
            // A title without a "book | topic" split takes the subject and grade as the book.
            Assert.Equal("درسنامه آموزشی Forces Physics Grade 10 در گاما", NativeTutorialSource.PageDescription("Forces", null, "Physics (1)", "Grade 10"));
        }

        [Fact]
        public async Task OnlyPublishedTutorialsAreServed()
        {
            Assert.Equal(OperationResult.Failed, (await source.GetTutorialAsync(1900)).OperationResult);
            Assert.Equal(OperationResult.Failed, (await source.GetTutorialAsync(1)).OperationResult);

            var list = (await source.SearchTutorialsAsync($"lesson={Physics}")).Data!["list"]!.AsArray();
            Assert.DoesNotContain(list, t => (string?)t!["id"] == "1900");
        }

        [Fact]
        public async Task TheLessonTreeIsTheSeasonsWithTheirChaptersTutorials()
        {
            var result = await source.GetLessonTreeAsync(Physics);

            Assert.Equal(OperationResult.Succeeded, result.OperationResult);
            var data = result.Data!;
            Assert.Equal(($"{Physics}", "Physics (1)"), ((string?)data["id"], (string?)data["title"]));
            var list = data["list"]!.AsArray();
            Assert.Equal([$"{Season1}", $"{Season2}"], list.Select(t => (string?)t!["id"]));

            var season = list[0]!;
            Assert.Equal(("Mechanics", true, 2), ((string?)season["title"], (bool)season["season"]!, (int)season["tutorialsNum"]!));
            var chapters = season["chapters"]!.AsArray();
            Assert.Equal([$"{Chapter1}", $"{Chapter2}"], chapters.Select(t => (string?)t!["id"]));
            // Newest change first, never a draft.
            Assert.Equal(["1863", "1862"], chapters[0]!["tutorials"]!.AsArray().Select(t => (string?)t!["id"]));
            Assert.Equal("Forces-Newtons-laws", (string?)chapters[0]!["tutorials"]![1]!["title_url"]);
            Assert.Empty(chapters[1]!["tutorials"]!.AsArray());

            // A season without chapters lists its own tutorials.
            var alone = list[1]!.AsObject();
            Assert.False(alone.ContainsKey("chapters"));
            Assert.Equal(["1864"], alone["tutorials"]!.AsArray().Select(t => (string?)t!["id"]));

            Assert.Equal("wrongLessonId", (await source.GetLessonTreeAsync(Chapter1)).Errors!.Single().Reference);
        }

        [Fact]
        public async Task TheSearchFiltersSortsAndPages()
        {
            var result = await source.SearchTutorialsAsync($"?lesson={Physics}&perpage=2&page=1");

            Assert.Equal(OperationResult.Succeeded, result.OperationResult);
            Assert.Equal(3, (int)result.Data!["num"]!);
            var list = result.Data["list"]!.AsArray();
            // Newest change first by default.
            Assert.Equal(["1864", "1863"], list.Select(t => (string?)t!["id"]));
            var item = list[1]!;
            Assert.Equal(("Ethan", "Carter", "56", "Physics (1)"), ((string?)item["first_name"], (string?)item["last_name"], (string?)item["user_id"], (string?)item["lesson_title"]));
            Assert.Null(item["avatar"]);
            Assert.False(item.AsObject().ContainsKey("content"));

            Assert.Equal(["1862", "1863", "1864"], await IdsAsync($"lesson={Physics}&sortby=subdateAsc"));
            Assert.Equal(["1862"], await IdsAsync("title=Newton"));
            Assert.Empty(await IdsAsync($"section={Board}&base=99"));
        }

        [Fact]
        public async Task ATopicSearchAlsoTakesTheTopicsLinkedFromAnotherBoard()
        {
            // A tutorial of another board on topic 20001, linked to chapter 10543.
            _ = context.Set<LegacyTaxonomyItem>().Add(Item(OtherBoardTopic, LegacyTaxonomyKinds.Topic, "Forces (other board)", 0, "7000"));
            _ = context.Set<LegacyTaxonomyRelation>().Add(new() { Id = 1, FromId = Chapter1, ToId = OtherBoardTopic, SyncDate = DateTimeOffset.UtcNow });
            _ = AddTutorial(1865, $"{OtherBoardTopic}", "Other board forces", subject: 7000, day: 20);
            _ = await context.SaveChangesAsync();
            context.ChangeTracker.Clear();

            Assert.Equal(["1865", "1863", "1862"], await IdsAsync($"topic={Chapter1}"));
            // The subject's list takes the topics imported into it too.
            Assert.Contains("1865", await IdsAsync($"lesson={Physics}"));
            Assert.DoesNotContain("1865", await IdsAsync($"lesson={Physics}&title=Newton"));
        }

        public void Dispose()
        {
            context.Dispose();
            serviceProvider.Dispose();
        }

        private async Task<List<string?>> IdsAsync(string query) =>
            [.. (await source.SearchTutorialsAsync(query)).Data!["list"]!.AsArray().Select(t => (string?)t!["id"])];

        private static LegacyTaxonomyItem Item(int id, short kind, string title, int order, string parent = "", string? metadata = null, string? picture = null) => new()
        {
            Id = id,
            Kind = kind,
            Title = title,
            ListOrder = order,
            Parent = parent,
            Metadata = metadata,
            Picture = picture,
            SyncDate = DateTimeOffset.UtcNow,
            Parents = [.. LegacyTaxonomySyncService.ParentKeys(parent).Select(t => new LegacyTaxonomyItemParent { ParentKey = t })],
        };

        private void Seed()
        {
            context.Set<LegacyTaxonomyItem>().AddRange(
                Item(Board, LegacyTaxonomyKinds.Board, "Board", 1),
                Item(Grade, LegacyTaxonomyKinds.Grade, "Grade 10", 1, $"{Board}"),
                Item(Physics, LegacyTaxonomyKinds.Subject, "Physics (1)", 1, $"0,{Grade}", picture: "phys.jpg"),
                Item(Season1, LegacyTaxonomyKinds.Topic, "**Mechanics", 1, $"{Physics}", """{"season":true}"""),
                Item(Chapter1, LegacyTaxonomyKinds.Topic, "Forces", 2, $"{Physics}", """{"startPage":"3","endPage":"9"}"""),
                Item(Chapter2, LegacyTaxonomyKinds.Topic, "Energy", 3, $"{Physics}"),
                Item(Season2, LegacyTaxonomyKinds.Topic, "**Waves", 4, $"{Physics}", """{"season":true}"""));
            _ = context.Set<ApplicationUser>().Add(new() { Id = 56, UserName = "owner@example.test", NormalizedUserName = "OWNER@EXAMPLE.TEST", FirstName = "Ethan", LastName = "Carter", ProfileVisibility = ProfileVisibility.Private, Enabled = true });

            var first = AddTutorial(1862, $"{Season1}+{Chapter1}", "Forces | Newton's laws", day: 16);
            first.Content = """<p>Text <img src="https://core.gamatrain.com/uploads/tutorials/2026/09/1862_a.png"></p><script src="https://polyfill.io/v3/polyfill.min.js"></script>""";
            first.CreationDate = new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero);
            _ = AddTutorial(1863, $"{Chapter1}", "Forces | Friction", day: 17).CreationDate = new DateTimeOffset(2026, 9, 2, 0, 0, 0, TimeSpan.Zero);
            _ = AddTutorial(1864, $"{Season2}", "Waves | Sound", day: 18).CreationDate = new DateTimeOffset(2026, 9, 3, 0, 0, 0, TimeSpan.Zero);
            AddTutorial(1900, $"{Chapter1}", "Draft", day: 19).Status = TutorialStatus.Pending;
            _ = context.SaveChanges();
            context.ChangeTracker.Clear();
        }

        private Tutorial AddTutorial(long id, string topics, string title, int subject = Physics, int day = 16)
        {
            Tutorial tutorial = new()
            {
                Id = id,
                CoreOwnerId = 900001,
                DisplayOwnerUserId = 56,
                CoreBoardId = subject == Physics ? Board : 7001,
                CoreGradeId = subject == Physics ? Grade : 7002,
                CoreSubjectId = subject,
                CoreTopicIds = topics,
                Title = title,
                Description = "<p>- First law</p>|Second law",
                Content = "<p>Text</p>",
                Status = TutorialStatus.Confirmed,
                ViewCount = 120,
                CreationDate = new DateTimeOffset(2026, 9, 15, 11, 46, 32, TimeSpan.Zero),
                LastModifyDate = new DateTimeOffset(2026, 9, day, 8, 0, 0, TimeSpan.Zero),
                SyncDate = DateTimeOffset.UtcNow,
            };
            _ = context.Set<Tutorial>().Add(tutorial);
            return tutorial;
        }
    }
}
