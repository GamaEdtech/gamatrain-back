namespace GamaEdtech.Test.Application
{
    using GamaEdtech.Application.Service;
    using GamaEdtech.Common.DataAccess.Context;
    using GamaEdtech.Common.DataAccess.Repositories;
    using GamaEdtech.Common.DataAccess.UnitOfWork;
    using GamaEdtech.Data.Dto.ExamAdmin;
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
    /// The admin's exams and question bank on our copy: lists with filters, an exam with its questions in order, and every change
    /// refused until exams are written here; then editing, status, delete, question order, and questions (checked, sanitized, new
    /// ids above gama-api's, never deleted while an exam uses them). The ids and names here are made up.
    /// </summary>
    public sealed class ExamAdminServiceTests : IDisposable
    {
        private const int Board = 6627;
        private const int Biology = 6346;
        private const int Cells = 10931;

        private readonly ServiceProvider serviceProvider;
        private readonly TestApplicationDBContext context;
        private MigrationRoute writes = MigrationRoute.Native;

        public ExamAdminServiceTests()
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
        public async Task TheListsFilterAndAnExamHasItsQuestionsInOrder()
        {
            var service = Service();

            var exams = (await service.GetExamsAsync(new() { SubjectId = Biology })).Data!;
            Assert.Equal((1, "Biology predicted", "Biology", "Marila Lombrozo"), (exams.Total, exams.Items[0].Title, exams.Items[0].SubjectTitle, exams.Items[0].OwnerName));
            Assert.Equal(1, (await service.GetExamsAsync(new() { Text = "Cx1" })).Data!.Total);
            Assert.Equal(0, (await service.GetExamsAsync(new() { Status = 6 })).Data!.Total);

            var exam = (await service.GetExamAsync(2540)).Data!;
            Assert.Equal([77449L, 77448L], exam.Questions.Select(t => t.Id));
            Assert.Equal(("Which organelle?", "fourchoice", "Cells"), (exam.Questions[1].Excerpt, exam.Questions[1].Type, exam.Questions[1].TopicTitle));

            var questions = (await service.GetQuestionsAsync(new() { TopicId = Cells })).Data!;
            Assert.Equal([77448L], questions.Items.Select(t => t.Id));
            Assert.Equal([77449L], (await service.GetQuestionsAsync(new() { Text = "Second" })).Data!.Items.Select(t => t.Id));
            Assert.Equal("https://cdn.test/examimage/a.png", (await service.GetQuestionAsync(77448)).Data!.AnswerAImage);
        }

        [Fact]
        public async Task NothingChangesUntilExamsAreWrittenHere()
        {
            writes = MigrationRoute.Legacy;
            var service = Service();

            Assert.Equal("notNative", (await service.UpdateExamAsync(2540, new() { Title = "x", SubjectId = Biology })).Errors!.Single().Reference);
            Assert.Equal("notNative", (await service.SetExamStatusAsync(2540, 1)).Errors!.Single().Reference);
            Assert.Equal("notNative", (await service.DeleteExamAsync(2540)).Errors!.Single().Reference);
            Assert.Equal("notNative", (await service.SetExamQuestionsAsync(2540, [77448])).Errors!.Single().Reference);
            Assert.Equal("notNative", (await service.CreateQuestionAsync(56, Question())).Errors!.Single().Reference);
            Assert.Equal("notNative", (await service.UpdateQuestionAsync(77448, Question())).Errors!.Single().Reference);
            Assert.Equal("notNative", (await service.DeleteQuestionAsync(77450)).Errors!.Single().Reference);
            Assert.Equal(OperationResult.Succeeded, (await service.GetExamAsync(2540)).OperationResult);
        }

        [Fact]
        public async Task AnExamsSettingsStatusQuestionsAndDelete()
        {
            var service = Service();

            Assert.True((await service.UpdateExamAsync(2540, new() { Title = "Renamed", BoardId = Board, SubjectId = Biology, DurationMinutes = 30, ScoreType = 2 })).Data);
            Assert.Equal("wrongType", (await service.UpdateExamAsync(2540, new() { Title = "x", SubjectId = Cells })).Errors!.Single().Reference);
            Assert.True((await service.SetExamStatusAsync(2540, 7)).Data);
            Assert.Equal("status", (await service.SetExamStatusAsync(2540, 9)).Errors!.Single().Reference);
            Assert.True((await service.SetExamQuestionsAsync(2540, [77448, 77450])).Data);
            Assert.Equal("wrongQuestion", (await service.SetExamQuestionsAsync(2540, [99999])).Errors!.Single().Reference);

            context.ChangeTracker.Clear();
            var exam = await context.Set<Exam>().SingleAsync();
            Assert.Equal(("Renamed", 30, (byte)2, (byte)7, "77448,77450", 2), (exam.Title, exam.DurationMinutes!.Value, exam.ScoreType!.Value, exam.Status, exam.QuestionIds, exam.QuestionCount));

            Assert.True((await service.DeleteExamAsync(2540)).Data);
            Assert.Equal(0, (await service.GetExamsAsync(new())).Data!.Total);
            Assert.Equal(1, (await service.GetExamsAsync(new() { IncludeDeleted = true })).Data!.Total);
        }

        [Fact]
        public async Task AQuestionIsCheckedSanitizedAndAddedAboveGamaApisIds()
        {
            var service = Service();

            var id = (await service.CreateQuestionAsync(56, Question("<p>What?<script>alert(1)</script></p>"))).Data;

            Assert.Equal(77451, id);
            var question = await context.Set<ExamQuestion>().SingleAsync(t => t.Id == id);
            Assert.DoesNotContain("<script", question.Question, StringComparison.OrdinalIgnoreCase);
            Assert.Equal((56L, (byte)0, (byte?)3, 6), (question.OwnerUserId!.Value, question.Status, question.TrueAnswer, question.Code!.Length));

            Assert.Equal("answers", (await service.CreateQuestionAsync(56, Question(trueAnswer: 5))).Errors!.Single().Reference);
            Assert.Equal("type", (await service.CreateQuestionAsync(56, Question(type: 3))).Errors!.Single().Reference);
            Assert.Equal("question", (await service.CreateQuestionAsync(56, Question("<p> </p>"))).Errors!.Single().Reference);

            Assert.True((await service.UpdateQuestionAsync(77450, Question("<p>Edited</p>"))).Data);
            Assert.True((await service.SetQuestionStatusAsync(77450, 2)).Data);
            context.ChangeTracker.Clear();
            Assert.Equal(("<p>Edited</p>", (byte)2), ((await context.Set<ExamQuestion>().SingleAsync(t => t.Id == 77450)).Question, (await context.Set<ExamQuestion>().SingleAsync(t => t.Id == 77450)).Status));

            // A question an exam uses is not deleted; an unused one is.
            Assert.Equal("usedByExam", (await service.DeleteQuestionAsync(77448)).Errors!.Single().Reference);
            Assert.True((await service.DeleteQuestionAsync(77450)).Data);
        }

        public void Dispose()
        {
            context.Dispose();
            serviceProvider.Dispose();
        }

        private static ExamQuestionEditDto Question(string text = "<p>Which?</p>", byte type = 1, byte trueAnswer = 3) => new()
        {
            BoardId = Board,
            SubjectId = Biology,
            CoreTopicIds = $"{Cells}",
            Type = type,
            Question = text,
            AnswerA = "A",
            AnswerB = "B",
            AnswerC = "C",
            AnswerD = "D",
            TrueAnswer = trueAnswer,
        };

        private ExamAdminService Service() => new(
            new(serviceProvider.GetRequiredService<IUnitOfWorkProvider>),
            new(serviceProvider.GetRequiredService<IHttpContextAccessor>),
            new(() => NullLogger<ExamAdminService>.Instance),
            new(() => new PassThroughMigrationRouter { WriteRoute = writes }),
            new(CdnFake.Create));

        private void Seed()
        {
            context.Set<LegacyTaxonomyItem>().AddRange(
                new LegacyTaxonomyItem { Id = Board, Kind = LegacyTaxonomyKinds.Board, Title = "Cambridge", SyncDate = DateTimeOffset.UtcNow, Parents = [] },
                new LegacyTaxonomyItem { Id = Biology, Kind = LegacyTaxonomyKinds.Subject, Title = "Biology", SyncDate = DateTimeOffset.UtcNow, Parents = [] },
                new LegacyTaxonomyItem { Id = Cells, Kind = LegacyTaxonomyKinds.Topic, Title = "**Cells", SyncDate = DateTimeOffset.UtcNow, Parents = [] });
            _ = context.Set<ApplicationUser>().Add(new() { Id = 56, UserName = "u56", NormalizedUserName = "U56", FirstName = "Marila", LastName = "Lombrozo", ProfileVisibility = ProfileVisibility.Private, Enabled = true });
            _ = context.Set<Exam>().Add(new()
            {
                Id = 2540,
                CoreOwnerId = 1718,
                OwnerUserId = 56,
                CoreBoardId = Board,
                CoreSubjectId = Biology,
                Type = 1,
                Title = "Biology predicted",
                Code = "Cx1",
                QuestionIds = "77449,77448",
                QuestionCount = 2,
                Status = 1,
                CreationDate = DateTimeOffset.UtcNow,
                LastModifyDate = DateTimeOffset.UtcNow,
                SyncDate = DateTimeOffset.UtcNow,
            });
            foreach (var (id, text, topic) in new (long, string, string?)[] { (77448, "<p>Which organelle?</p>", $"{Cells}"), (77449, "<p>Second</p>", null), (77450, "<p>Unused</p>", null) })
            {
                _ = context.Set<ExamQuestion>().Add(new()
                {
                    Id = id,
                    Code = $"q{id}",
                    CoreSubjectId = Biology,
                    CoreTopicIds = topic,
                    Type = 1,
                    Question = text,
                    FileA = id == 77448 ? "a.png" : null,
                    TrueAnswer = 2,
                    Status = 1,
                    CreationDate = DateTimeOffset.UtcNow,
                    LastModifyDate = DateTimeOffset.UtcNow.AddMinutes(-id + 77440),
                    SyncDate = DateTimeOffset.UtcNow,
                });
            }

            _ = context.SaveChanges();
            context.ChangeTracker.Clear();
        }
    }
}
