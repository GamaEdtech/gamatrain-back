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
    /// The native exam reads from our copy, in gama-api's shapes: the exam page (with the caller's own attempt), a question's page
    /// and a random practice question (never locked), the search's exam tab, and what the export reads. Only published exams and
    /// confirmed questions are public; images come from our storage; no Jalali dates. The ids and names here are made up.
    /// </summary>
    public sealed class NativeExamSourceTests : IDisposable
    {
        private const int Board = 6627;
        private const int Grade = 22;
        private const int Biology = 6346;
        private const int Chemistry = 6347;
        private const int Cells = 10931;
        private const int OtherBoardCells = 20001;
        private const int Paper1 = 6042;

        private static readonly string[] SubjectCodes = ["xkTC9v", "pQ2"];

        private readonly ServiceProvider serviceProvider;
        private readonly TestApplicationDBContext context;
        private readonly NativeExamSource source;

        public NativeExamSourceTests()
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
        public async Task AnExamsPageIsWhatGamaApiShows()
        {
            var result = await source.GetExamDetailsAsync(new() { ExamId = 2540, SecretKey = null });

            Assert.Equal(OperationResult.Succeeded, result.OperationResult);
            var data = result.Data!;
            Assert.Equal(("2540", "UsrkkH", "1718", "Science Combined 0653-11 May Jun 2026 Online Exam", "40", "45"),
                ((string?)data["id"], (string?)data["code"], (string?)data["user_"], (string?)data["title"], (string?)data["tests_num"], (string?)data["azmoon_time"]));
            Assert.Equal("""["77448","77449"]""", data["tests"]!.ToJsonString());
            Assert.Equal(("Cambridge", "IGCSE", "Biology", "Paper 1"), ((string?)data["section_title"], (string?)data["base_title"], (string?)data["lesson_title"], (string?)data["azmoon_type_title"]));
            Assert.Equal(("Science-Combined-0653-11-May-Jun-2026-Online-Exam", "Jun", "45350"), ((string?)data["title_url"], (string?)data["edu_month_title"], (string?)data["paperID"]));
            Assert.Equal("https://cdn.test/thumbnail/UsrkkH_azmoon_1.png", (string?)data["thumb_pic_url"]);
            // Free to take; the PDF a unit per question, rounded to ten.
            Assert.Equal("""{"participation":{"price":0},"pdf":{"price":40}}""", data["price"]!.ToJsonString());
            Assert.True((bool)data["participable"]!);
            Assert.Equal((true, true, true, false, false), ((bool)data["freetime"]!, (bool)data["participateTime"]!, (bool)data["pdf"]!, (bool)data["owner"]!, (bool)data["examUserData"]!));
            Assert.Equal(("Marila", "Lombrozo", "0"), ((string?)data["first_name"], (string?)data["last_name"], (string?)data["username"]));
            // The topics decoded as gama-api sends them on the page.
            Assert.Equal("""[{"id":"10931","order":"1","title":"Cells","season":false,"pages":{"from":"","to":""}}]""", data["topics"]!.ToJsonString());
            // No Jalali dates: they mean nothing on this site.
            Assert.DoesNotContain(data, t => t.Key.Contains("jalali", StringComparison.Ordinal));
        }

        [Fact]
        public async Task TheCallersOwnAttemptIsOnTheirPage()
        {
            var signedIn = (await source.GetExamDetailsAsync(new() { ExamId = 2540, SecretKey = null, UserId = 57 })).Data!;
            Assert.Equal(("""{"id":"1830","status":"1"}""", "57"), (signedIn["examUserData"]!.ToJsonString(), (string?)signedIn["uid"]));

            // An exam whose gama-api owner has no account here shows the fallback author, as the past papers do.
            var unlinked = context.Set<Exam>().Single(t => t.Id == 2542);
            unlinked.OwnerUserId = null;
            // gama-api sends a missing month as "0".
            unlinked.EduMonth = null;
            _ = await context.SaveChangesAsync();
            context.ChangeTracker.Clear();
            var fallback = (await source.GetExamDetailsAsync(new() { ExamId = 2542, SecretKey = null })).Data!;
            Assert.Equal(("Marila", "0"), ((string?)fallback["first_name"], (string?)fallback["edu_month"]));

            // A draft is the owner's only.
            Assert.Equal(OperationResult.Failed, (await source.GetExamDetailsAsync(new() { ExamId = 2541, SecretKey = null })).OperationResult);
            var own = (await source.GetExamDetailsAsync(new() { ExamId = 2541, SecretKey = null, UserId = 56 })).Data!;
            Assert.True((bool)own["owner"]!);
            Assert.False((bool)own["participable"]!);
        }

        [Fact]
        public async Task ATimedExamOpensAndClosesOnItsDates()
        {
            var exam = context.Set<Exam>().Single(t => t.Id == 2540);
            exam.StartDate = DateTimeOffset.UtcNow.AddHours(-1);
            exam.EndDate = DateTimeOffset.UtcNow.AddHours(1);
            _ = await context.SaveChangesAsync();
            context.ChangeTracker.Clear();

            var open = (await source.GetExamDetailsAsync(new() { ExamId = 2540, SecretKey = null })).Data!;
            Assert.Equal((false, true, false, true, true), ((bool)open["freetime"]!, (bool)open["started"]!, (bool)open["finished"]!, (bool)open["participateTime"]!, (bool)open["participable"]!));

            // A day after its end, gama-api opens it again with no dates.
            exam.StartDate = DateTimeOffset.UtcNow.AddDays(-3);
            exam.EndDate = DateTimeOffset.UtcNow.AddDays(-2);
            _ = context.Update(exam);
            _ = await context.SaveChangesAsync();
            context.ChangeTracker.Clear();
            var reopened = (await source.GetExamDetailsAsync(new() { ExamId = 2540, SecretKey = null })).Data!;
            Assert.Equal((true, null), ((bool)reopened["freetime"]!, (string?)reopened["start_date"]));
        }

        [Fact]
        public async Task AQuestionsPageIsFoundByItsCodeWithItsTitleAndImages()
        {
            var data = (await source.GetExamTestPageAsync("xkTC9v", full: true)).Data!;

            Assert.Equal(("77448", "fourchoice", "Haemoglobin", "<p>Which is right?</p>", "4"),
                ((string?)data["id"], (string?)data["type"], (string?)data["title"], (string?)data["question"], (string?)data["true_answer"]));
            Assert.Equal(("https://cdn.test/examimage/a.png", string.Empty, true), ((string?)data["a_file"], (string?)data["b_file"], (bool)data["testImgAnswers"]!));
            Assert.Equal(("Cells", "Which is right? | Biology IGCSE"), ((string?)data["topic_title"], (string?)data["pageTitle"]));
            Assert.Equal("""{"locked":false,"limit":false,"usage":false,"remained":0}""", data["paywall"]!.ToJsonString());
            Assert.Equal("1862", (string?)data["tutorial_id"]);

            // A question waiting for review is not public.
            Assert.Equal(OperationResult.Failed, (await source.GetExamTestPageAsync("draftQ", full: false)).OperationResult);
        }

        [Fact]
        public async Task ARandomQuestionIsFromTheTopicElseTheSubject()
        {
            Assert.Equal("xkTC9v", (string?)(await source.GetRandomExamTestAsync($"lesson={Biology}&topic={Cells}")).Data!["code"]);
            // No question of that topic: any of the subject's.
            Assert.Contains((string?)(await source.GetRandomExamTestAsync($"lesson={Biology}&topic=99")).Data!["code"], SubjectCodes);
            Assert.Equal(OperationResult.Failed, (await source.GetRandomExamTestAsync($"lesson={Chemistry}")).OperationResult);
        }

        [Fact]
        public async Task TheSearchListsPublishedExamsNewestFirstWithLinkedTopics()
        {
            var all = (await source.SearchExamsAsync($"section={Board}")).Data!;
            Assert.Equal("2", (string?)all["num"]);
            // By year and month, newest first; the draft is not listed.
            Assert.Equal(["2540", "2542"], all["list"]!.AsArray().Select(t => (string?)t!["id"]));
            var item = all["list"]![0]!;
            Assert.Equal(("UsrkkH", "Biology", "@marila"), ((string?)item["code"], (string?)item["lesson_title"], (string?)item["identity"]));

            Assert.Equal(["2542"], (await source.SearchExamsAsync("title=Chemistry")).Data!["list"]!.AsArray().Select(t => (string?)t!["id"]));
            Assert.Equal(["2540"], (await source.SearchExamsAsync("test_id=77448")).Data!["list"]!.AsArray().Select(t => (string?)t!["id"]));

            // A topic search also takes the exams of a topic linked from another board.
            _ = context.Set<LegacyTaxonomyRelation>().Add(new() { Id = 1, FromId = Cells, ToId = OtherBoardCells, SyncDate = DateTimeOffset.UtcNow });
            _ = await context.SaveChangesAsync();
            Assert.Equal(["2540", "2542"], (await source.SearchExamsAsync($"topic={Cells}")).Data!["list"]!.AsArray().Select(t => (string?)t!["id"]));
        }

        [Fact]
        public async Task TheExportReadsTheQuestionsInExamOrder()
        {
            var info = (await source.GetExamInformationAsync(new() { ExamId = 2540, SecretKey = null })).Data!;

            Assert.Equal(("Science Combined 0653-11 May Jun 2026 Online Exam", 40, "Marila Lombrozo", 1718L), (info.Exam!.Title, info.Exam.TestsCount, info.Exam.Author, info.Exam.AuthorCoreId!.Value));
            Assert.Equal(['D', 'B'], info.Tests!.Select(t => t.CorrectOption ?? ' '));
            Assert.Equal(("<p>Which is right?</p>", "https://cdn.test/examimage/a.png"), (info.Tests![0].Question, info.Tests[0].OptionAFile));
            Assert.Equal(2, (await source.GetExamQuestionCountAsync(new() { ExamId = 2540, SecretKey = null })).Data);
            Assert.Equal(OperationResult.Failed, (await source.GetExamQuestionCountAsync(new() { ExamId = 2541, SecretKey = null })).OperationResult);
        }

        [Fact]
        public async Task TheExamBuilderIsNotNativeYet()
        {
            Assert.Equal("notNative", (await source.GetExamResultAsync(new() { ExamId = 2540, SecretKey = "t" })).Errors!.Single().Reference);
            Assert.Equal("notNative", (await source.GetCurrentExamIdAsync(new() { SecretKey = "t" })).Errors!.Single().Reference);
            Assert.Equal("Haemoglobin", ExamQuestionText.SplitTitle("<p>@@Haemoglobin@@</p><p>Q</p>").Title);
            Assert.Equal("""{"participation":{"price":300},"pdf":{"price":600}}""",
                NativeExamSource.Price(new() { Title = "x", Status = 1, QuestionCount = 600 }).ToJsonString());
        }

        public void Dispose()
        {
            context.Dispose();
            serviceProvider.Dispose();
        }

        private static LegacyTaxonomyItem Item(int id, short kind, string title, string parent = "", int order = 1) => new()
        {
            Id = id,
            Kind = kind,
            Title = title,
            Parent = parent,
            ListOrder = order,
            SyncDate = DateTimeOffset.UtcNow,
            Parents = [.. LegacyTaxonomySyncService.ParentKeys(parent).Select(t => new LegacyTaxonomyItemParent { ParentKey = t })],
        };

        private void Seed()
        {
            context.Set<LegacyTaxonomyItem>().AddRange(
                Item(Board, LegacyTaxonomyKinds.Board, "Cambridge"),
                Item(Grade, LegacyTaxonomyKinds.Grade, "IGCSE", $"{Board}"),
                Item(Biology, LegacyTaxonomyKinds.Subject, "Biology", $"0,{Grade}"),
                Item(Chemistry, LegacyTaxonomyKinds.Subject, "Chemistry", $"0,{Grade}"),
                Item(Cells, LegacyTaxonomyKinds.Topic, "Cells", $"{Biology}"),
                Item(OtherBoardCells, LegacyTaxonomyKinds.Topic, "Cells (other board)", "7000"),
                Item(Paper1, LegacyTaxonomyKinds.Paper, "Paper 1", $"{Board}"));
            context.Set<ApplicationUser>().AddRange(
                new ApplicationUser { Id = 56, UserName = "u56", NormalizedUserName = "U56", FirstName = "Marila", LastName = "Lombrozo", Handle = "marila", ProfileVisibility = ProfileVisibility.Private, Enabled = true },
                new ApplicationUser { Id = 57, UserName = "u57", NormalizedUserName = "U57", ProfileVisibility = ProfileVisibility.Private, Enabled = true });

            context.Set<Exam>().AddRange(
                Exam(2540, "Science Combined 0653-11 May Jun 2026 Online Exam", "77448,77449", Biology, $"{Cells}", year: 2026, month: 6),
                Exam(2541, "Draft exam", "77448", Biology, null, year: 2026, month: 7, status: 6),
                Exam(2542, "Chemistry predicted", "77449", Chemistry, $"{OtherBoardCells}", year: 2025, month: 11));
            context.Set<ExamQuestion>().AddRange(
                Question(77448, "xkTC9v", "<p>@@Haemoglobin@@</p><p>Which is right?</p>", 4, Biology, $"{Cells}", fileA: "a.png"),
                Question(77449, "pQ2", "<p>Second</p>", 2, Biology, null),
                Question(77450, "draftQ", "<p>Waiting</p>", 1, Biology, $"{Cells}", status: 0));
            _ = context.Set<ExamAttempt>().Add(new() { Id = 1830, CoreUserId = 900, UserId = 57, ExamId = 2540, Status = 1, ResultScore = "70", CreationDate = DateTimeOffset.UtcNow, SyncDate = DateTimeOffset.UtcNow });
            _ = context.Set<Tutorial>().Add(new() { Id = 1862, CoreOwnerId = 1, Title = "Cells", CoreTopicIds = $"{Cells}", Status = TutorialStatus.Confirmed, CreationDate = DateTimeOffset.UtcNow, SyncDate = DateTimeOffset.UtcNow });
            _ = context.SaveChanges();
            context.ChangeTracker.Clear();
        }

        private static Exam Exam(long id, string title, string questions, int subject, string? topics, short year, byte month, byte status = 1) => new()
        {
            Id = id,
            CoreOwnerId = 1718,
            OwnerUserId = 56,
            CoreBoardId = Board,
            CoreGradeId = Grade,
            CoreSubjectId = subject,
            CoreTopicIds = topics,
            CoreExamTypeId = Paper1,
            Type = 1,
            Title = title,
            QuestionIds = questions,
            QuestionCount = 40,
            DurationMinutes = 45,
            ScoreType = 1,
            Code = id == 2540 ? "UsrkkH" : $"C{id}",
            EduYear = year,
            EduMonth = month,
            FilePdf = "UsrkkH_x.pdf",
            Thumbnail = id == 2540 ? "uploads/thumbs/UsrkkH_azmoon_1.png" : null,
            Status = status,
            Metadata = """{"paperID":"45350","app":"v5"}""",
            CreationDate = new DateTimeOffset(2026, 11, 16, 0, 47, 53, TimeSpan.Zero),
            LastModifyDate = new DateTimeOffset(2026, 11, 16, 0, 47, 53, TimeSpan.Zero),
            SyncDate = DateTimeOffset.UtcNow,
        };

        private static ExamQuestion Question(long id, string code, string text, byte trueAnswer, int subject, string? topics, byte status = 1, string? fileA = null) => new()
        {
            Id = id,
            CoreOwnerId = 1218,
            Code = code,
            CoreBoardId = Board,
            CoreGradeId = Grade,
            CoreSubjectId = subject,
            CoreTopicIds = topics,
            Type = 1,
            Question = text,
            AnswerA = "<p>A</p>",
            AnswerB = "<p>B</p>",
            AnswerC = "<p>C</p>",
            AnswerD = "<p>D</p>",
            FileA = fileA,
            TrueAnswer = trueAnswer,
            AnswerViewType = 4,
            Status = status,
            CreationDate = new DateTimeOffset(2022, 11, 8, 12, 1, 49, TimeSpan.Zero),
            LastModifyDate = new DateTimeOffset(2022, 11, 8, 12, 1, 49, TimeSpan.Zero),
            SyncDate = DateTimeOffset.UtcNow,
        };
    }
}
