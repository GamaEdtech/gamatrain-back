namespace GamaEdtech.Test.Application
{
    using System.Reflection;

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

    using Microsoft.AspNetCore.Http;
    using Microsoft.EntityFrameworkCore;
    using Microsoft.Extensions.Configuration;
    using Microsoft.Extensions.DependencyInjection;
    using Microsoft.Extensions.Logging.Abstractions;

    using Xunit;

    using static GamaEdtech.Common.Core.Constants;

    using TestApplicationDBContext = GamaEdtech.Test.Application.ExamAnalysisServiceTests.TestApplicationDBContext;

    /// <summary>
    /// The native paper page and lists from our copy, against what gama-api answers for the same paper (paper 45830: a
    /// 14-page PDF priced 190, a Word file priced 370, no answer, one free extra file). The owner is our user, pictures are
    /// our CDN's, and only published papers are served.
    /// </summary>
    public sealed class NativePastPaperSourceTests : IDisposable
    {
        private const int Cambridge = 6627;
        private const int ALevel = 4161;
        private const int Biology = 4164;
        private const int Predicted = 8344;
        private const int Paper1 = 6320;
        private const int Paper2 = 6321;
        private const int Worksheets = 310;
        private const int NoVariant = 7813;
        private const int Variant1 = 7814;
        private const int ConfidentialInstructions = 10517;

        private readonly ServiceProvider serviceProvider;
        private readonly TestApplicationDBContext context;
        private readonly NativePastPaperSource source;

        public NativePastPaperSourceTests()
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
                new(CdnFake.Create),
                new(ExamsFake.Create),
                new(() => new Microsoft.AspNetCore.DataProtection.EphemeralDataProtectionProvider()),
                new(() => null!),
                new(() => null!));
            Seed();
        }

        [Fact]
        public async Task APapersPageIsWhatGamaApiShows()
        {
            var result = await source.GetPaperAsync(45830);

            Assert.Equal(OperationResult.Succeeded, result.OperationResult);
            var data = result.Data!;
            Assert.Equal("45830", (string?)data["id"]);
            Assert.Equal("Predicted-Paper-Biology-9700-Paper-3-Advanced-Practical-Skills-1-for-OctNov-2026", (string?)data["title_url"]);
            Assert.Equal(("Cambridge", "AS & A Level", "Biology (9700)", "Predicted Papers"),
                ((string?)data["section_title"], (string?)data["base_title"], (string?)data["lesson_title"], (string?)data["test_type_title"]));
            Assert.False((bool)data["is_paper"]!);
            // Dates are UTC only; gama-api's Jalali copies are not sent.
            Assert.Equal(("Nov", false, "2026-09-15 11:46:32"), ((string?)data["edu_month_title"], data.ContainsKey("subdate_jalali"), (string?)data["subdate"]));
            // The exams built from the paper, with their status, in place of the raw exam_id; the owner's handle is ours.
            Assert.Equal("""[{"id":"2527","status":"1"}]""", data["exams"]!.ToJsonString());
            Assert.False(data.ContainsKey("exam_id"));
            Assert.Equal("@ethan", (string?)data["ownerIdentity"]);
            Assert.Equal(("5", "4"), ((string?)data["price_multiplier"], (string?)data["ref_score"]));
            Assert.Equal("https://cdn.test/subjectpicture/lPic_bio.jpg", (string?)data["lesson_pic"]);
            Assert.Equal("https://cdn.test/thumbnail/1b1c.png", (string?)data["thumb_pic"]);
            Assert.Equal("AS &amp; A Level &lt;b&gt;Biology&lt;/b&gt;", (string?)data["description"]);
            Assert.False((bool)data["singleSell"]!);

            // The owner is our user; the username is never published and an owner without an avatar has none.
            Assert.Equal(("0", "Ethan", "Carter", "1"), ((string?)data["username"], (string?)data["first_name"], (string?)data["last_name"], (string?)data["sex"]));
            Assert.Null(data["avatar"]);

            var files = data["files"]!;
            Assert.Equal("""{"exist":true,"size":"644 KB","ext":"pdf","price":190}""", files["pdf"]!.ToJsonString());
            Assert.Equal("""{"exist":true,"size":"2 MB","ext":"docx","price":370}""", files["word"]!.ToJsonString());
            Assert.Equal("""{"exist":false,"size":0,"ext":false,"price":0}""", files["answer"]!.ToJsonString());
            Assert.Equal("""[{"id":"5953","type":"10517","length":"8","type_title":"Confidental Instructions","size":"212 KB","ext":"pdf","price":0}]""",
                files["extra"]!.ToJsonString());
        }

        [Fact]
        public async Task OnlyPublishedPapersAreServed()
        {
            Assert.Equal(OperationResult.Failed, (await source.GetPaperAsync(45900)).OperationResult);
            Assert.Equal(OperationResult.Failed, (await source.GetPaperAsync(1)).OperationResult);

            var list = (await source.SearchPapersAsync("lesson=4164&is_paper=true&directory=true")).Data!;
            Assert.DoesNotContain(list["list"]!.AsArray(), t => (string?)t!["id"] == "45900");
        }

        [Fact]
        public async Task TheDirectoryListsPapersNewestFirstThenByPaperAndVariant()
        {
            var result = await source.SearchPapersAsync("lesson=4164&is_paper=true&directory=true&page=1&perpage=10");

            Assert.Equal(OperationResult.Succeeded, result.OperationResult);
            var list = result.Data!["list"]!.AsArray();
            Assert.Equal(3, (int)result.Data["num"]!);
            // 2026 before 2025; in 2026, Paper 1 before Paper 2, and variant "nv" (no number) before "1".
            Assert.Equal(["45832", "45831", "45833"], list.Select(t => (string?)t!["id"]));
            var first = list[0]!;
            Assert.Equal(("Paper 1", "nv", true), ((string?)first["test_type_title"], (string?)first["variant_title"], (bool)first["is_paper"]!));
            Assert.Equal("[]", first["files"]!["extra"]!.ToJsonString());
            Assert.False((bool)first["in_file"]!);
            Assert.Equal("[2527]", (string?)list.Single(t => (string?)t!["id"] == "45831")!["exam_id"]);
        }

        [Fact]
        public async Task TheDirectoryYearsCountEachYearsPapersNewestFirst()
        {
            var result = await source.SearchPapersAsync("lesson=4164&is_paper=true&directory=true&facet=years");

            Assert.Equal(OperationResult.Succeeded, result.OperationResult);
            Assert.Equal(3, (int)result.Data!["num"]!);
            Assert.Equal([("2026", 2), ("2025", 1)], result.Data["years"]!.AsArray().Select(t => ((string?)t!["edu_year"], (int)t["num"]!)));
            Assert.Null(result.Data["list"]);

            // The other filters still apply, and a year's papers come from the list with edu_year.
            var year = await source.SearchPapersAsync("lesson=4164&is_paper=true&directory=true&edu_year=2025&page=1&perpage=50");
            Assert.Equal(["45833"], year.Data!["list"]!.AsArray().Select(t => (string?)t!["id"]));
        }

        [Fact]
        public async Task TheResourcesDirectoryGroupsByNonPaperType()
        {
            var result = await source.SearchPapersAsync("lesson=4164&is_paper=false&directory=true");

            var data = result.Data!;
            Assert.Equal(("4164", "Biology (9700)", 0), ((string?)data["lesson"], (string?)data["lesson_title"], (int)data["num"]!));
            var groups = data["list"]!.AsArray();
            // Groups in the types' list order, then id; a type with no papers still has its (empty) group.
            Assert.Equal([$"{Worksheets}", $"{Predicted}"], groups.Select(t => (string?)t!["type_id"]));
            Assert.Empty(groups[0]!["items"]!.AsArray());
            Assert.Equal(["45830"], groups[1]!["items"]!.AsArray().Select(t => (string?)t!["id"]));
        }

        [Fact]
        public async Task ASubjectListAlsoShowsTopicsLinkedFromAnotherBoard()
        {
            // An Edexcel paper on topic 9001, linked to Cambridge Biology's topic 9000.
            _ = context.Set<LegacyTaxonomyItem>().Add(Item(9000, LegacyTaxonomyKinds.Topic, "Cells", $"{Biology}"));
            _ = context.Set<LegacyTaxonomyItem>().Add(Item(9001, LegacyTaxonomyKinds.Topic, "Cell biology", "7000"));
            _ = context.Set<LegacyTaxonomyRelation>().Add(new() { Id = 1, FromId = 9000, ToId = 9001, SyncDate = DateTimeOffset.UtcNow });
            _ = AddPaper(46000, subject: 7000, paperType: Paper1, year: 2024, topics: "9001");
            _ = await context.SaveChangesAsync();

            var list = (await source.SearchPapersAsync("lesson=4164&perpage=50")).Data!["list"]!.AsArray();

            Assert.Contains(list, t => (string?)t!["id"] == "46000");
            // A list item carries the raw topic field, never the decoded topics (only the page has them).
            Assert.All(list, t => Assert.False(t!.AsObject().ContainsKey("topics")));
        }

        [Fact]
        public async Task RelatedPapersAreAPlusThenTheSessionsThenTheSubjectsPublishedOnes()
        {
            // An A+ paper of the same type comes first; then the same grade and subject within a month of the session, then
            // without the month. Never the paper itself, a draft, or one twice.
            var aPlus = AddPaper(45834, subject: Biology, paperType: Paper1, year: 2024, title: "Biology A+");
            aPlus.RefereeScore = 5;
            _ = await context.SaveChangesAsync();
            context.ChangeTracker.Clear();

            var result = await source.GetRelatedPapersAsync("test", 45831);

            Assert.Equal(OperationResult.Succeeded, result.OperationResult);
            var list = result.Data!;
            Assert.Equal(["45834", "45833", "45832", "45830"], list.Select(t => (string?)t!["id"]));
            var first = list[0]!;
            Assert.Equal(("Biology A+", "Biology-A", "5", "6", "Ethan", "Carter", "4"),
                ((string?)first["title"], (string?)first["title_url"], (string?)first["referee_score"], (string?)first["edu_month"],
                (string?)first["first_name"], (string?)first["last_name"], (string?)first["q_file_pages"]));
            Assert.Equal("https://cdn.test/thumbnail/1b1c.png", (string?)list[3]!["thumb_pic"]);
        }

        [Fact]
        public async Task ThePaperLinkIsItsTitleAndSlug()
        {
            var link = await source.GetPaperLinkAsync(45831);

            Assert.Equal((45831L, "Biology 9700/11 May June 2026", "Biology-970011-May-June-2026", true),
                (link.Data!.Id, link.Data.Title, link.Data.Slug, link.Data.IsPastPaper));
        }

        [Fact]
        public async Task ATeachersListIsTheirOwnPapersInAnyStatusNewestFirst()
        {
            await OwnAsync(45831, 45832, 45900);
            AddPaper(45901, subject: Biology, paperType: Paper1, year: 2026).IsDeleted = true; // they deleted it themselves
            var removed = AddPaper(45902, subject: Biology, paperType: Paper1, year: 2026); // an admin deleted it
            removed.IsDeleted = true;
            removed.Status = PastPaperStatus.Deleted;
            removed.OwnerUserId = 56;
            _ = await context.SaveChangesAsync();
            await OwnAsync(45901);

            var result = await source.GetOwnPapersAsync(new() { UserId = 56, Token = "t", Query = "page=1&perpage=10" });

            Assert.Equal(OperationResult.Succeeded, result.OperationResult);
            var list = result.Data!["list"]!.AsArray();
            Assert.Equal(["45902", "45900", "45832", "45831"], list.Select(t => (string?)t!["id"]));
            Assert.Equal(4, (int)result.Data["num"]!);
            var item = list.Single(t => (string?)t!["id"] == "45900")!;
            Assert.Equal(("0", "Biology (9700)", "Paper 1", true, false), ((string?)item["status"], (string?)item["lesson_title"], (string?)item["test_type_title"],
                (bool)item["q_file"]!, (bool)item["a_file"]!));

            // gama-api's exact filters and paging.
            Assert.Equal(["45900"], (await source.GetOwnPapersAsync(new() { UserId = 56, Token = "t", Query = "status=0" })).Data!["list"]!.AsArray().Select(t => (string?)t!["id"]));
            var second = (await source.GetOwnPapersAsync(new() { UserId = 56, Token = "t", Query = "?perpage=3&page=2&lesson=4164" })).Data!;
            Assert.Equal(["45831"], second["list"]!.AsArray().Select(t => (string?)t!["id"]));
            Assert.Equal(4, (int)second["num"]!);
            Assert.Empty((await source.GetOwnPapersAsync(new() { UserId = 57, Token = "t" })).Data!["list"]!.AsArray());
        }

        [Fact]
        public async Task TheEditFormGetsTheOwnersPaperWithWhatTheyMayChange()
        {
            await OwnAsync(45831, 45900);

            // Waiting for review: everything, files deletable; no view is counted.
            var pending = (await source.GetOwnPaperAsync(new() { UserId = 56, Token = "t", Id = 45900 })).Data!;
            Assert.True((bool)pending["owner"]!);
            Assert.Equal("""{"mode":"full","editableFields":null,"canDeleteFiles":true,"message":null,"reviewNotes":[]}""", pending["edit"]!.ToJsonString());
            Assert.Equal(0, (await context.Set<PastPaper>().SingleAsync(t => t.Id == 45900)).ViewCount);

            // Published: the description and files only; a file's removal waits for review with the rest of the edit.
            var edit = (await source.GetOwnPaperAsync(new() { UserId = 56, Token = "t", Id = 45831 })).Data!["edit"]!;
            Assert.Equal(("limited", true), ((string?)edit["mode"], (bool)edit["canDeleteFiles"]!));
            Assert.Contains("keeps showing the published version", (string?)edit["message"], StringComparison.Ordinal);
            Assert.Equal("""["description","file_pdf","file_word","file_answer","file_extra"]""", edit["editableFields"]!.ToJsonString());

            // Someone else's unpublished paper is denied; their published one has no edit.
            Assert.Equal("permissionDenied", (await source.GetOwnPaperAsync(new() { UserId = 57, Token = "t", Id = 45900 })).Errors!.Single().Reference);
            var other = (await source.GetOwnPaperAsync(new() { UserId = 57, Token = "t", Id = 45831 })).Data!;
            Assert.Equal((false, false), ((bool)other["owner"]!, other.ContainsKey("edit")));
        }

        [Fact]
        public void APaperSentBackCarriesTheReviewsNotes()
        {
            var paper = new PastPaper { Status = PastPaperStatus.NeedsChanges, ReviewNote = "Please fix:\n- Add the answers\n- Page 2 is missing" };
            Assert.Equal("""["Add the answers","Page 2 is missing"]""", NativePastPaperSource.EditInfo(paper)["reviewNotes"]!.ToJsonString());
            Assert.Equal(["One note"], NativePastPaperSource.ReviewNotes("One note"));
            // A note still holding a gama-api ticket id has no text to show.
            Assert.Empty(NativePastPaperSource.ReviewNotes("123456"));
            // The copy keeps a ticket's text without its tags.
            Assert.Equal("- Add the answers\n- Fix & resend", PastPaperImportService.ReviewNoteText("<p>- Add the answers</p>\n<p>- Fix &amp; resend</p>"));
        }

        private async Task OwnAsync(params long[] ids)
        {
            foreach (var paper in await context.Set<PastPaper>().Where(t => ids.Contains(t.Id)).ToListAsync())
            {
                paper.OwnerUserId = 56;
            }

            _ = await context.SaveChangesAsync();
            context.ChangeTracker.Clear();
        }

        public void Dispose()
        {
            context.Dispose();
            serviceProvider.Dispose();
        }

        private static LegacyTaxonomyItem Item(int id, short kind, string title, string parent = "", string? metadata = null, bool? isPaper = null, string? picture = null) => new()
        {
            Id = id,
            Kind = kind,
            Title = title,
            Parent = parent,
            Metadata = metadata,
            IsPaper = isPaper,
            Picture = picture,
            SyncDate = DateTimeOffset.UtcNow,
            Parents = [],
        };

        private void Seed()
        {
            context.Set<LegacyTaxonomyItem>().AddRange(
                Item(Cambridge, LegacyTaxonomyKinds.Board, "Cambridge"),
                Item(ALevel, LegacyTaxonomyKinds.Grade, "AS & A Level", $"{Cambridge}"),
                Item(Biology, LegacyTaxonomyKinds.Subject, "Biology (9700)", $"0,{ALevel}", picture: "lPic_bio.jpg"),
                Item(Predicted, LegacyTaxonomyKinds.Classification, "Predicted Papers", $"{Cambridge}", isPaper: false),
                Item(Worksheets, LegacyTaxonomyKinds.Classification, "Worksheets", $"{Cambridge}", isPaper: false),
                Item(Paper1, LegacyTaxonomyKinds.Classification, "Paper 1", $"{Cambridge}", isPaper: true),
                Item(Paper2, LegacyTaxonomyKinds.Classification, "Paper 2", $"{Cambridge}", isPaper: true),
                Item(NoVariant, LegacyTaxonomyKinds.Variant, "nv"),
                Item(Variant1, LegacyTaxonomyKinds.Variant, "1"),
                Item(ConfidentialInstructions, LegacyTaxonomyKinds.ExtraFileType, "Confidental Instructions", metadata: """{"price_multiplier":"0"}"""));
            _ = context.Set<ApplicationUser>().Add(new() { Id = 56, UserName = "owner@example.test", NormalizedUserName = "OWNER@EXAMPLE.TEST", FirstName = "Ethan", LastName = "Carter", Handle = "ethan", Gender = GenderType.Male, ProfileVisibility = ProfileVisibility.Private, Enabled = true });
            context.Set<PastPaperFilePrice>().AddRange(
                new PastPaperFilePrice { Id = 1, CoreType = 1, Extensions = "pdf", BasePrice = 50, SyncDate = DateTimeOffset.UtcNow },
                new PastPaperFilePrice { Id = 2, CoreType = 1, Extensions = "doc,docx", BasePrice = 100, SyncDate = DateTimeOffset.UtcNow });

            // Paper 45830 as gama-api shows it, and three papers of a subject for the directory's order.
            var predicted = AddPaper(45830, subject: Biology, paperType: Predicted, year: 2026, title: "Predicted Paper Biology 9700 Paper 3 Advanced Practical Skills 1 for Oct/Nov 2026", isPaper: false);
            predicted.EduMonth = 11;
            predicted.Description = "AS & A Level <b>Biology</b>";
            predicted.CoreExamIds = "[2527,2600]";
            predicted.Thumbnail = "uploads/thumbs/1b1c.png";
            predicted.Files = [
                new() { Kind = PastPaperFileKind.Question, Path = "2026/09/38/q.pdf", PageCount = 14, Size = 659_456 },
                new() { Kind = PastPaperFileKind.QuestionWord, Path = "2026/09/38/q.docx", Size = 2_097_152 },
                new() { Kind = PastPaperFileKind.Extra, Path = "2026/09/38/x.pdf", Length = 8, CoreTypeId = ConfidentialInstructions, CoreExtraFileId = 5953, Size = 217_088 },
            ];
            AddPaper(45831, subject: Biology, paperType: Paper1, year: 2026, variant: Variant1, title: "Biology 9700/11 May June 2026").CoreExamIds = "[2527]";
            _ = AddPaper(45832, subject: Biology, paperType: Paper1, year: 2026, variant: NoVariant);
            _ = AddPaper(45833, subject: Biology, paperType: Paper2, year: 2025);
            var draft = AddPaper(45900, subject: Biology, paperType: Paper1, year: 2026);
            draft.Status = PastPaperStatus.Pending;
            _ = context.SaveChanges();
            context.ChangeTracker.Clear();
        }

        private PastPaper AddPaper(long id, int subject, int paperType, short year, short? variant = null, string? title = null, bool isPaper = true, string? topics = null)
        {
            PastPaper paper = new()
            {
                Id = id,
                CoreOwnerId = 900001,
                CoreTeacherId = 900002,
                DisplayOwnerUserId = 56,
                Title = title ?? $"Paper {id}",
                Status = PastPaperStatus.Confirmed,
                CoreBoardId = Cambridge,
                CoreGradeId = ALevel,
                CoreSubjectId = subject,
                CorePaperTypeId = paperType,
                CoreTopicIds = topics,
                Variant = variant,
                EduYear = year,
                EduMonth = 6,
                IsPaper = isPaper,
                RefereeScore = 4,
                PriceMultiplier = 5,
                CreationDate = new DateTimeOffset(2026, 9, 15, 11, 46, 32, TimeSpan.Zero),
                LastModifyDate = new DateTimeOffset(2026, 9, 15, 11, 46, 32, TimeSpan.Zero),
                SyncDate = DateTimeOffset.UtcNow,
                Files = [new() { Kind = PastPaperFileKind.Question, Path = $"{id}.pdf", PageCount = 4, Size = 1000 }],
            };
            _ = context.Set<PastPaper>().Add(paper);
            return paper;
        }

        /// <summary>gama-api's exams through the switch: exam 2527 is published, any other exam cannot be read.</summary>
        public class ExamsFake : DispatchProxy
        {
            public static IMigrationRouter Create() => Create<IMigrationRouter, ExamsFake>();

            protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
            {
                ArgumentNullException.ThrowIfNull(args);
                var exam = (Func<IExamSource, Task<ResultData<System.Text.Json.Nodes.JsonObject>>>)args[2]!;
                return exam(ExamSourceFake.Create());
            }
        }

        /// <summary>The exam source behind <see cref="ExamsFake"/>.</summary>
        public class ExamSourceFake : DispatchProxy
        {
            public static IExamSource Create() => Create<IExamSource, ExamSourceFake>();

            protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
            {
                ArgumentNullException.ThrowIfNull(args);
                var request = (GamaEdtech.Data.Dto.Game.ExamInformationRequestDto)args[0]!;
                return Task.FromResult(request.ExamId == 2527
                    ? new ResultData<System.Text.Json.Nodes.JsonObject>(OperationResult.Succeeded) { Data = new() { ["id"] = "2527", ["status"] = "1" } }
                    : new ResultData<System.Text.Json.Nodes.JsonObject>(OperationResult.Failed));
            }
        }

        /// <summary>Our CDN: a stored file's permanent link, none without a file.</summary>
        public class CdnFake : DispatchProxy
        {
            public static IFileService Create() => Create<IFileService, CdnFake>();

            protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
            {
                ArgumentNullException.ThrowIfNull(targetMethod);
                ArgumentNullException.ThrowIfNull(args);
                var request = (FileUriRequestDto)args[0]!;
                return string.IsNullOrEmpty(request.FileId) ? null : $"https://cdn.test/{request.ContainerType.Name.ToLowerInvariant()}/{request.FileId}";
            }
        }
    }
}
