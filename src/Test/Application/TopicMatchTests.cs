namespace GamaEdtech.Test.Application
{
    using GamaEdtech.Application.Interface;
    using GamaEdtech.Application.Service;
    using GamaEdtech.Common.Data;
    using GamaEdtech.Common.DataAccess.Context;
    using GamaEdtech.Common.DataAccess.Repositories;
    using GamaEdtech.Common.DataAccess.UnitOfWork;
    using GamaEdtech.Data.Dto.Provider.Ai;
    using GamaEdtech.Data.Dto.Taxonomy;
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

    using Fake = GamaEdtech.Test.ExamImport.McpSignOutTests.Fake;
    using GamaApiDbFake = GamaEdtech.Test.Application.PastPaperImportTests.GamaApiDbFake;
    using TestApplicationDBContext = GamaEdtech.Test.Application.ExamAnalysisServiceTests.TestApplicationDBContext;

    /// <summary>
    /// gama-api's topic matching here: the copy of its suggested matches, approving (links the two topics) and taking it back,
    /// removing, adding by hand, and the AI's suggestions (only GAMA against one corresponding board, a season only against a
    /// season, only confident matches of topics it was given). GAMA Physics (7133) corresponds to Cambridge IGCSE Physics
    /// (2195) in gama-api's list; the other ids and names are made up.
    /// </summary>
    public sealed class TopicMatchTests : IDisposable
    {
        private const int GamaBoard = 7131;
        private const int GamaGrade = 7132;
        private const int GamaPhysics = 7133;
        private const int CambridgeBoard = 6627;
        private const int Igcse = 2179;
        private const int IgcsePhysics = 2195;
        private const int GamaForces = 20001;
        private const int GamaMechanics = 20002;
        private const int IgcseForces = 30001;
        private const int IgcseEnergy = 30002;
        private const int IgcseMotion = 30003;
        private const int IgcseUnit = 30004;

        private readonly ServiceProvider serviceProvider;
        private readonly TestApplicationDBContext context;
        private readonly Dictionary<string, List<Dictionary<string, object?>>> gamaApiDb = new(StringComparer.Ordinal);
        private readonly List<ChatCompletionRequestDto> aiRequests = [];
        private MigrationRoute writes = MigrationRoute.Native;
        private string aiAnswer = """
            {"matches":[
              {"candidate_topic_id": 30001, "confidence": 95, "reasoning": "Both teach forces."},
              {"candidate_topic_id": 30002, "confidence": 80, "reasoning": "Only related."},
              {"candidate_topic_id": 99999, "confidence": 99, "reasoning": "Not a candidate."},
              {"candidate_topic_id": "30003", "confidence": "92", "reasoning": "Motion."}
            ]}
            """;

        public TopicMatchTests()
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
        public void TheCandidatesAreAlwaysBetweenGamaAndAnotherBoard()
        {
            Assert.True(GamaSubjects.IsGama(GamaPhysics));
            Assert.Contains(IgcsePhysics, GamaSubjects.CandidateSubjects(GamaPhysics));
            // Another board's subject is compared with GAMA's only, never with the other boards in the same group.
            var fromIgcse = GamaSubjects.CandidateSubjects(IgcsePhysics);
            Assert.Contains(GamaPhysics, fromIgcse);
            Assert.All(fromIgcse, t => Assert.True(GamaSubjects.IsGama(t)));
            Assert.Empty(GamaSubjects.CandidateSubjects(123456));
        }

        [Fact]
        public async Task TheCopyMirrorsGamaApisMatches()
        {
            _ = context.Set<ApplicationUser>().Add(new() { Id = 56, UserName = "u56", NormalizedUserName = "U56", CoreId = 5138, ProfileVisibility = ProfileVisibility.Private, Enabled = true });
            _ = context.Set<TopicMatch>().Add(Match(900, GamaForces, IgcseEnergy));
            _ = await context.SaveChangesAsync();
            gamaApiDb["topic_found_matches"] = [Row(1, GamaForces, IgcseForces, status: 1, reviewer: 5138), Row(2, GamaForces, IgcseMotion)];

            var result = await Import().ImportAsync();

            Assert.Equal((2, 2, 0, 1), (result.Data!.Read, result.Data.Added, result.Data.Updated, result.Data.Removed));
            context.ChangeTracker.Clear();
            var approved = await context.Set<TopicMatch>().SingleAsync(t => t.Id == 1);
            Assert.Equal((TopicMatchStatus.Approved, 56L, 5138L, GamaPhysics, IgcsePhysics, (byte)95), (approved.Status, approved.ReviewedByUserId!.Value, approved.CoreReviewedBy!.Value, approved.GamaSubjectId, approved.ExternalSubjectId, approved.Confidence));
            Assert.False(await context.Set<TopicMatch>().AnyAsync(t => t.Id == 900));

            // A second run changes nothing; once reviewed here, the copy stops.
            Assert.Equal(0, (await Import().ImportAsync()).Data!.Updated);
            writes = MigrationRoute.Native;
            Assert.Equal(OperationResult.NotValid, (await Import(MigrationRoute.Native).ImportAsync()).OperationResult);
        }

        [Fact]
        public async Task ApprovingLinksTheTopicsAndTakingItBackUnlinksThem()
        {
            _ = context.Set<TopicMatch>().Add(Match(1, GamaForces, IgcseForces));
            _ = context.Set<LegacyTaxonomyRelation>().Add(new() { Id = 40, FromId = 1, ToId = 2, SyncDate = DateTimeOffset.UtcNow });
            _ = await context.SaveChangesAsync();
            var service = Admin();

            Assert.True((await service.ApproveAsync(1, 56)).Data);
            context.ChangeTracker.Clear();
            var link = await context.Set<LegacyTaxonomyRelation>().SingleAsync(t => t.Id == 41);
            Assert.Equal((GamaForces, IgcseForces), (link.FromId, link.ToId));
            var match = await context.Set<TopicMatch>().SingleAsync();
            Assert.Equal((TopicMatchStatus.Approved, 56L), (match.Status, match.ReviewedByUserId!.Value));

            // Approving again adds no second link; an approved match can't be removed before it is taken back.
            _ = await service.ApproveAsync(1, 56);
            Assert.Equal(2, await context.Set<LegacyTaxonomyRelation>().CountAsync());
            Assert.Equal("approved", (await service.RemoveAsync(1)).Errors!.Single().Reference);

            Assert.True((await service.UnapproveAsync(1)).Data);
            context.ChangeTracker.Clear();
            Assert.False(await context.Set<LegacyTaxonomyRelation>().AnyAsync(t => t.Id == 41));
            Assert.Equal((TopicMatchStatus.Pending, null), ((await context.Set<TopicMatch>().SingleAsync()).Status, (await context.Set<TopicMatch>().SingleAsync()).ReviewedByUserId));
            Assert.Equal(OperationResult.NotFound, (await service.UnapproveAsync(1)).OperationResult);

            Assert.True((await service.RemoveAsync(1)).Data);
            Assert.False(await context.Set<TopicMatch>().AnyAsync());
        }

        [Fact]
        public async Task NothingChangesUntilTopicMatchesAreReviewedHere()
        {
            _ = context.Set<TopicMatch>().Add(Match(1, GamaForces, IgcseForces));
            _ = await context.SaveChangesAsync();
            writes = MigrationRoute.Legacy;
            var service = Admin();

            Assert.Equal("notNative", (await service.ApproveAsync(1, 56)).Errors!.Single().Reference);
            Assert.Equal("notNative", (await service.SuggestAsync(GamaForces)).Errors!.Single().Reference);
            Assert.Equal("notNative", (await service.AddAsync(new() { GamaTopicId = GamaForces, ExternalTopicId = IgcseMotion })).Errors!.Single().Reference);
            Assert.False(await context.Set<LegacyTaxonomyRelation>().AnyAsync());
            Assert.Equal(1, (await service.GetListAsync(new())).Data!.Total);
        }

        [Fact]
        public async Task AMatchAddedByHandWaitsWithItsSubjects()
        {
            var service = Admin();

            var id = (await service.AddAsync(new() { GamaTopicId = GamaForces, ExternalTopicId = IgcseMotion, Confidence = 150 })).Data;

            var match = await context.Set<TopicMatch>().SingleAsync(t => t.Id == id);
            Assert.Equal((GamaPhysics, IgcsePhysics, (byte)100, "manual", TopicMatchStatus.Pending), (match.GamaSubjectId, match.ExternalSubjectId, match.Confidence, match.AiModel, match.Status));
            Assert.Equal(OperationResult.Duplicate, (await service.AddAsync(new() { GamaTopicId = GamaForces, ExternalTopicId = IgcseMotion })).OperationResult);
            Assert.Equal("noResult", (await service.AddAsync(new() { GamaTopicId = GamaForces, ExternalTopicId = 1 })).Errors!.Single().Reference);
        }

        [Fact]
        public async Task TheListShowsEachSidesPlaceAndTheGamaTopicsFirstTutorial()
        {
            _ = context.Set<TopicMatch>().Add(Match(1, GamaForces, IgcseForces));
            _ = context.Set<Tutorial>().Add(new() { Id = 1862, CoreOwnerId = 1, Title = "Forces | Newton's laws", CoreTopicIds = $"{GamaMechanics}+{GamaForces}", Status = TutorialStatus.Confirmed, CreationDate = DateTimeOffset.UtcNow, SyncDate = DateTimeOffset.UtcNow });
            _ = await context.SaveChangesAsync();

            var match = (await Admin().GetListAsync(new() { TopicId = IgcseForces })).Data!.Items.Single();

            Assert.Equal(("GAMA", "High School", "Physics", "Forces", 1862L), (match.Gama.Board!.Title, match.Gama.Grade!.Title, match.Gama.Subject.Title, match.Gama.Topic.Title, match.Gama.Tutorial!.Id));
            Assert.Equal(("Cambridge", "IGCSE", "Physics (0625)", "Forces and motion"), (match.External.Board!.Title, match.External.Grade!.Title, match.External.Subject.Title, match.External.Topic.Title));
            Assert.Null(match.External.Tutorial);
        }

        [Fact]
        public async Task TheAisConfidentMatchesOfItsCandidatesAreSavedAsWaiting()
        {
            _ = context.Set<TopicMatch>().Add(Match(5, GamaForces, IgcseMotion));
            _ = await context.SaveChangesAsync();

            var result = await Admin().SuggestAsync(GamaForces);

            Assert.Equal(OperationResult.Succeeded, result.OperationResult);
            // 30001 (95) is new; 30002 is not confident enough, 99999 was not a candidate, 30003 is suggested already.
            Assert.Equal(1, result.Data!.Added);
            var added = await context.Set<TopicMatch>().SingleAsync(t => t.ExternalTopicId == IgcseForces);
            Assert.Equal((6L, GamaPhysics, IgcsePhysics, (byte)95, "Both teach forces.", TopicMatchStatus.Pending), (added.Id, added.GamaSubjectId, added.ExternalSubjectId, added.Confidence, added.Reasoning, added.Status));
            Assert.Equal(2, result.Data.Items.Count);

            // The prompt names both sides; a topic is offered only topics (not seasons) of the corresponding subject.
            var prompt = aiRequests.Single().SystemPrompt!;
            Assert.Contains("- Board: GAMA", prompt, StringComparison.Ordinal);
            Assert.Contains("- id: 30001, title: Forces and motion", prompt, StringComparison.Ordinal);
            Assert.DoesNotContain("30004", prompt, StringComparison.Ordinal);
            Assert.Contains("Candidate topics (Board: Cambridge, Lesson: Physics (0625))", prompt, StringComparison.Ordinal);
        }

        [Fact]
        public async Task AnUnreadableAnswerIsReportedAndSavesNothing()
        {
            aiAnswer = "I cannot help with that.";

            var result = await Admin().SuggestAsync(GamaForces);

            Assert.Equal((0, 1), (result.Data!.Added, result.Data.Errors.Count));
            Assert.Null(TopicMatchAdminService.ParseMatches("no json"));
            Assert.Empty(TopicMatchAdminService.ParseMatches("""```json {"other":1} ```""")!);
            Assert.Equal("notFound", (await Admin().SuggestAsync(GamaPhysics)).Errors!.Single().Reference);
        }

        public void Dispose()
        {
            context.Dispose();
            serviceProvider.Dispose();
        }

        private TopicMatchImportService Import(MigrationRoute? route = null) => new(
            new(serviceProvider.GetRequiredService<IUnitOfWorkProvider>),
            new(serviceProvider.GetRequiredService<IHttpContextAccessor>),
            new(() => NullLogger<TopicMatchImportService>.Instance),
            new(() => GamaApiDbFake.Create(gamaApiDb, () => true)),
            new(() => new ConfigurationBuilder().Build()),
            new(() => new PassThroughMigrationRouter { WriteRoute = route ?? MigrationRoute.Legacy }));

        private TopicMatchAdminService Admin() => new(
            new(serviceProvider.GetRequiredService<IUnitOfWorkProvider>),
            new(serviceProvider.GetRequiredService<IHttpContextAccessor>),
            new(() => NullLogger<TopicMatchAdminService>.Instance),
            new(() => new PassThroughMigrationRouter { WriteRoute = writes }),
            new(() => Fake.Create<IAiService>(new()
            {
                ["CompleteAsync"] = args =>
                {
                    aiRequests.Add((ChatCompletionRequestDto)args[0]!);
                    return Task.FromResult(new ResultData<ChatCompletionResponseDto>(OperationResult.Succeeded) { Data = new() { Content = aiAnswer } });
                },
            })));

        private static TopicMatch Match(long id, int gamaTopic, int externalTopic) => new()
        {
            Id = id,
            GamaTopicId = gamaTopic,
            GamaSubjectId = GamaPhysics,
            ExternalTopicId = externalTopic,
            ExternalSubjectId = IgcsePhysics,
            Confidence = 95,
            CreationDate = DateTimeOffset.UtcNow,
            SyncDate = DateTimeOffset.UtcNow,
        };

        private static Dictionary<string, object?> Row(long id, int gamaTopic, int externalTopic, int status = 0, long? reviewer = null) => new()
        {
            ["id"] = id,
            ["gama_topic_id"] = gamaTopic,
            ["gama_lesson_id"] = GamaPhysics,
            ["external_topic_id"] = externalTopic,
            ["external_lesson_id"] = IgcsePhysics,
            ["confidence"] = 95,
            ["reasoning"] = "Same content.",
            ["ai_model"] = "gpt-4.1-mini",
            ["status"] = status,
            ["reviewed_by"] = reviewer,
            ["sub_date"] = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Unspecified),
            ["up_date"] = null,
        };

        private static LegacyTaxonomyItem Item(int id, short kind, string title, string parent = "", int order = 1, string? metadata = null) => new()
        {
            Id = id,
            Kind = kind,
            Title = title,
            Parent = parent,
            ListOrder = order,
            Metadata = metadata,
            SyncDate = DateTimeOffset.UtcNow,
            Parents = [.. LegacyTaxonomySyncService.ParentKeys(parent).Select(t => new LegacyTaxonomyItemParent { ParentKey = t })],
        };

        private void Seed()
        {
            context.Set<LegacyTaxonomyItem>().AddRange(
                Item(GamaBoard, LegacyTaxonomyKinds.Board, "GAMA"),
                Item(GamaGrade, LegacyTaxonomyKinds.Grade, "High School", $"{GamaBoard}"),
                Item(GamaPhysics, LegacyTaxonomyKinds.Subject, "Physics", $"0,{GamaGrade}"),
                Item(GamaForces, LegacyTaxonomyKinds.Topic, "Forces", $"{GamaPhysics}", 2),
                Item(GamaMechanics, LegacyTaxonomyKinds.Topic, "**Mechanics", $"{GamaPhysics}", 1, """{"season":1}"""),
                Item(CambridgeBoard, LegacyTaxonomyKinds.Board, "Cambridge"),
                Item(Igcse, LegacyTaxonomyKinds.Grade, "IGCSE", $"{CambridgeBoard}"),
                Item(IgcsePhysics, LegacyTaxonomyKinds.Subject, "Physics (0625)", $"0,{Igcse}"),
                Item(IgcseForces, LegacyTaxonomyKinds.Topic, "Forces and motion", $"{IgcsePhysics}", 2),
                Item(IgcseEnergy, LegacyTaxonomyKinds.Topic, "Energy", $"{IgcsePhysics}", 3),
                Item(IgcseMotion, LegacyTaxonomyKinds.Topic, "Motion", $"{IgcsePhysics}", 4),
                Item(IgcseUnit, LegacyTaxonomyKinds.Topic, "**Unit 1", $"{IgcsePhysics}", 1, """{"season":1}"""));
            _ = context.SaveChanges();
            context.ChangeTracker.Clear();
        }
    }
}
