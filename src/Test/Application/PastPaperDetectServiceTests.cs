namespace GamaEdtech.Test.Application
{
    using System.Globalization;
    using System.Text;

    using GamaEdtech.Application.Interface;
    using GamaEdtech.Application.Service;
    using GamaEdtech.Common.Data;
    using GamaEdtech.Common.DataAccess.Context;
    using GamaEdtech.Common.DataAccess.Repositories;
    using GamaEdtech.Common.DataAccess.UnitOfWork;
    using GamaEdtech.Data.Dto.Provider.Ai;
    using GamaEdtech.Data.Dto.Taxonomy;
    using GamaEdtech.Domain.Entity;
    using GamaEdtech.Domain.Enumeration;

    using Microsoft.AspNetCore.Http;
    using Microsoft.Extensions.Configuration;
    using Microsoft.Extensions.DependencyInjection;
    using Microsoft.Extensions.Logging.Abstractions;

    using Xunit;

    using static GamaEdtech.Common.Core.Constants;

    using Fake = GamaEdtech.Test.ExamImport.McpSignOutTests.Fake;
    using TestApplicationDBContext = GamaEdtech.Test.Application.ExamAnalysisServiceTests.TestApplicationDBContext;

    /// <summary>
    /// Reading a question paper's cover for the add form: Cambridge's printed codes map to the board, grade, subject, paper
    /// type and variant ids without AI; a cover without such a code goes to the AI model; a mark scheme is flagged.
    /// </summary>
    public sealed class PastPaperDetectServiceTests : IDisposable
    {
        private const int Cambridge = 6627;
        private const int ALevel = 4161;
        private const int Igcse = 22;
        private const int Biology = 4164;
        private const int Paper1 = 6320;
        private const int OtherPaper1 = 8679;
        private const int Variant1 = 7814;
        private const int NoVariant = 7813;

        private readonly ServiceProvider serviceProvider;
        private readonly TestApplicationDBContext context;
        private string? aiAnswer;
        private int aiCalls;

        public PastPaperDetectServiceTests()
        {
            Environment.SetEnvironmentVariable("Test", "True");
            ServiceCollection services = new();
            _ = services.AddLogging();
            _ = services.AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["EnableAudit"] = bool.FalseString }).Build());
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
        public async Task ACambridgeCoverFillsEveryFieldWithoutAi()
        {
            var pdf = Pdf("Cambridge International AS & A Level", "BIOLOGY 9700/11", "Paper 1 Multiple Choice", "May/June 2025", "1 hour 15 minutes");

            var result = await Service().DetectAsync(pdf);

            Assert.Equal(OperationResult.Succeeded, result.OperationResult);
            var found = result.Data!;
            Assert.Equal(("pattern", false, "9700/11"), (found.Source, found.IsMarkScheme, found.Code));
            Assert.Equal((Cambridge, ALevel, Biology), (found.Board!.Id, found.Grade!.Id, found.Subject!.Id));

            // Of the board's two "Paper 1" items, the one its papers use; component 11 is paper 1, variant 1.
            Assert.Equal((Paper1, Variant1), (found.Classification!.Id, found.Variant!.Id));
            Assert.Equal((2025, 6), (found.Year, found.Month));
            Assert.Equal("Biology 9700/11 May June 2025", found.Title);
            Assert.Equal(0, aiCalls);
        }

        [Theory]
        [InlineData("3248/01", 1, "nv")]
        [InlineData("9700/42", 4, "2")]
        public void AComponentIsPaperAndVariant(string code, int paper, string variant)
        {
            var facts = PastPaperDetectService.ReadPrintedCodes($"Cambridge O Level URDU {code} October/November 2024 MARK SCHEME")!;
            Assert.Equal((paper, variant, 2024, 11, "O-Level", true), (facts.Paper!.Value, facts.Variant, facts.Year!.Value, facts.Month!.Value, facts.Grade, facts.IsMarkScheme));
        }

        [Fact]
        public async Task ACoverWithoutAKnownCodeIsReadByTheAiModel()
        {
            aiAnswer = "```json\n{\"board\":\"Cambridge\",\"grade\":\"AS & A Level\",\"subject\":\"Biology\",\"code\":null,\"paper\":1,\"variant\":\"1\",\"year\":2024,\"month\":11,\"isMarkScheme\":false}\n```";
            var pdf = Pdf("Cambridge International Examinations", "Biology Paper 1 Multiple Choice", "Winter series 2024");

            var result = await Service().DetectAsync(pdf);

            var found = result.Data!;
            Assert.Equal(("ai", Biology, ALevel, Paper1, 2024, 11), (found.Source, found.Subject!.Id, found.Grade!.Id, found.Classification!.Id, found.Year!.Value, found.Month!.Value));
            Assert.Equal("Biology Paper 1 October November 2024", found.Title);
            Assert.Equal(1, aiCalls);
        }

        [Fact]
        public async Task NothingIsFoundOnAPaperWithoutTextOrWhenTheModelKnowsNothing()
        {
            Assert.Equal(OperationResult.NotFound, (await Service().DetectAsync([1, 2, 3])).OperationResult);

            aiAnswer = "I can't tell.";
            Assert.Equal(OperationResult.NotFound, (await Service().DetectAsync(Pdf("Some worksheet"))).OperationResult);
        }

        [Fact]
        public async Task TheUploadAnswerCarriesTheDetection()
        {
            var upload = new ResultData<System.Text.Json.Nodes.JsonNode>(OperationResult.Succeeded)
            {
                Data = System.Text.Json.Nodes.JsonNode.Parse("""[{"file":{"name":"key","size":3}}]"""),
            };

            var answer = await Service().WithDetectionAsync(upload, Pdf("Cambridge IGCSE", "BIOLOGY 0610/21", "February/March 2026"), question: true);

            var detected = answer.Data![0]!["detected"]!;
            Assert.Equal(("pattern", Igcse, "0610/21", 3), ((string?)detected["source"], (int)detected["grade"]!["id"]!, (string?)detected["code"], (int)detected["month"]!));
        }

        [Theory]
        [InlineData("BIOLOGY 9700/11 MARK SCHEME Maximum Mark: 40", "answer", null)]
        [InlineData("ENGLISH 0500/12 INSERT October/November 2024", "extra", 6351)]
        [InlineData("CONFIDENTIAL INSTRUCTIONS 9700/31", "extra", 10517)]
        [InlineData("Cambridge Physics 9702/42 Solved Paper by our teachers May/June 2025", "question", null)]
        [InlineData("Cambridge BIOLOGY 9700/11 May/June 2025 Insert the correct answer", "question", null)]
        public async Task AnUploadSaysWhatFileItIs(string cover, string role, int? typeId)
        {
            var result = await Service().DetectAsync(Pdf(cover), askAi: false);

            Assert.Equal((role, typeId), (result.Data!.FileRole, result.Data.FileType?.Id));
            Assert.Equal(0, aiCalls);
        }

        public void Dispose()
        {
            context.Dispose();
            serviceProvider.Dispose();
        }

        private PastPaperDetectService Service() => new(
            new(serviceProvider.GetRequiredService<IUnitOfWorkProvider>),
            new(serviceProvider.GetRequiredService<IHttpContextAccessor>),
            new(() => NullLogger<PastPaperDetectService>.Instance),
            new(() => Fake.Create<IAiService>(new()
            {
                ["CompleteAsync"] = _ =>
                {
                    aiCalls++;
                    return Task.FromResult(aiAnswer is null
                        ? new ResultData<ChatCompletionResponseDto>(OperationResult.Failed)
                        : new ResultData<ChatCompletionResponseDto>(OperationResult.Succeeded) { Data = new() { Content = aiAnswer } });
                },
            })));

        private void Seed()
        {
            Item(Cambridge, LegacyTaxonomyKinds.Board, "Cambridge", string.Empty);
            Item(ALevel, LegacyTaxonomyKinds.Grade, "AS & A Level", $"{Cambridge}");
            Item(Igcse, LegacyTaxonomyKinds.Grade, "IGCSE", $"{Cambridge}");
            Item(Biology, LegacyTaxonomyKinds.Subject, "Biology (9700)", $"0,{ALevel}");
            Item(4300, LegacyTaxonomyKinds.Subject, "Biology (0610)", $"0,{Igcse}");
            Item(OtherPaper1, LegacyTaxonomyKinds.Classification, "Paper 1", $"{Cambridge}", isPaper: true);
            Item(Paper1, LegacyTaxonomyKinds.Classification, "Paper 1", $"{Cambridge}", isPaper: true);
            Item(Variant1, LegacyTaxonomyKinds.Variant, "1", string.Empty);
            Item(NoVariant, LegacyTaxonomyKinds.Variant, "nv", string.Empty);
            Item(6351, LegacyTaxonomyKinds.ExtraFileType, "Insert", string.Empty);
            Item(10517, LegacyTaxonomyKinds.ExtraFileType, "Confidental Instructions", string.Empty);
            _ = context.Set<PastPaper>().Add(new()
            {
                Id = 1,
                CoreOwnerId = 0,
                Title = "Biology 9700/12",
                Status = PastPaperStatus.Confirmed,
                CoreBoardId = Cambridge,
                CoreGradeId = ALevel,
                CoreSubjectId = Biology,
                CorePaperTypeId = Paper1,
                CreationDate = DateTimeOffset.UtcNow,
                SyncDate = DateTimeOffset.UtcNow,
            });
            _ = context.SaveChanges();
            context.ChangeTracker.Clear();
        }

        private void Item(int id, short kind, string title, string parent, bool? isPaper = null) => context.Set<LegacyTaxonomyItem>().Add(new()
        {
            Id = id,
            Kind = kind,
            Title = title,
            Parent = parent,
            IsPaper = isPaper,
            ListOrder = id == OtherPaper1 ? 0 : 1,
            SyncDate = DateTimeOffset.UtcNow,
            Parents = [.. LegacyTaxonomySyncService.ParentKeys(parent).Select(t => new LegacyTaxonomyItemParent { ParentKey = t })],
        });

        /// <summary>A one-page PDF with these lines of text (Helvetica), as a real paper's cover reads.</summary>
        private static byte[] Pdf(params string[] lines)
        {
            StringBuilder content = new("BT /F1 12 Tf 72 760 Td 16 TL\n");
            foreach (var line in lines)
            {
                _ = content.Append(CultureInfo.InvariantCulture, $"({line.Replace("(", "\\(", StringComparison.Ordinal).Replace(")", "\\)", StringComparison.Ordinal)}) Tj T*\n");
            }

            _ = content.Append("ET");
            string[] objects =
            [
                "<< /Type /Catalog /Pages 2 0 R >>",
                "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
                "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Resources << /Font << /F1 5 0 R >> >> /Contents 4 0 R >>",
                $"<< /Length {content.Length} >>\nstream\n{content}\nendstream",
                "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>",
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
    }
}
