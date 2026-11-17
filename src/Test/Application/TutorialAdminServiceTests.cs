namespace GamaEdtech.Test.Application
{
    using GamaEdtech.Application.Interface;
    using GamaEdtech.Application.Service;
    using GamaEdtech.Common.Caching;
    using GamaEdtech.Common.Data;
    using GamaEdtech.Common.DataAccess.Context;
    using GamaEdtech.Common.DataAccess.Repositories;
    using GamaEdtech.Common.DataAccess.UnitOfWork;
    using GamaEdtech.Data.Dto.Provider.Ai;
    using GamaEdtech.Data.Dto.Taxonomy;
    using GamaEdtech.Data.Dto.Tutorial;
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

    using CdnFake = GamaEdtech.Test.Application.NativePastPaperSourceTests.CdnFake;
    using Fake = GamaEdtech.Test.ExamImport.McpSignOutTests.Fake;
    using TestApplicationDBContext = GamaEdtech.Test.Application.ExamAnalysisServiceTests.TestApplicationDBContext;

    /// <summary>
    /// The admin's tutorials: nothing changes until tutorials are edited here, a new one continues above gama-api's ids, the
    /// HTML is sanitized and its headings make the description, the types must exist, a change drops the cached page and
    /// lesson trees, and the AI's keywords are saved. The ids and names here are made up.
    /// </summary>
    public sealed class TutorialAdminServiceTests : IDisposable
    {
        private const int Board = 6627;
        private const int Grade = 22;
        private const int Physics = 6529;
        private const int Chemistry = 6530;
        private const int Season = 10541;
        private const int Chapter = 10543;

        private readonly ServiceProvider serviceProvider;
        private readonly TestApplicationDBContext context;
        private readonly List<string> removedCacheKeys = [];
        private readonly List<ChatCompletionRequestDto> aiRequests = [];
        private MigrationRoute writes = MigrationRoute.Native;
        private string aiAnswer = "KEYWORDS: Newton's Second Law | friction | Friction\nTOPICS: Forces and Motion";

        public TutorialAdminServiceTests()
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
            Seed();
        }

        [Fact]
        public async Task ANewTutorialContinuesAboveGamaApisIdsSanitizedAndWaiting()
        {
            var result = await Service().CreateAsync(56, Edit(content: """<h2>Forces</h2><p>Push &amp; pull<script>alert(1)</script></p><h3>Newton's laws</h3>"""));

            Assert.Equal(OperationResult.Succeeded, result.OperationResult);
            Assert.Equal(1863, result.Data);
            var tutorial = await TutorialAsync(1863);
            Assert.Equal((TutorialStatus.Pending, 56L, 56L, 0L), (tutorial.Status, tutorial.OwnerUserId!.Value, tutorial.DisplayOwnerUserId!.Value, tutorial.CoreOwnerId));
            Assert.DoesNotContain("<script", tutorial.Content, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(("Forces | Newton's laws", tutorial.Content!.Length), (tutorial.Description, tutorial.ContentSize!.Value));
            // Topics as gama-api stores them; the order is the last topic's list order.
            Assert.Equal(($"{Season}+{Chapter}", 2), (tutorial.CoreTopicIds, tutorial.TopicOrder!.Value));
            Assert.Equal("Newton's laws | Forces", tutorial.Keywords);
            Assert.Contains(SearchService.LessonTreeCacheKey(Physics), removedCacheKeys);
        }

        [Fact]
        public async Task NothingChangesUntilTutorialsAreEditedHere()
        {
            writes = MigrationRoute.Legacy;
            var service = Service();

            Assert.Equal("notNative", (await service.CreateAsync(56, Edit())).Errors!.Single().Reference);
            Assert.Equal("notNative", (await service.UpdateAsync(1862, Edit())).Errors!.Single().Reference);
            Assert.Equal("notNative", (await service.ChangeStatusAsync(1862, TutorialStatus.Deactivated)).Errors!.Single().Reference);
            Assert.Equal("notNative", (await service.DeleteAsync(1862)).Errors!.Single().Reference);
            Assert.Equal("notNative", (await service.GenerateKeywordsAsync(1862)).Errors!.Single().Reference);
            // Reading is fine.
            Assert.Equal(OperationResult.Succeeded, (await service.GetAsync(1862)).OperationResult);
            Assert.Equal(1, await context.Set<Tutorial>().CountAsync());
        }

        [Fact]
        public async Task TheRequiredFieldsAndTypesAreChecked()
        {
            var service = Service();

            var missing = await service.CreateAsync(56, new() { Content = "<p> </p>" });
            Assert.Equal(("requiredField", OperationResult.NotValid), (missing.Errors!.Single().Reference, missing.OperationResult));
            Assert.Equal("wrongType", (await service.CreateAsync(56, Edit(subject: Board))).Errors!.Single().Reference);
            Assert.Equal("wrongType", (await service.CreateAsync(56, Edit(topics: [Physics]))).Errors!.Single().Reference);
            // A content of only a picture is a content.
            Assert.Equal(OperationResult.Succeeded, (await service.CreateAsync(56, Edit(content: """<p><img src="https://cdn.test/editorimage/a.webp"></p>"""))).OperationResult);
        }

        [Fact]
        public async Task AnEditMovesTheTutorialAndDropsBothSubjectsCachedTrees()
        {
            var result = await Service().UpdateAsync(1862, Edit(subject: Chemistry, topics: [], description: "Own description", status: TutorialStatus.Confirmed, keywords: null));

            Assert.Equal(OperationResult.Succeeded, result.OperationResult);
            var tutorial = await TutorialAsync(1862);
            Assert.Equal((Chemistry, null, null, "Own description"), (tutorial.CoreSubjectId!.Value, tutorial.CoreTopicIds, tutorial.TopicOrder, tutorial.Description));
            // The views and the owner stay; keywords stay when not sent; the change date moves.
            Assert.Equal((120, 91L, "old keyword"), (tutorial.ViewCount, tutorial.OwnerUserId!.Value, tutorial.Keywords));
            Assert.True(tutorial.LastModifyDate > new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero));
            Assert.Equal([SearchService.TutorialCacheKey(1862), SearchService.LessonTreeCacheKey(Physics), SearchService.LessonTreeCacheKey(Chemistry)], removedCacheKeys);

            Assert.Equal(OperationResult.NotFound, (await Service().UpdateAsync(9999, Edit())).OperationResult);
        }

        [Fact]
        public async Task TheStatusAndDeleteChangeOnlyThatTutorial()
        {
            var service = Service();

            Assert.True((await service.ChangeStatusAsync(1862, TutorialStatus.Deactivated)).Data);
            Assert.Equal(TutorialStatus.Deactivated, (await TutorialAsync(1862)).Status);

            Assert.True((await service.DeleteAsync(1862)).Data);
            Assert.False(await context.Set<Tutorial>().AnyAsync());
            Assert.False(await context.Set<TutorialImage>().AnyAsync());
        }

        [Fact]
        public async Task TheListFiltersByStatusSubjectAndTextWithTitlesAndOwner()
        {
            _ = await Service().CreateAsync(56, Edit(title: "Acids", subject: Chemistry, topics: []));

            var all = (await Service().GetListAsync(new())).Data!;
            Assert.Equal((2, 1863L), (all.Total, all.Items[0].Id));

            var pending = (await Service().GetListAsync(new() { Status = TutorialStatus.Confirmed })).Data!.Items.Single();
            Assert.Equal((1862L, "Board", "Grade 10", "Physics", "Ethan Carter"), (pending.Id, pending.BoardTitle, pending.GradeTitle, pending.SubjectTitle, pending.OwnerName));
            // A season's marks are taken off its title.
            Assert.Equal(["Mechanics", "Forces"], pending.Topics.Select(t => t.Title));
            Assert.Equal(["old keyword"], pending.Keywords);

            Assert.Equal([1862L], (await Service().GetListAsync(new() { Title = "old key" })).Data!.Items.Select(t => t.Id));
            Assert.Equal([1862L], (await Service().GetListAsync(new() { Title = "1862" })).Data!.Items.Select(t => t.Id));
            Assert.Equal([1863L], (await Service().GetListAsync(new() { SubjectId = Chemistry })).Data!.Items.Select(t => t.Id));
            Assert.Equal([1862L], (await Service().GetListAsync(new() { TopicId = Chapter })).Data!.Items.Select(t => t.Id));
        }

        [Fact]
        public async Task TheEditFormGetsTheHtmlWithImagesOnOurStorage()
        {
            var tutorial = (await Service().GetAsync(1862)).Data!;

            Assert.StartsWith("""<p><img src="https://cdn.test/tutorial/2026/09/1862_a.png"></p>""", tutorial.Content, StringComparison.Ordinal);
            Assert.Equal(OperationResult.NotFound, (await Service().GetAsync(9999)).OperationResult);
        }

        [Fact]
        public async Task TheAisKeywordsAreSavedOnTheTutorial()
        {
            var result = await Service().GenerateKeywordsAsync(1862);

            Assert.Equal(OperationResult.Succeeded, result.OperationResult);
            Assert.Equal(["Newton's Second Law", "friction"], result.Data!.Keywords);
            Assert.Equal(["Forces and Motion"], result.Data.Clusters);
            Assert.Equal("Newton's Second Law | friction", (await TutorialAsync(1862)).Keywords);
            // The tutorial's text, never its HTML, goes to the AI.
            Assert.Contains("Pushes and pulls", aiRequests.Single().UserPrompt, StringComparison.Ordinal);
            Assert.DoesNotContain("<p>", aiRequests.Single().UserPrompt, StringComparison.Ordinal);

            aiAnswer = "Sorry, I can't.";
            Assert.Equal("noKeywords", (await Service().GenerateKeywordsAsync(1862)).Errors!.Single().Reference);
            Assert.Equal("Newton's Second Law | friction", (await TutorialAsync(1862)).Keywords);
        }

        [Fact]
        public void TheAisAnswerIsReadEvenWithMarks()
        {
            var found = TutorialAdminService.ParseKeywords("```\n**KEYWORDS:** `vector` | acceleration |  | Vector\nTOPICS: Motion\n```");

            Assert.Equal(["vector", "acceleration"], found.Keywords);
            Assert.Equal(["Motion"], found.Clusters);
            Assert.Equal("Forces | Newton's laws", TutorialContent.Description("<h2>Forces</h2><p>x</p><h3 class=\"a\">Newton&#39;s <b>laws</b></h3>"));
            Assert.Null(TutorialContent.Description("<p>No headings</p>"));
        }

        public void Dispose()
        {
            context.Dispose();
            serviceProvider.Dispose();
        }

        private static TutorialEditDto Edit(string title = "Forces | Newton's laws", string content = "<p>Pushes and pulls</p>", int subject = Physics,
            IReadOnlyList<int>? topics = null, string? description = null, TutorialStatus? status = null, IReadOnlyList<string>? keywords = null) => new()
            {
                Title = title,
                Content = content,
                Description = description,
                BoardId = Board,
                GradeId = Grade,
                SubjectId = subject,
                TopicIds = topics ?? [Season, Chapter],
                Keywords = keywords ?? (status is null && description is null ? ["Newton's laws", "Forces"] : null),
                Status = status,
            };

        private TutorialAdminService Service() => new(
            new(serviceProvider.GetRequiredService<IUnitOfWorkProvider>),
            new(serviceProvider.GetRequiredService<IHttpContextAccessor>),
            new(() => NullLogger<TutorialAdminService>.Instance),
            new(() => new PassThroughMigrationRouter { WriteRoute = writes }),
            new(CdnFake.Create),
            new(() => Fake.Create<ICacheProvider>(new()
            {
                ["RemoveAsync"] = args =>
                {
                    removedCacheKeys.Add((string)args[0]!);
                    return Task.CompletedTask;
                },
            })),
            new(() => Fake.Create<IAiService>(new()
            {
                ["CompleteAsync"] = args =>
                {
                    aiRequests.Add((ChatCompletionRequestDto)args[0]!);
                    return Task.FromResult(new ResultData<ChatCompletionResponseDto>(OperationResult.Succeeded) { Data = new() { Content = aiAnswer } });
                },
            })));

        private async Task<Tutorial> TutorialAsync(long id)
        {
            context.ChangeTracker.Clear();
            return await context.Set<Tutorial>().SingleAsync(t => t.Id == id);
        }

        private static LegacyTaxonomyItem Item(int id, short kind, string title, int order = 1) => new()
        {
            Id = id,
            Kind = kind,
            Title = title,
            ListOrder = order,
            SyncDate = DateTimeOffset.UtcNow,
            Parents = [],
        };

        private void Seed()
        {
            context.Set<LegacyTaxonomyItem>().AddRange(
                Item(Board, LegacyTaxonomyKinds.Board, "Board"),
                Item(Grade, LegacyTaxonomyKinds.Grade, "Grade 10"),
                Item(Physics, LegacyTaxonomyKinds.Subject, "Physics"),
                Item(Chemistry, LegacyTaxonomyKinds.Subject, "Chemistry"),
                Item(Season, LegacyTaxonomyKinds.Topic, "**Mechanics", 1),
                Item(Chapter, LegacyTaxonomyKinds.Topic, "Forces", 2));
            context.Set<ApplicationUser>().AddRange(
                new ApplicationUser { Id = 56, UserName = "admin@example.test", NormalizedUserName = "ADMIN@EXAMPLE.TEST", FirstName = "Ada", LastName = "Admin", ProfileVisibility = ProfileVisibility.Private, Enabled = true },
                new ApplicationUser { Id = 91, UserName = "owner@example.test", NormalizedUserName = "OWNER@EXAMPLE.TEST", FirstName = "Ethan", LastName = "Carter", ProfileVisibility = ProfileVisibility.Private, Enabled = true });
            _ = context.Set<Tutorial>().Add(new()
            {
                Id = 1862,
                CoreOwnerId = 900001,
                OwnerUserId = 91,
                DisplayOwnerUserId = 91,
                CoreBoardId = Board,
                CoreGradeId = Grade,
                CoreSubjectId = Physics,
                CoreTopicIds = $"{Season}+{Chapter}",
                Title = "Forces | Newton's laws",
                Content = """<p><img src="https://core.gamatrain.com/uploads/tutorials/2026/09/1862_a.png"></p><p>Pushes and pulls</p>""",
                Keywords = "old keyword",
                Status = TutorialStatus.Confirmed,
                ViewCount = 120,
                CreationDate = new DateTimeOffset(2026, 9, 15, 0, 0, 0, TimeSpan.Zero),
                LastModifyDate = new DateTimeOffset(2026, 9, 16, 0, 0, 0, TimeSpan.Zero),
                SyncDate = DateTimeOffset.UtcNow,
                Images = [new() { CoreFileId = 15411, Path = "2026/09/1862_a.png" }],
            });
            _ = context.SaveChanges();
            context.ChangeTracker.Clear();
        }
    }
}
