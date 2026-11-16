namespace GamaEdtech.Test.Application
{
    using System.Reflection;

    using GamaEdtech.Application.Interface;
    using GamaEdtech.Application.Service;
    using GamaEdtech.Common.DataAccess.Context;
    using GamaEdtech.Common.DataAccess.Repositories;
    using GamaEdtech.Common.DataAccess.UnitOfWork;
    using GamaEdtech.Data.Dto.File;
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
                new(CdnFake.Create));
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
            Assert.Equal(("Nov", "1405/06/24", "2026-09-15 11:46:32"), ((string?)data["edu_month_title"], (string?)data["subdate_jalali"], (string?)data["subdate"]));
            Assert.Equal(("5", "4", "900002"), ((string?)data["price_multiplier"], (string?)data["ref_score"], (string?)data["ownerIdentity"]));
            Assert.Equal("https://cdn.test/subjectpicture/lPic_bio.jpg", (string?)data["lesson_pic"]);
            Assert.Equal("https://cdn.test/thumbnail/1b1c.png", (string?)data["thumb_pic"]);
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
        }

        [Fact]
        public async Task ThePaperLinkIsItsTitleAndSlug()
        {
            var link = await source.GetPaperLinkAsync(45831);

            Assert.Equal((45831L, "Biology 9700/11 May June 2026", "Biology-970011-May-June-2026", true),
                (link.Data!.Id, link.Data.Title, link.Data.Slug, link.Data.IsPastPaper));
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
            _ = context.Set<ApplicationUser>().Add(new() { Id = 56, UserName = "owner@example.test", NormalizedUserName = "OWNER@EXAMPLE.TEST", FirstName = "Ethan", LastName = "Carter", Gender = GenderType.Male, ProfileVisibility = ProfileVisibility.Private, Enabled = true });
            context.Set<PastPaperFilePrice>().AddRange(
                new PastPaperFilePrice { Id = 1, CoreType = 1, Extensions = "pdf", BasePrice = 50, SyncDate = DateTimeOffset.UtcNow },
                new PastPaperFilePrice { Id = 2, CoreType = 1, Extensions = "doc,docx", BasePrice = 100, SyncDate = DateTimeOffset.UtcNow });

            // Paper 45830 as gama-api shows it, and three papers of a subject for the directory's order.
            var predicted = AddPaper(45830, subject: Biology, paperType: Predicted, year: 2026, title: "Predicted Paper Biology 9700 Paper 3 Advanced Practical Skills 1 for Oct/Nov 2026", isPaper: false);
            predicted.EduMonth = 11;
            predicted.Thumbnail = "uploads/thumbs/1b1c.png";
            predicted.Files = [
                new() { Kind = PastPaperFileKind.Question, Path = "2026/09/38/q.pdf", PageCount = 14, Size = 659_456 },
                new() { Kind = PastPaperFileKind.QuestionWord, Path = "2026/09/38/q.docx", Size = 2_097_152 },
                new() { Kind = PastPaperFileKind.Extra, Path = "2026/09/38/x.pdf", Length = 8, CoreTypeId = ConfidentialInstructions, CoreExtraFileId = 5953, Size = 217_088 },
            ];
            _ = AddPaper(45831, subject: Biology, paperType: Paper1, year: 2026, variant: Variant1, title: "Biology 9700/11 May June 2026");
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
