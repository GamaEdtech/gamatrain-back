namespace GamaEdtech.Test.Application
{
    using System.Globalization;
    using System.Reflection;
    using System.Text;
    using System.Text.Json.Nodes;

    using GamaEdtech.Application.Interface;
    using GamaEdtech.Application.Service;
    using GamaEdtech.Common.Data;
    using GamaEdtech.Common.DataAccess.Context;
    using GamaEdtech.Common.DataAccess.Repositories;
    using GamaEdtech.Common.DataAccess.UnitOfWork;
    using GamaEdtech.Data.Dto.File;
    using GamaEdtech.Data.Dto.Taxonomy;
    using GamaEdtech.Domain.Entity;
    using GamaEdtech.Domain.Entity.Identity;
    using GamaEdtech.Domain.Enumeration;
    using GamaEdtech.Infrastructure.Interface;

    using Microsoft.AspNetCore.DataProtection;
    using Microsoft.AspNetCore.Http;
    using Microsoft.EntityFrameworkCore;
    using Microsoft.Extensions.Configuration;
    using Microsoft.Extensions.DependencyInjection;
    using Microsoft.Extensions.Logging.Abstractions;

    using Xunit;

    using static GamaEdtech.Common.Core.Constants;

    using TestApplicationDBContext = GamaEdtech.Test.Application.ExamAnalysisServiceTests.TestApplicationDBContext;

    /// <summary>
    /// A teacher adding, editing and deleting their own papers natively, by gama-api's rules: uploads are the uploader's own,
    /// a new paper takes the next id and waits for review, an edit after review is limited and sends it back to review, and
    /// a paper past review keeps its files.
    /// </summary>
    public sealed class TeacherPaperWriteTests : IDisposable
    {
        private const long Teacher = 56;
        private const long OtherTeacher = 57;
        private const long Student = 58;
        private const int Cambridge = 6627;
        private const int ALevel = 4161;
        private const int Biology = 4164;
        private const int Paper1 = 6320;

        private readonly ServiceProvider serviceProvider;
        private readonly TestApplicationDBContext context;
        private readonly NativePastPaperSource source;

        public TeacherPaperWriteTests()
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
                new(() => NullLogger<NativePastPaperSource>.Instance),
                new(StorageFake.Create),
                new(() => null!),
                new(() => new EphemeralDataProtectionProvider()),
                new(() => null!),
                new(() => null!));
            Seed();
        }

        [Fact]
        public async Task ATeachersNewPaperTakesTheNextIdAndWaitsForReview()
        {
            var pdf = await UploadAsync(Teacher, "paper.pdf", TwoPagePdf(), "pdf");
            var word = await UploadAsync(Teacher, "paper.docx", [1, 2, 3]);

            var result = await source.AddPaperAsync(new()
            {
                UserId = Teacher,
                Token = "t",
                Body = Body(pdf, word, """{"title":"Biology+9700!","description":"<p>Real <b>paper</b></p><script>x()</script><i>it</i>","topics":[11,12],"holding_level":3,"state":["7","8"],"uid":"56"}"""),
            });

            Assert.Equal(OperationResult.Succeeded, result.OperationResult);
            Assert.Equal(45831, (long)result.Data!["id"]!);
            var paper = await context.Set<PastPaper>().Include(t => t.Files).SingleAsync(t => t.Id == 45831);
            Assert.Equal((PastPaperStatus.Pending, Teacher, Teacher, false), (paper.Status, paper.OwnerUserId, paper.DisplayOwnerUserId, paper.IsDeleted));
            // As gama-api's strip_tags: tags other than b, br and p go, their text stays (as plain text).
            Assert.Equal(("Biology 9700", "<p>Real <b>paper</b></p>x()it"), (paper.Title, paper.Description));
            Assert.Equal(("11+12", (short?)3, "7+8", true), (paper.CoreTopicIds, paper.HoldingLevel, paper.State, paper.IsPaper));
            Assert.Equal((Cambridge, ALevel, Biology, Paper1), (paper.CoreBoardId, paper.CoreGradeId, paper.CoreSubjectId, paper.CorePaperTypeId!.Value));

            // The question PDF: its pages and thumbnail; both files stored and sized, so they can be downloaded.
            var question = paper.Files!.Single(t => t.Kind == PastPaperFileKind.Question);
            Assert.Equal(("stored-2.pdf", (short?)2, "pdf"), (question.Path, question.PageCount, question.Extension));
            Assert.Equal("stored-1.webp", paper.Thumbnail);
            Assert.Equal(("stored-3.docx", 3L), (paper.Files!.Single(t => t.Kind == PastPaperFileKind.QuestionWord).Path, paper.Files!.Single(t => t.Kind == PastPaperFileKind.QuestionWord).Size!.Value));
        }

        [Fact]
        public async Task AnAddFollowsGamaApisChecks()
        {
            var pdf = await UploadAsync(Teacher, "paper.pdf", TwoPagePdf());

            // Only teachers add papers, and only for themselves.
            Assert.Equal("accessDenied", (await source.AddPaperAsync(new() { UserId = Student, Token = "t", Body = Body(pdf) })).Errors!.Single().Reference);
            Assert.Equal("onBehalfNotAllowed", (await source.AddPaperAsync(new() { UserId = Teacher, Token = "t", Body = Body(pdf, extra: """{"uid":"57"}""") })).Errors!.Single().Info);

            // The required fields, by classification: a paper needs its answer type, year and month.
            var missing = await source.AddPaperAsync(new() { UserId = Teacher, Token = "t", Body = JsonNode.Parse("""{"section":6627,"test_type":6320}""")!.AsObject() });
            Assert.Equal(("requiredField", "base,lesson,title,description,file_pdf,answer_type,edu_year,edu_month"), (missing.Errors!.Single().Reference, missing.Errors!.Single().Info));

            // Someone else's upload is no file at all.
            var others = await UploadAsync(OtherTeacher, "paper.pdf", TwoPagePdf());
            Assert.Equal("file_pdf", (await source.AddPaperAsync(new() { UserId = Teacher, Token = "t", Body = Body(others) })).Errors!.Single().Info);

            // One paper every 30 seconds.
            Assert.Equal(OperationResult.Succeeded, (await source.AddPaperAsync(new() { UserId = Teacher, Token = "t", Body = Body(pdf) })).OperationResult);
            Assert.Equal("rateLimit-addnew", (await source.AddPaperAsync(new() { UserId = Teacher, Token = "t", Body = Body(pdf) })).Errors!.Single().Info);
        }

        [Fact]
        public async Task AnEditAfterReviewIsLimitedAndSendsThePaperBack()
        {
            // Waiting for review: everything changes and the status stays.
            var pending = await source.EditPaperAsync(new() { UserId = Teacher, Token = "t", Id = 45830, Body = JsonNode.Parse("""{"title":"New title","course":""}""")!.AsObject() });
            Assert.Equal("""{"id":45830,"status":0,"editMode":"full"}""", pending.Data!.ToJsonString());
            Assert.Equal("New title", (await context.Set<PastPaper>().SingleAsync(t => t.Id == 45830)).Title);

            // Published: only the description and files; the paper goes back to review.
            await SetStatusAsync(45830, PastPaperStatus.Confirmed);
            var word = await UploadAsync(Teacher, "new.docx", [9, 9]);
            var limited = await source.EditPaperAsync(new() { UserId = Teacher, Token = "t", Id = 45830, Body = JsonNode.Parse($$"""{"title":"Ignored","description":"Better","file_word":"{{word}}"}""")!.AsObject() });
            Assert.Equal((6, "limited", """["title"]"""), ((int)limited.Data!["status"]!, (string?)limited.Data["editMode"], limited.Data["ignoredFields"]!.ToJsonString()));
            Assert.Contains("reviewed again", (string?)limited.Data["message"], StringComparison.Ordinal);
            var paper = await context.Set<PastPaper>().Include(t => t.Files).SingleAsync(t => t.Id == 45830);
            Assert.Equal(("New title", "Better", PastPaperStatus.Edited), (paper.Title, paper.Description, paper.Status));
            Assert.Equal("stored-1.docx", paper.Files!.Single(t => t.Kind == PastPaperFileKind.QuestionWord).Path);

            // Someone else's paper is not theirs to edit.
            Assert.Equal("accessDenied", (await source.EditPaperAsync(new() { UserId = OtherTeacher, Token = "t", Id = 45830, Body = JsonNode.Parse("""{"title":"x"}""")!.AsObject() })).Errors!.Single().Reference);
        }

        [Fact]
        public async Task FilesAreDeletedOnlyUntilThePaperIsPastReview()
        {
            Assert.Equal("lastMainFile", (await source.DeletePaperAsync(new() { UserId = Teacher, Token = "t", Id = 45830, FileType = "pdf" })).Errors!.Single().Info);
            Assert.Equal(OperationResult.Succeeded, (await source.DeletePaperAsync(new() { UserId = Teacher, Token = "t", Id = 45830, FileType = "word" })).OperationResult);
            Assert.Equal(OperationResult.Succeeded, (await source.DeletePaperAsync(new() { UserId = Teacher, Token = "t", Id = 5953, FileType = "extra" })).OperationResult);
            var paper = await context.Set<PastPaper>().Include(t => t.Files).SingleAsync(t => t.Id == 45830);
            Assert.Equal([PastPaperFileKind.Question], paper.Files!.Select(t => t.Kind));

            await SetStatusAsync(45830, PastPaperStatus.Confirmed);
            Assert.Equal("postAlreadyConfirmed", (await source.DeletePaperAsync(new() { UserId = Teacher, Token = "t", Id = 45830 })).Errors!.Single().Info);

            // While it waits for review the owner deletes it; the row (and its stored files) stay, hidden.
            await SetStatusAsync(45830, PastPaperStatus.Pending);
            Assert.Equal(OperationResult.Succeeded, (await source.DeletePaperAsync(new() { UserId = Teacher, Token = "t", Id = 45830 })).OperationResult);
            Assert.True((await context.Set<PastPaper>().SingleAsync(t => t.Id == 45830)).IsDeleted);
        }

        [Fact]
        public async Task TheAdminPanelSeesAndManagesEveryPaper()
        {
            // Every paper not deleted, with its owner; any paper opens with full edit rights.
            var list = (await source.GetOwnPapersAsync(new() { UserId = OtherTeacher, Token = "t", Manager = true, Query = "uid=56" })).Data!;
            Assert.Equal((1, Teacher), ((int)list["num"]!, (long)list["list"]![0]!["user_"]!));
            var paper = (await source.GetOwnPaperAsync(new() { UserId = OtherTeacher, Token = "t", Manager = true, Id = 45830 })).Data!;
            Assert.Equal(("full", true), ((string?)paper["edit"]!["mode"], (bool)paper["admin"]!));

            // An admin edit after review changes everything and keeps the status.
            await SetStatusAsync(45830, PastPaperStatus.Confirmed);
            var edited = await source.EditPaperAsync(new() { UserId = OtherTeacher, Token = "t", Manager = true, Id = 45830, Body = JsonNode.Parse("""{"title":"Fixed","price_multiplier":2}""")!.AsObject() });
            Assert.Equal((1, "full"), ((int)edited.Data!["status"]!, (string?)edited.Data["editMode"]));
            var saved = await context.Set<PastPaper>().SingleAsync(t => t.Id == 45830);
            Assert.Equal(("Fixed", 2m), (saved.Title, saved.PriceMultiplier));

            // Adding for a teacher: uid names the teacher, who owns it; only teachers can.
            var pdf = await UploadAsync(OtherTeacher, "paper.pdf", TwoPagePdf());
            Assert.Equal("notATeacher", (await source.AddPaperAsync(new() { UserId = OtherTeacher, Token = "t", Manager = true, Body = Body(pdf, extra: """{"uid":"58"}""") })).Errors!.Single().Info);
            var added = await source.AddPaperAsync(new() { UserId = OtherTeacher, Token = "t", Manager = true, Body = Body(pdf, extra: """{"uid":"56"}""") });
            Assert.Equal(Teacher, (await context.Set<PastPaper>().SingleAsync(t => t.Id == (long)added.Data!["id"]!)).OwnerUserId);
        }

        [Fact]
        public async Task AReviewSetsTheStatusAndTheNotesTheTeacherSees()
        {
            var writes = MigrationRoute.Legacy;
            PastPaperAdminService Admin() => new(
                new(serviceProvider.GetRequiredService<IUnitOfWorkProvider>),
                new(serviceProvider.GetRequiredService<IHttpContextAccessor>),
                new(() => NullLogger<PastPaperAdminService>.Instance),
                new(() => new PassThroughMigrationRouter { WriteRoute = writes }),
                new(() => new SourceFactory(source)),
                new(() => throw new NotSupportedException("The review doesn't read files.")));

            // While papers change on gama-api, the review is gama-api's panel's.
            Assert.Equal("notNative", (await Admin().ReviewAsync(new() { Id = 45830, Status = 1 })).Errors!.Single().Reference);

            writes = MigrationRoute.Native;
            var admin = Admin();
            Assert.Equal(OperationResult.NotValid, (await admin.ReviewAsync(new() { Id = 45830, Status = 3 })).OperationResult);
            Assert.Equal(3, (int)(await admin.ReviewAsync(new() { Id = 45830, Status = 3, Notes = ["Add the answers", " "] })).Data!["status"]!);
            var edit = (await source.GetOwnPaperAsync(new() { UserId = Teacher, Token = "t", Id = 45830 })).Data!["edit"]!;
            Assert.Equal("""["Add the answers"]""", edit["reviewNotes"]!.ToJsonString());

            Assert.Equal(5, (int)(await admin.SetScoreAsync(45830, 5)).Data!["ref_score"]!);
            Assert.Equal(OperationResult.NotValid, (await admin.SetScoreAsync(45830, 6)).OperationResult);
            Assert.Equal(1, (int)(await admin.ReviewAsync(new() { Id = 45830, Status = 1 })).Data!["status"]!);
            Assert.Equal((PastPaperStatus.Confirmed, (byte)5), await context.Set<PastPaper>().Where(t => t.Id == 45830).Select(t => ValueTuple.Create(t.Status, t.RefereeScore)).SingleAsync());
        }

        private sealed class SourceFactory(IPastPaperSource native) : GamaEdtech.Common.Service.Factory.IGenericFactory<IPastPaperSource, MigrationRoute>
        {
            public IPastPaperSource? GetProvider(MigrationRoute providerType, bool returnFirstItemIfNotMatch = false) => providerType == MigrationRoute.Native ? native : null;
        }

        [Fact]
        public void TitlesAndDescriptionsAreCleanedAsGamaApiDoes()
        {
            Assert.Equal("Physics 5054, Paper ۲-2", NativePastPaperSource.CorrectTitle("<b>Physics</b> 5054; Paper%20#۲-2!").Replace("2-2", "۲-2", StringComparison.Ordinal));
            Assert.Equal("<p>a</p>b", NativePastPaperSource.CorrectDescription("<p>a</p><div>b</div>"));
        }

        public void Dispose()
        {
            context.Dispose();
            serviceProvider.Dispose();
        }

        private async Task<string> UploadAsync(long userId, string name, byte[] content, string? kind = null)
        {
            var result = await source.UploadPaperFileAsync(new() { UserId = userId, Token = "t", FileName = name, ContentType = "application/octet-stream", Content = content, Kind = kind });
            Assert.Equal(OperationResult.Succeeded, result.OperationResult);
            return (string)result.Data![0]!["file"]!["name"]!;
        }

        private static JsonObject Body(string pdf, string? word = null, string? extra = null)
        {
            var body = JsonNode.Parse($$"""
                {"section":6627,"base":4161,"lesson":4164,"test_type":6320,"answer_type":1,"edu_year":2026,"edu_month":6,
                 "title":"Biology 9700","description":"A real paper","file_pdf":"{{pdf}}","file_word":"{{word}}"}
                """)!.AsObject();
            foreach (var (key, value) in JsonNode.Parse(extra ?? "{}")!.AsObject())
            {
                body[key] = value?.DeepClone();
            }

            return body;
        }

        private async Task SetStatusAsync(long id, PastPaperStatus status)
        {
            var paper = await context.Set<PastPaper>().SingleAsync(t => t.Id == id);
            paper.Status = status;
            _ = await context.SaveChangesAsync();
            context.ChangeTracker.Clear();
        }

        private void Seed()
        {
            ApplicationRole teacherRole = new() { Id = 2, Name = nameof(Role.Teacher), NormalizedName = "TEACHER", ConcurrencyStamp = "2" };
            _ = context.Set<ApplicationRole>().Add(teacherRole);
            foreach (var id in new[] { Teacher, OtherTeacher, Student })
            {
                _ = context.Set<ApplicationUser>().Add(new() { Id = id, UserName = $"user{id}@example.test", NormalizedUserName = $"USER{id}@EXAMPLE.TEST", ProfileVisibility = ProfileVisibility.Private, Enabled = true });
            }

            context.Set<ApplicationUserRole>().AddRange(new ApplicationUserRole { UserId = Teacher, RoleId = 2 }, new ApplicationUserRole { UserId = OtherTeacher, RoleId = 2 });
            _ = context.Set<LegacyTaxonomyItem>().Add(new() { Id = Paper1, Kind = LegacyTaxonomyKinds.Classification, Title = "Paper 1", Parent = $"{Cambridge}", IsPaper = true, SyncDate = DateTimeOffset.UtcNow });
            _ = context.Set<PastPaper>().Add(new()
            {
                Id = 45830,
                CoreOwnerId = 0,
                OwnerUserId = Teacher,
                DisplayOwnerUserId = Teacher,
                Title = "Biology 9700/11",
                Description = "Old",
                Status = PastPaperStatus.Pending,
                CoreBoardId = Cambridge,
                CoreGradeId = ALevel,
                CoreSubjectId = Biology,
                CoreCourseId = 5,
                CorePaperTypeId = Paper1,
                CreationDate = DateTimeOffset.UtcNow.AddDays(-1),
                SyncDate = DateTimeOffset.UtcNow,
                Files =
                [
                    new() { Kind = PastPaperFileKind.Question, Path = "q.pdf", Size = 10 },
                    new() { Kind = PastPaperFileKind.QuestionWord, Path = "q.docx", Size = 10 },
                    new() { Kind = PastPaperFileKind.Extra, Path = "x.pdf", Size = 10, CoreExtraFileId = 5953 },
                ],
            });
            _ = context.SaveChanges();
            context.ChangeTracker.Clear();
        }

        /// <summary>A two-page PDF, written out with its cross-reference table.</summary>
        private static byte[] TwoPagePdf()
        {
            string[] objects =
            [
                "<< /Type /Catalog /Pages 2 0 R >>",
                "<< /Type /Pages /Kids [3 0 R 4 0 R] /Count 2 >>",
                "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 200 200] >>",
                "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 200 200] >>",
            ];
            StringBuilder pdf = new("%PDF-1.4\n");
            List<int> offsets = [];
            for (var i = 0; i < objects.Length; i++)
            {
                offsets.Add(pdf.Length);
                _ = pdf.Append(CultureInfo.InvariantCulture, $"{i + 1} 0 obj\n{objects[i]}\nendobj\n");
            }

            var xref = pdf.Length;
            _ = pdf.Append(CultureInfo.InvariantCulture, $"xref\n0 {objects.Length + 1}\n0000000000 65535 f \n");
            foreach (var offset in offsets)
            {
                _ = pdf.Append(CultureInfo.InvariantCulture, $"{offset:D10} 00000 n \n");
            }

            _ = pdf.Append(CultureInfo.InvariantCulture, $"trailer\n<< /Size {objects.Length + 1} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n");
            return Encoding.ASCII.GetBytes(pdf.ToString());
        }

        /// <summary>The file storage: each stored file gets the next name (<c>stored-N.ext</c>).</summary>
        public class StorageFake : DispatchProxy
        {
            private int stored;

            public static IFileService Create() => Create<IFileService, StorageFake>();

            protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
            {
                ArgumentNullException.ThrowIfNull(targetMethod);
                ArgumentNullException.ThrowIfNull(args);
                if (targetMethod.Name == nameof(IFileService.CreateFileAsync))
                {
                    var request = (CreateFileRequestDto)args[0]!;
                    return Task.FromResult(new ResultData<string?>(OperationResult.Succeeded) { Data = $"stored-{++stored}{Path.GetExtension(request.File.FileName)}" });
                }

                return targetMethod.Name == nameof(IFileService.GetStaticFileUrl)
                    ? $"https://cdn.test/{((FileUriRequestDto)args[0]!).FileId}"
                    : throw new NotSupportedException(targetMethod.Name);
            }
        }
    }
}
