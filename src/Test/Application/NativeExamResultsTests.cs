namespace GamaEdtech.Test.Application
{
    using System.Text.Json.Nodes;

    using GamaEdtech.Application.Service;
    using GamaEdtech.Common.DataAccess.Context;
    using GamaEdtech.Common.DataAccess.Repositories;
    using GamaEdtech.Common.DataAccess.UnitOfWork;
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

    using CdnFake = GamaEdtech.Test.Application.NativePastPaperSourceTests.CdnFake;
    using TestApplicationDBContext = GamaEdtech.Test.Application.ExamAnalysisServiceTests.TestApplicationDBContext;

    /// <summary>
    /// Taking an exam natively, as gama-api's <c>Exam_lib</c> does: a start makes (or resumes) the attempt above gama-api's ids
    /// and sends the questions without answers; a submit grades it (3 per right answer, minus 1 per wrong one with negative
    /// marking) once; the result page is the student's or the exam owner's, read by the points and the analysis; a user's
    /// results and an exam's participants (for its owner). The ids and names here are made up.
    /// </summary>
    public sealed class NativeExamResultsTests : IDisposable
    {
        private const int Biology = 6346;

        private readonly ServiceProvider serviceProvider;
        private readonly TestApplicationDBContext context;
        private readonly NativeExamSource source;

        public NativeExamResultsTests()
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
                new(() => NullLogger<NativeExamSource>.Instance),
                new(CdnFake.Create),
                new(() => new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Migration:PastPapers:FallbackOwnerUserId"] = "56" }).Build()));
            Seed();
        }

        [Fact]
        public async Task AStartMakesTheAttemptAboveGamaApisIdsAndHidesTheAnswers()
        {
            var started = await source.StartExamAsync(new() { ExamId = 2540, SecretKey = null, UserId = 57 });

            Assert.Equal(OperationResult.Succeeded, started.OperationResult);
            var data = started.Data!;
            Assert.Equal("1831", (string?)data["startID"]);
            Assert.InRange((long)data["remainedSeconds"]!, (45 * 60) - 5, 45 * 60);
            var tests = data["tests"]!.AsArray();
            Assert.Equal(["77448", "77449", "77450"], tests.Select(t => (string?)t!["id"]));
            Assert.All(tests, t => Assert.False(t!.AsObject().ContainsKey("true_answer")));
            Assert.Equal(("fourchoice", "https://cdn.test/examimage/a.png"), ((string?)tests[0]!["type"], (string?)tests[0]!["a_file"]));
            var attempt = await context.Set<ExamAttempt>().SingleAsync(t => t.Id == 1831);
            Assert.Equal((57L, 5138L, (byte)0), (attempt.UserId!.Value, attempt.CoreUserId, attempt.Status));

            // Starting again resumes the same attempt.
            Assert.Equal("1831", (string?)(await source.StartExamAsync(new() { ExamId = 2540, SecretKey = null, UserId = 57 })).Data!["startID"]);
            Assert.Equal("accessDenied", (await source.StartExamAsync(new() { ExamId = 2540, SecretKey = null })).Errors!.Single().Reference);
        }

        [Fact]
        public async Task ASubmitIsGradedOnceWithNegativeMarking()
        {
            var startId = long.Parse((string)(await source.StartExamAsync(new() { ExamId = 2540, SecretKey = null, UserId = 57 })).Data!["startID"]!, System.Globalization.CultureInfo.InvariantCulture);

            // One right, one wrong, one unanswered: (3 - 1) / 9 = 22.2%.
            var ended = await source.EndExamAsync(new() { ExamId = 2540, SecretKey = "t", UserId = 57, Body = Body(startId, ("77448", 4), ("77449", 1)) });

            Assert.Equal((OperationResult.Succeeded, startId), (ended.OperationResult, ended.Data!.Value));
            context.ChangeTracker.Clear();
            var attempt = await context.Set<ExamAttempt>().SingleAsync(t => t.Id == startId);
            Assert.Equal(((byte)1, "22.2"), (attempt.Status, attempt.ResultScore));
            Assert.Equal("77448:4,77449:1,77450:", (await context.Set<ExamAnswerSheet>().SingleAsync(t => t.AttemptId == startId)).Answers);
            Assert.Equal(1, (await context.Set<Exam>().SingleAsync(t => t.Id == 2540)).ParticipantCount);
            Assert.Equal("""{"":0,"1":0,"2":0,"3":0,"4":1}""", (await context.Set<ExamQuestion>().SingleAsync(t => t.Id == 77448)).AnswersStat);

            // A second submit answers with the result, as gama-api's refusal does; someone else's attempt is refused.
            var again = await source.EndExamAsync(new() { ExamId = 2540, SecretKey = "t", UserId = 57, Body = Body(startId) });
            Assert.Equal(("alreadyParticipated", startId), (again.Errors!.Single().Reference, again.Data!.Value));
            Assert.Equal("alreadyParticipated", (await source.StartExamAsync(new() { ExamId = 2540, SecretKey = null, UserId = 57 })).Errors!.Single().Reference);
            Assert.Equal("accessDenied", (await source.EndExamAsync(new() { ExamId = 2540, SecretKey = "t", UserId = 58, Body = Body(startId) })).Errors!.Single().Reference);
        }

        [Fact]
        public async Task TheResultPageIsTheStudentsOrTheExamOwnersAndFeedsThePoints()
        {
            var startId = long.Parse((string)(await source.StartExamAsync(new() { ExamId = 2540, SecretKey = null, UserId = 57 })).Data!["startID"]!, System.Globalization.CultureInfo.InvariantCulture);
            _ = await source.EndExamAsync(new() { ExamId = 2540, SecretKey = "t", UserId = 57, Body = Body(startId, ("77448", 4), ("77449", 1)) });

            var page = (await source.GetExamResultPageAsync(new() { ExamId = startId, SecretKey = null, UserId = 57 })).Data!;
            Assert.Equal("""{"num":3,"true":1,"false":1,"noAnswer":1,"percent":22.2}""", page["answerStats"]!["total"]!.ToJsonString());
            Assert.Equal(["4", "1", null], page["result"]!.AsArray().Select(t => (string?)t!["user_answer"]));
            Assert.Equal(("4", "Biology"), ((string?)page["result"]![0]!["true_answer"], (string?)page["result"]![0]!["lesson_title"]));
            Assert.Equal((true, false, "Ada"), ((bool)page["resultOwner"]!, (bool)page["examOwner"]!, (string?)page["user"]!["first_name"]));

            // The points and the analysis read the same page.
            var detail = (await source.GetExamResultDetailAsync(new() { ExamId = startId, SecretKey = null, UserId = 57 })).Data!;
            Assert.Equal((1, 1, 1, 3), (detail.Stats.Valid, detail.Stats.Invalid, detail.Stats.NoAnswer, detail.Stats.Total));
            Assert.Equal(('D', 'D'), (detail.Questions[0].CorrectOption!.Value, detail.Questions[0].StudentOption!.Value));

            Assert.True((bool)(await source.GetExamResultPageAsync(new() { ExamId = startId, SecretKey = null, UserId = 56 })).Data!["examOwner"]!);
            Assert.Equal("accessDenied", (await source.GetExamResultPageAsync(new() { ExamId = startId, SecretKey = null, UserId = 58 })).Errors!.Single().Reference);
        }

        [Fact]
        public async Task AUsersResultsAndAnExamsParticipants()
        {
            var startId = long.Parse((string)(await source.StartExamAsync(new() { ExamId = 2540, SecretKey = null, UserId = 57 })).Data!["startID"]!, System.Globalization.CultureInfo.InvariantCulture);
            _ = await source.EndExamAsync(new() { ExamId = 2540, SecretKey = "t", UserId = 57, Body = Body(startId, ("77448", 4)) });

            var mine = (await source.GetUserResultsAsync(new() { SecretKey = null, UserId = 57 })).Data!;
            Assert.Equal(("2", "1831", "Science Combined", "Biology"), ((string?)mine["num"], (string?)mine["list"]![0]!["id"], (string?)mine["list"]![0]!["exam_title"], (string?)mine["list"]![0]!["lesson_title"]));

            Assert.Equal("1", (string?)(await source.GetUserResultsAsync(new() { SecretKey = null, UserId = 57, Query = $"lesson={Biology}" })).Data!["num"]);
            Assert.Equal("0", (string?)(await source.GetUserResultsAsync(new() { SecretKey = null, UserId = 57, Query = "lesson=999" })).Data!["num"]);

            var participants = (await source.GetParticipantsAsync(new() { ExamId = 2540, SecretKey = null, UserId = 56, Query = "status=1" })).Data!;
            Assert.Equal(("1", "Ada", "33.3"), ((string?)participants["num"], (string?)participants["list"]![0]!["first_name"], (string?)participants["list"]![0]!["result_score"]));
            Assert.Equal("accessDenied", (await source.GetParticipantsAsync(new() { ExamId = 2540, SecretKey = null, UserId = 57 })).Errors!.Single().Reference);

            Assert.True((await source.ValidateTestAsync(new() { UserId = 57, TestId = 77448, SubmissionId = 4 })).Data);
            Assert.False((await source.ValidateTestAsync(new() { UserId = 57, TestId = 77448, SubmissionId = 1 })).Data);
        }

        [Fact]
        public void TheScoreIsGamaApisRule()
        {
            Exam plain = new() { Title = "x", QuestionCount = 4, ScoreType = 1 };
            Exam negative = new() { Title = "x", QuestionCount = 4, ScoreType = 2 };
            (byte?, int?)[] answers = [(1, 1), (2, 1), (3, null), (4, 4)];
            Assert.Equal((50m, 41.7m), (NativeExamSource.Score(plain, answers), NativeExamSource.Score(negative, answers)));
            Assert.Equal("""{"":1,"1":0,"2":0,"3":0,"4":0}""", NativeExamSource.AnswersStat(null, null));
        }

        public void Dispose()
        {
            context.Dispose();
            serviceProvider.Dispose();
        }

        private static JsonObject Body(long startId, params (string Question, int Choice)[] answers) => new()
        {
            ["startID"] = startId,
            ["answers"] = new JsonObject(answers.Select(t => KeyValuePair.Create(t.Question, (JsonNode?)t.Choice))),
        };

        private void Seed()
        {
            _ = context.Set<LegacyTaxonomyItem>().Add(new() { Id = Biology, Kind = LegacyTaxonomyKinds.Subject, Title = "Biology", SyncDate = DateTimeOffset.UtcNow, Parents = [] });
            context.Set<ApplicationUser>().AddRange(
                new ApplicationUser { Id = 56, UserName = "u56", NormalizedUserName = "U56", FirstName = "Marila", ProfileVisibility = ProfileVisibility.Private, Enabled = true },
                new ApplicationUser { Id = 57, UserName = "u57", NormalizedUserName = "U57", FirstName = "Ada", CoreId = 5138, ProfileVisibility = ProfileVisibility.Private, Enabled = true });
            _ = context.Set<Exam>().Add(new()
            {
                Id = 2540,
                CoreOwnerId = 1718,
                OwnerUserId = 56,
                CoreSubjectId = Biology,
                Type = 1,
                Title = "Science Combined",
                QuestionIds = "77448,77449,77450",
                QuestionCount = 3,
                DurationMinutes = 45,
                ScoreType = 2,
                Code = "UsrkkH",
                Status = 1,
                CreationDate = DateTimeOffset.UtcNow,
                SyncDate = DateTimeOffset.UtcNow,
            });
            foreach (var (id, answer) in new (long, byte)[] { (77448, 4), (77449, 2), (77450, 3) })
            {
                _ = context.Set<ExamQuestion>().Add(new()
                {
                    Id = id,
                    Code = $"q{id}",
                    CoreSubjectId = Biology,
                    Type = 1,
                    Question = $"<p>Question {id}</p>",
                    AnswerA = "A",
                    AnswerB = "B",
                    AnswerC = "C",
                    AnswerD = "D",
                    FileA = id == 77448 ? "a.png" : null,
                    TrueAnswer = answer,
                    Status = 1,
                    CreationDate = DateTimeOffset.UtcNow,
                    SyncDate = DateTimeOffset.UtcNow,
                });
            }

            // A copied attempt of another exam: new ones continue above its id.
            _ = context.Set<ExamAttempt>().Add(new() { Id = 1830, CoreUserId = 5138, UserId = 57, ExamId = 1906, Status = 1, ResultScore = "70", CreationDate = DateTimeOffset.UtcNow.AddDays(-1), SyncDate = DateTimeOffset.UtcNow });
            _ = context.SaveChanges();
            context.ChangeTracker.Clear();
        }
    }
}
