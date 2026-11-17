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

    using CacheFake = GamaEdtech.Test.Application.EditorImageServiceTests.CacheFake;
    using GamaApiDbFake = GamaEdtech.Test.Application.PastPaperImportTests.GamaApiDbFake;
    using TestApplicationDBContext = GamaEdtech.Test.Application.ExamAnalysisServiceTests.TestApplicationDBContext;

    /// <summary>
    /// Copying gama-api's exams, questions, attempts and answer sheets: ids and content kept, users linked by <c>CoreId</c>, a
    /// later run reads only what changed and removes what gama-api no longer has, a test server copies a sample, and the copy
    /// stops once exams are written here. The ids and names here are made up.
    /// </summary>
    public sealed class ExamCopyTests : IDisposable
    {
        private readonly ServiceProvider serviceProvider;
        private readonly TestApplicationDBContext context;
        private readonly Dictionary<string, List<Dictionary<string, object?>>> gamaApiDb = new(StringComparer.Ordinal);
        private readonly Dictionary<string, object?> cache = new(StringComparer.Ordinal);
        private MigrationRoute examWrites = MigrationRoute.Legacy;
        private MigrationRoute resultWrites = MigrationRoute.Legacy;
        private string? maxExams;

        public ExamCopyTests()
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

            _ = context.Set<ApplicationUser>().Add(new() { Id = 56, UserName = "u56", NormalizedUserName = "U56", CoreId = 5138, ProfileVisibility = ProfileVisibility.Private, Enabled = true });
            _ = context.SaveChanges();
            context.ChangeTracker.Clear();
        }

        [Fact]
        public async Task ExamsQuestionsAttemptsAndAnswersAreCopiedWithTheirIds()
        {
            AddExam(2540, questions: "77448,77449", owner: 5138);
            AddQuestion(77448, trueAnswer: 2);
            AddQuestion(77449, trueAnswer: 4);
            AddAttempt(1830, exam: 2540, user: 5138, score: "70", status: 1);
            AddSheet(1185, exam: 2540, attempt: 1830, user: 5138, answers: "77448:2,77449:1");

            var result = await Service().CopyAsync();

            Assert.Equal(OperationResult.Succeeded, result.OperationResult);
            Assert.Equal((1, 2, 1, 1), (result.Data!.Exams.Added, result.Data.Questions.Added, result.Data.Attempts.Added, result.Data.AnswerSheets.Added));
            context.ChangeTracker.Clear();
            var exam = await context.Set<Exam>().SingleAsync();
            Assert.Equal(("Biology 9700 predicted", 56L, "77448,77449", 40, 6627, (byte)1, "UsrkkH"),
                (exam.Title, exam.OwnerUserId!.Value, exam.QuestionIds, exam.QuestionCount, exam.CoreBoardId!.Value, exam.Status, exam.Code));
            Assert.Equal([77448L, 77449L], ExamCopyService.QuestionIdsOf(exam.QuestionIds));
            Assert.Null(exam.CoreCourseId);
            var question = await context.Set<ExamQuestion>().SingleAsync(t => t.Id == 77449);
            Assert.Equal(((byte?)4, "<p>Which organelle?</p>", "a.png"), (question.TrueAnswer, question.Question, question.QuestionFile));
            var attempt = await context.Set<ExamAttempt>().SingleAsync();
            Assert.Equal((56L, "70", (byte)1, 12854), (attempt.UserId!.Value, attempt.ResultScore, attempt.Status, attempt.SubmitSeconds!.Value));
            Assert.Equal(("77448:2,77449:1", 1830L), ((await context.Set<ExamAnswerSheet>().SingleAsync()).Answers, (await context.Set<ExamAnswerSheet>().SingleAsync()).AttemptId!.Value));
        }

        [Fact]
        public async Task ALaterRunReadsOnlyWhatChangedAndRemovesWhatIsGone()
        {
            AddExam(2540, questions: "77448");
            AddExam(2541, questions: "77448");
            AddQuestion(77448, trueAnswer: 2);
            AddAttempt(1830, exam: 2540, user: 1, status: 0);
            _ = await Service().CopyAsync();

            // A question edited (a new change date), an exam deleted on gama-api, an attempt finished.
            var question = gamaApiDb["azmoon_tests"][0];
            question["true_answer"] = 3;
            question["up_date"] = new DateTime(2026, 11, 18, 9, 0, 0, DateTimeKind.Unspecified);
            _ = gamaApiDb["azmoons"].RemoveAll(t => (long)t["id"]! == 2541);
            gamaApiDb["azmoon_data"][0]["status"] = 1;
            gamaApiDb["azmoon_data"][0]["result_score"] = "90";
            // A change without a new change date is not read again (gama-api stamps every edit).
            gamaApiDb["azmoons"][0]["title"] = "Unstamped change";

            var result = await Service().CopyAsync();

            Assert.Equal((0, 0, 1), (result.Data!.Exams.Added, result.Data.Exams.Updated, result.Data.Exams.Removed));
            Assert.Equal((1, 1), (result.Data.Questions.Updated, result.Data.Attempts.Updated));
            context.ChangeTracker.Clear();
            Assert.Equal((byte?)3, (await context.Set<ExamQuestion>().SingleAsync()).TrueAnswer);
            Assert.Equal("Biology 9700 predicted", (await context.Set<Exam>().SingleAsync()).Title);
            Assert.Equal("90", (await context.Set<ExamAttempt>().SingleAsync()).ResultScore);
        }

        [Fact]
        public async Task ATestServerCopiesTheNewestExamsWithOnlyTheirQuestionsAndAttempts()
        {
            AddExam(2538, questions: "1");
            AddExam(2539, questions: "2,3");
            AddExam(2540, questions: "3");
            foreach (var id in new long[] { 1, 2, 3, 4 })
            {
                AddQuestion(id, trueAnswer: 1);
            }

            AddAttempt(10, exam: 2538, user: 1, status: 1);
            AddAttempt(11, exam: 2540, user: 1, status: 1);
            AddSheet(20, exam: 2538, attempt: 10, user: 1, answers: "1:1");
            AddSheet(21, exam: 2540, attempt: 11, user: 1, answers: "3:1");
            maxExams = "2";

            _ = await Service().CopyAsync();

            Assert.Equal([2539L, 2540L], await context.Set<Exam>().OrderBy(t => t.Id).Select(t => t.Id).ToListAsync());
            Assert.Equal([2L, 3L], await context.Set<ExamQuestion>().OrderBy(t => t.Id).Select(t => t.Id).ToListAsync());
            Assert.Equal([11L], await context.Set<ExamAttempt>().Select(t => t.Id).ToListAsync());
            Assert.Equal([21L], await context.Set<ExamAnswerSheet>().Select(t => t.Id).ToListAsync());
        }

        [Fact]
        public async Task OnceResultsAreWrittenHereAttemptsAreLeftAlone()
        {
            AddExam(2540, questions: "77448");
            AddQuestion(77448, trueAnswer: 2);
            AddAttempt(1830, exam: 2540, user: 1, status: 1);
            _ = context.Set<ExamAttempt>().Add(new() { Id = 1900, CoreUserId = 1, ExamId = 2540, Status = 1, CreationDate = DateTimeOffset.UtcNow, SyncDate = DateTimeOffset.UtcNow });
            _ = await context.SaveChangesAsync();
            resultWrites = MigrationRoute.Native;

            var result = await Service().CopyAsync();

            // Exams and questions still follow gama-api; the attempt made here (1900) stays, gama-api's 1830 is not copied.
            Assert.Equal((1, 1, 0), (result.Data!.Exams.Added, result.Data.Questions.Added, result.Data.Attempts.Total));
            Assert.Equal([1900L], await context.Set<ExamAttempt>().Select(t => t.Id).ToListAsync());
        }

        [Fact]
        public async Task NothingIsReadOnceExamsAreWrittenHere()
        {
            AddExam(2540, questions: "1");
            examWrites = MigrationRoute.Native;

            Assert.Equal(OperationResult.NotValid, (await Service().CopyAsync()).OperationResult);
            Assert.False(await context.Set<Exam>().AnyAsync());
        }

        public void Dispose()
        {
            context.Dispose();
            serviceProvider.Dispose();
        }

        private ExamCopyService Service() => new(
            new(serviceProvider.GetRequiredService<IUnitOfWorkProvider>),
            new(serviceProvider.GetRequiredService<IHttpContextAccessor>),
            new(() => NullLogger<ExamCopyService>.Instance),
            new(() => GamaApiDbFake.Create(gamaApiDb, () => true)),
            new(() => new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Migration:LegacyDatabase:MaxExams"] = maxExams,
            }).Build()),
            new(() => new PassThroughMigrationRouter
            {
                WriteRoute = examWrites,
                WriteRoutes = new Dictionary<string, MigrationRoute>(StringComparer.Ordinal) { ["Exams.Games"] = resultWrites },
            }),
            new(() => CacheFake.Create(cache)));

        private void Add(string table, Dictionary<string, object?> row)
        {
            if (!gamaApiDb.TryGetValue(table, out var rows))
            {
                gamaApiDb[table] = rows = [];
            }

            rows.Add(row);
        }

        private void AddExam(long id, string questions, long owner = 900001) => Add("azmoons", new()
        {
            ["id"] = id,
            ["clone_id"] = 0,
            ["user_"] = owner,
            ["type"] = 1,
            ["azmoon_type"] = 6042,
            ["section"] = 6627,
            ["base"] = 4161,
            ["course"] = 0,
            ["lesson"] = 4164,
            ["title"] = "Biology 9700 predicted",
            ["tests"] = questions,
            ["tests_num"] = 40,
            ["tests_combination"] = 2,
            ["azmoon_time"] = 45,
            ["score_type"] = 1,
            ["code"] = "UsrkkH",
            ["file_pdf"] = "UsrkkH_x.pdf",
            ["status"] = 1,
            ["metadata"] = """{"paperID":"45350"}""",
            ["views"] = 12,
            ["bit_delete"] = 0,
            ["subdate"] = new DateTime(2026, 11, 10, 8, 0, 0, DateTimeKind.Unspecified),
            ["up_date"] = new DateTime(2026, 11, 11, 8, 0, 0, DateTimeKind.Unspecified),
        });

        private void AddQuestion(long id, int trueAnswer) => Add("azmoon_tests", new()
        {
            ["id"] = id,
            ["user_"] = 900001,
            ["section"] = 6627,
            ["lesson"] = 4164,
            ["question"] = "<p>Which organelle?</p>",
            ["q_file"] = "a.png",
            ["answer_a"] = "Nucleus",
            ["answer_b"] = "Ribosome",
            ["true_answer"] = trueAnswer,
            ["status"] = 1,
            ["subdate"] = new DateTime(2026, 11, 10, 8, 0, 0, DateTimeKind.Unspecified),
            ["up_date"] = new DateTime(2026, 11, 11, 8, 0, 0, DateTimeKind.Unspecified),
        });

        private void AddAttempt(long id, long exam, long user, int status, string? score = null) => Add("azmoon_data", new()
        {
            ["id"] = id,
            ["user_"] = user,
            ["azmoon"] = exam,
            ["azmoon_code"] = "UsrkkH",
            ["submit_time"] = 12854,
            ["result_score"] = score,
            ["status"] = status,
            ["state"] = 0,
            ["transfer"] = 1,
            ["subdate"] = new DateTime(2026, 11, 12, 8, 0, 0, DateTimeKind.Unspecified),
        });

        private void AddSheet(long id, long exam, long attempt, long user, string answers) => Add("azmoon_results", new()
        {
            ["id"] = id,
            ["user_"] = user,
            ["azmoon"] = exam,
            ["start_id"] = attempt,
            ["tests"] = answers,
            ["subdate"] = new DateTime(2026, 11, 12, 9, 0, 0, DateTimeKind.Unspecified),
        });
    }
}
