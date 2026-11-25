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
        private const long QuestionFile = 11;
        private const long WordFile = 12;
        private const long ExtraFile = 13;

        private readonly ServiceProvider serviceProvider;
        private readonly TestApplicationDBContext context;
        private readonly NativePastPaperSource source;
        private readonly IFileService storage = StorageFake.Create();

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
                new(() => storage),
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
            Assert.Equal(("requiredField", "base,lesson,title,description,answer_type,edu_year,edu_month"), (missing.Errors!.Single().Reference, missing.Errors!.Single().Info));

            // The question paper is required, and someone else's upload is no file at all.
            var others = await UploadAsync(OtherTeacher, "paper.pdf", TwoPagePdf());
            Assert.Equal("files", (await source.AddPaperAsync(new() { UserId = Teacher, Token = "t", Body = Body(others) })).Errors!.Single().Info);

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

            // Deactivated: only the description and files; the paper goes back to review.
            await SetStatusAsync(45830, PastPaperStatus.Deactivated);
            var word = await UploadAsync(Teacher, "new.docx", [9, 9]);
            var limited = await source.EditPaperAsync(new() { UserId = Teacher, Token = "t", Id = 45830, Body = JsonNode.Parse($$"""{"title":"Ignored","description":"Better","files":[{"id":12,"upload":"{{word}}"}]}""")!.AsObject() });
            Assert.Equal((6, "limited", """["title"]"""), ((int)limited.Data!["status"]!, (string?)limited.Data["editMode"], limited.Data["ignoredFields"]!.ToJsonString()));
            Assert.Contains("reviewed again", (string?)limited.Data["message"], StringComparison.Ordinal);
            var paper = await context.Set<PastPaper>().Include(t => t.Files).SingleAsync(t => t.Id == 45830);
            Assert.Equal(("New title", "Better", PastPaperStatus.Edited), (paper.Title, paper.Description, paper.Status));
            Assert.Equal("stored-1.docx", paper.Files!.Single(t => t.Kind == PastPaperFileKind.QuestionWord).Path);

            // The replaced Word file is removed from storage.
            Assert.Equal(["PastPaper/q.docx"], ((StorageFake)storage).Removed);

            // Someone else's paper is not theirs to edit.
            Assert.Equal("accessDenied", (await source.EditPaperAsync(new() { UserId = OtherTeacher, Token = "t", Id = 45830, Body = JsonNode.Parse("""{"title":"x"}""")!.AsObject() })).Errors!.Single().Reference);
        }

        [Fact]
        public async Task FilesAreRemovedOnlyUntilThePaperIsPastReview()
        {
            // Past review (deactivated) the owner only replaces files; the question paper is only ever replaced.
            await SetStatusAsync(45830, PastPaperStatus.Deactivated);
            Assert.Equal("postAlreadyConfirmed", (await EditFilesAsync("""[{"id":12,"remove":true}]""")).Errors!.Single().Info);
            await SetStatusAsync(45830, PastPaperStatus.Pending);
            Assert.Equal("lastMainFile", (await EditFilesAsync("""[{"id":11,"remove":true}]""")).Errors!.Single().Info);

            // Waiting for review: removed at once, and their stored files with them.
            Assert.Equal(OperationResult.Succeeded, (await EditFilesAsync("""[{"id":12,"remove":true},{"id":13,"remove":true}]""")).OperationResult);
            var paper = await context.Set<PastPaper>().Include(t => t.Files).SingleAsync(t => t.Id == 45830);
            Assert.Equal([PastPaperFileKind.Question], paper.Files!.Select(t => t.Kind));
            Assert.Equal(["PastPaper/q.docx", "PastPaper/x.pdf"], ((StorageFake)storage).Removed);

            await SetStatusAsync(45830, PastPaperStatus.Confirmed);
            Assert.Equal("postAlreadyConfirmed", (await source.DeletePaperAsync(new() { UserId = Teacher, Token = "t", Id = 45830 })).Errors!.Single().Info);

            // While it waits for review the owner deletes it; the row (and its stored files) stay, hidden.
            await SetStatusAsync(45830, PastPaperStatus.Pending);
            Assert.Equal(OperationResult.Succeeded, (await source.DeletePaperAsync(new() { UserId = Teacher, Token = "t", Id = 45830 })).OperationResult);
            Assert.True((await context.Set<PastPaper>().SingleAsync(t => t.Id == 45830)).IsDeleted);
        }

        [Fact]
        public async Task ATeacherOpensTheFilesOfTheirOwnPaperOnly()
        {
            // Their own paper, waiting for review: a signed link to each stored file, extras by their id.
            var pdf = await source.GetOwnPaperFileLinkAsync(new() { UserId = Teacher, Token = "t", Id = 45830, FileId = QuestionFile });
            var extra = await source.GetOwnPaperFileLinkAsync(new() { UserId = Teacher, Token = "t", Id = 45830, FileId = ExtraFile });
            Assert.Equal(("https://private.test/q.pdf?signed", "https://private.test/x.pdf?signed"), ((string?)pdf.Data!["url"], (string?)extra.Data!["url"]));
            Assert.Equal("fileNotExist", (await source.GetOwnPaperFileLinkAsync(new() { UserId = Teacher, Token = "t", Id = 45830, FileId = 99 })).Errors!.Single().Message);

            // Someone else's paper is refused, whatever the file.
            Assert.Equal("permissionDenied", (await source.GetOwnPaperFileLinkAsync(new() { UserId = OtherTeacher, Token = "t", Id = 45830, FileId = QuestionFile })).Errors!.Single().Reference);
        }

        [Fact]
        public async Task TheAdminPanelSeesAndManagesEveryPaper()
        {
            // Every paper not deleted, with its owner; any paper opens with full edit rights.
            var list = (await source.GetOwnPapersAsync(new() { UserId = OtherTeacher, Token = "t", Manager = true, Query = "uid=56" })).Data!;
            Assert.Equal((1, Teacher), ((int)list["num"]!, (long)list["list"]![0]!["user_"]!));

            // The panel's other filters: the code, paper or not, and the id order.
            Assert.Equal(0, (int)(await source.GetOwnPapersAsync(new() { UserId = OtherTeacher, Token = "t", Manager = true, Query = "uid=56&code=nope" })).Data!["num"]!);
            Assert.Equal(1, (int)(await source.GetOwnPapersAsync(new() { UserId = OtherTeacher, Token = "t", Manager = true, Query = "uid=56&is_paper=0&order=idasc" })).Data!["num"]!);
            Assert.Equal(0, (int)(await source.GetOwnPapersAsync(new() { UserId = OtherTeacher, Token = "t", Manager = true, Query = "uid=56&is_paper=1" })).Data!["num"]!);

            // Sorted by downloads, most first, each row with its count.
            var downloaded = await context.Set<PastPaper>().SingleAsync(t => t.Id == 45830);
            downloaded.DownloadCount = 9;
            _ = await context.SaveChangesAsync();
            context.ChangeTracker.Clear();
            var mostDownloaded = (await source.GetOwnPapersAsync(new() { UserId = OtherTeacher, Token = "t", Manager = true, Query = "order=downloadsdesc&perpage=1" })).Data!["list"]![0]!;
            Assert.Equal(("45830", "9"), ((string?)mostDownloaded["id"], (string?)mostDownloaded["downloads"]));

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

        [Fact]
        public async Task AnEditOfAPublishedPaperWaitsForReviewAndTheWebsiteKeepsThePublishedVersion()
        {
            await SetStatusAsync(45830, PastPaperStatus.Confirmed);
            var pdf = await UploadAsync(Teacher, "new.pdf", TwoPagePdf());
            var extra = await UploadAsync(Teacher, "audio.mp3", [1, 2, 3]);
            var edit = await source.EditPaperAsync(new()
            {
                UserId = Teacher,
                Token = "t",
                Id = 45830,
                Body = JsonNode.Parse($$"""{"title":"Ignored","description":"Better","files":[{"id":11,"upload":"{{pdf}}"},{"role":"extra","type_id":7,"upload":"{{extra}}"}]}""")!.AsObject(),
            });
            Assert.Equal((1, "limited", true), ((int)edit.Data!["status"]!, (string?)edit.Data["editMode"], (bool)edit.Data["pendingEdit"]!));
            Assert.Contains("keeps showing the published version", (string?)edit.Data["message"], StringComparison.Ordinal);

            // The Word file's removal waits too; the PDF is still only replaced.
            Assert.True((bool)(await EditFilesAsync("""[{"id":12,"remove":true}]""")).Data!["pendingEdit"]!);
            Assert.Equal("lastMainFile", (await EditFilesAsync("""[{"id":11,"remove":true}]""")).Errors!.Single().Info);

            // The published paper is untouched: status, description and files.
            var paper = await context.Set<PastPaper>().Include(t => t.Files).SingleAsync(t => t.Id == 45830);
            Assert.Equal((PastPaperStatus.Confirmed, "Old", "Biology 9700/11"), (paper.Status, paper.Description, paper.Title));
            Assert.Equal(["q.docx", "q.pdf", "x.pdf"], paper.Files!.Select(t => t.Path!).Order());
            _ = Assert.NotNull(paper.PendingEditDate);
            context.ChangeTracker.Clear();

            // The edit form shows the edit apart from the published version; the new files open with pending=true.
            var form = (await source.GetOwnPaperAsync(new() { UserId = Teacher, Token = "t", Id = 45830 })).Data!;
            var pending = form["pending_edit"]!;
            Assert.Equal((true, "Better"), ((bool)pending["waiting"]!, (string?)pending["description"]));
            var files = pending["files"]!.AsArray();
            Assert.Equal(["11:question:replaced", "12:word:removed", "extra:added"], files.Select(t => (long)t!["id"]! > 0 ? $"{(long)t["id"]!}:{(string?)t["role"]}:{(string?)t["change"]}" : $"{(string?)t["role"]}:{(string?)t["change"]}"));
            Assert.True((bool)form["edit"]!["canDeleteFiles"]!);
            var newExtraId = (long)files[2]!["id"]!;
            Assert.True(newExtraId < 0);
            var link = await source.GetOwnPaperFileLinkAsync(new() { UserId = Teacher, Token = "t", Id = 45830, FileId = newExtraId, Pending = true });
            Assert.Equal("https://private.test/stored-2.mp3?signed", (string?)link.Data!["url"]);
            var replaced = await source.GetOwnPaperFileLinkAsync(new() { UserId = Teacher, Token = "t", Id = 45830, FileId = QuestionFile, Pending = true });
            Assert.Equal("https://private.test/stored-1.pdf?signed", (string?)replaced.Data!["url"]);

            // Dropping the new extra file before review removes its upload from storage.
            _ = await EditFilesAsync($$"""[{"id":{{newExtraId}},"remove":true}]""");
            Assert.Equal(["PastPaper/stored-2.mp3"], ((StorageFake)storage).Removed);
            Assert.Equal(2, await context.Set<PastPaperPendingFile>().CountAsync());
        }

        [Fact]
        public async Task ApprovingAnEditReplacesThePublishedVersionAndRejectingKeepsIt()
        {
            var writes = MigrationRoute.Native;
            PastPaperAdminService admin = new(
                new(serviceProvider.GetRequiredService<IUnitOfWorkProvider>),
                new(serviceProvider.GetRequiredService<IHttpContextAccessor>),
                new(() => NullLogger<PastPaperAdminService>.Instance),
                new(() => new PassThroughMigrationRouter { WriteRoute = writes }),
                new(() => new SourceFactory(source)),
                new(() => storage));
            var removed = ((StorageFake)storage).Removed;
            await SetStatusAsync(45830, PastPaperStatus.Confirmed);

            // Rejected: the edit and its upload go, the published version stays, the owner sees the notes.
            var answer = await UploadAsync(Teacher, "answer.pdf", [1]);
            _ = await source.EditPaperAsync(new() { UserId = Teacher, Token = "t", Id = 45830, Body = JsonNode.Parse($$"""{"description":"Worse","files":[{"role":"answer","upload":"{{answer}}"}]}""")!.AsObject() });
            Assert.Equal(OperationResult.NotValid, (await admin.ReviewAsync(new() { Id = 45830, Status = 3 })).OperationResult);
            var rejected = await admin.ReviewAsync(new() { Id = 45830, Status = 3, Notes = ["Wrong answers"] });
            Assert.Equal((1, "rejected"), ((int)rejected.Data!["status"]!, (string?)rejected.Data["pendingEdit"]));
            Assert.Equal(["PastPaper/stored-1.pdf"], removed);
            var form = (await source.GetOwnPaperAsync(new() { UserId = Teacher, Token = "t", Id = 45830 })).Data!;
            Assert.Equal((false, "Old", """["Wrong answers"]"""), ((bool)form["pending_edit"]!["waiting"]!, (string?)form["description"], form["pending_edit"]!["reviewNotes"]!.ToJsonString()));
            Assert.Empty(await context.Set<PastPaperPendingFile>().ToListAsync());

            // Approved: the new description, Word file and answer replace the published ones, the extra file goes, and the
            // replaced and removed files leave storage.
            var word = await UploadAsync(Teacher, "word.docx", [2]);
            answer = await UploadAsync(Teacher, "answer.pdf", [3]);
            _ = await source.EditPaperAsync(new() { UserId = Teacher, Token = "t", Id = 45830, Body = JsonNode.Parse($$"""{"description":"Better","files":[{"id":12,"upload":"{{word}}"},{"role":"answer","upload":"{{answer}}"}]}""")!.AsObject() });
            _ = await EditFilesAsync("""[{"id":13,"remove":true}]""");
            var approved = await admin.ReviewAsync(new() { Id = 45830, Status = 1 });
            Assert.Equal((1, "approved"), ((int)approved.Data!["status"]!, (string?)approved.Data["pendingEdit"]));
            context.ChangeTracker.Clear();
            var paper = await context.Set<PastPaper>().Include(t => t.Files).SingleAsync(t => t.Id == 45830);
            Assert.Equal((PastPaperStatus.Confirmed, "Better", default(DateTimeOffset?)), (paper.Status, paper.Description, paper.PendingEditDate));
            Assert.Equal(["q.pdf", "stored-2.docx", "stored-3.pdf"], paper.Files!.Select(t => t.Path!).Order());
            _ = Assert.NotNull(paper.LastModifyDate);
            Assert.Equal(["PastPaper/stored-1.pdf", "PastPaper/q.docx", "PastPaper/x.pdf"], removed);
            Assert.Empty(await context.Set<PastPaperPendingFile>().ToListAsync());

            // The admin panel lists the papers with an edit waiting.
            Assert.Equal(0, (int)(await source.GetOwnPapersAsync(new() { UserId = OtherTeacher, Token = "t", Manager = true, Query = "pending_edit=1" })).Data!["num"]!);
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
                 "title":"Biology 9700","description":"A real paper","files":[{"role":"question","upload":"{{pdf}}"},{"role":"word","upload":"{{word}}"}]}
                """)!.AsObject();
            foreach (var (key, value) in JsonNode.Parse(extra ?? "{}")!.AsObject())
            {
                body[key] = value?.DeepClone();
            }

            return body;
        }

        /// <summary>The teacher's edit of paper 45830 that only changes its files.</summary>
        private Task<ResultData<JsonNode>> EditFilesAsync(string files) =>
            source.EditPaperAsync(new() { UserId = Teacher, Token = "t", Id = 45830, Body = JsonNode.Parse($$"""{"files":{{files}}}""")!.AsObject() });

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
                    new() { Id = QuestionFile, Kind = PastPaperFileKind.Question, Path = "q.pdf", Size = 10 },
                    new() { Id = WordFile, Kind = PastPaperFileKind.QuestionWord, Path = "q.docx", Size = 10 },
                    new() { Id = ExtraFile, Kind = PastPaperFileKind.Extra, Path = "x.pdf", Size = 10, CoreExtraFileId = 5953 },
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

            /// <summary>The files removed from storage, as "container/name".</summary>
            public System.Collections.ObjectModel.Collection<string> Removed { get; } = [];

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

                if (targetMethod.Name == nameof(IFileService.RemoveFileAsync))
                {
                    var request = (RemoveFileRequestDto)args[0]!;
                    Removed.Add($"{request.ContainerType.Name}/{request.FileId}");
                    return Task.FromResult(new ResultData<bool>(OperationResult.Succeeded) { Data = true });
                }

                return targetMethod.Name switch
                {
                    nameof(IFileService.GetFileUrlAsync) => Task.FromResult<Uri?>(new($"https://private.test/{((FileUriRequestDto)args[0]!).FileId}?signed")),
                    nameof(IFileService.GetStaticFileUrl) => $"https://cdn.test/{((FileUriRequestDto)args[0]!).FileId}",
                    _ => throw new NotSupportedException(targetMethod.Name),
                };
            }
        }
    }
}
