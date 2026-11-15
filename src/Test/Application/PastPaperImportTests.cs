namespace GamaEdtech.Test.Application
{
    using System.Reflection;

    using GamaEdtech.Application.Service;
    using GamaEdtech.Common.Data;
    using GamaEdtech.Common.DataAccess.Context;
    using GamaEdtech.Common.DataAccess.Repositories;
    using GamaEdtech.Common.DataAccess.UnitOfWork;
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
    /// Copying gama-api's past papers: each paper keeps its gama-api id and is linked to its owner by <c>CoreId</c>, its
    /// files keep gama-api's paths, a second run updates instead of duplicating (and follows files added or removed in
    /// gama-api), and nothing is read without a configured database. The user and paper ids here are made up.
    /// </summary>
    public sealed class PastPaperImportTests : IDisposable
    {
        private readonly ServiceProvider serviceProvider;
        private readonly TestApplicationDBContext context;
        private readonly Dictionary<string, List<Dictionary<string, object?>>> gamaApiDb = new(StringComparer.Ordinal);
        private readonly PastPaperImportService service;
        private bool configured = true;
        private string? maxPapers;

        public PastPaperImportTests()
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

            service = new(
                new(serviceProvider.GetRequiredService<IUnitOfWorkProvider>),
                new(serviceProvider.GetRequiredService<IHttpContextAccessor>),
                new(() => NullLogger<PastPaperImportService>.Instance),
                new(() => GamaApiDbFake.Create(gamaApiDb, () => configured)),
                new(() => new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Migration:LegacyDatabase:MaxPastPapers"] = maxPapers,
                }).Build()));
        }

        [Fact]
        public async Task APaperIsCopiedWithItsFilesAndLinkedToTheUserWithThatCoreId()
        {
            AddUser(10, coreId: 91001);
            AddPaper(31617, owner: 91001, title: "Physics 9702 P1", status: 1, questionFile: "2025/06/22/q.pdf", answerFile: "a.pdf", wordFile: "2025/06/22/q.docx");

            var result = await service.ImportAsync();

            Assert.Equal(OperationResult.Succeeded, result.OperationResult);
            Assert.Equal((1, 1, 0, 0, 3), (result.Data!.PapersRead, result.Data.PapersAdded, result.Data.PapersUpdated, result.Data.PapersWithoutUser, result.Data.FilesAdded));

            var paper = await PaperAsync(31617);
            Assert.Equal((91001L, 10L, 8001L), (paper.CoreOwnerId, paper.OwnerUserId!.Value, paper.CoreTeacherId!.Value));
            Assert.Equal(("Physics 9702 P1", "9702", 6627, 22, 6529), (paper.Title, paper.Code, paper.CoreBoardId, paper.CoreGradeId, paper.CoreSubjectId));
            Assert.Equal(PastPaperStatus.Confirmed, paper.Status);
            Assert.Null(paper.CoreCourseId);
            Assert.Equal(1.5m, paper.PriceMultiplier);
            // gama-api's MySQL runs in UTC.
            Assert.Equal(new DateTimeOffset(2025, 6, 1, 12, 0, 0, TimeSpan.Zero), paper.CreationDate);

            var files = paper.Files!.OrderBy(t => t.Kind.Value).Select(t => (t.Kind, t.Path, t.Extension)).ToList();
            Assert.Equal([
                (PastPaperFileKind.Question, "2025/06/22/q.pdf", "pdf"),
                (PastPaperFileKind.Answer, "a.pdf", "pdf"),
                (PastPaperFileKind.QuestionWord, "2025/06/22/q.docx", "docx")], files);
        }

        [Fact]
        public async Task AMissingFileMarkerIsNotAFile()
        {
            AddPaper(31617, owner: 91001, title: "Maths", questionFile: "q.pdf", answerFile: "fileMissed", wordFile: "");

            _ = await service.ImportAsync();

            var paper = await PaperAsync(31617);
            Assert.Equal(PastPaperFileKind.Question, Assert.Single(paper.Files!).Kind);
            Assert.Null(paper.OwnerUserId);
        }

        [Fact]
        public async Task ASecondRunUpdatesTheCopyAndFollowsItsFiles()
        {
            AddPaper(31617, owner: 91001, title: "Old title", questionFile: "q.pdf", answerFile: "a.pdf");
            _ = await service.ImportAsync();

            var row = gamaApiDb["tests"][0];
            row["title"] = "New title";
            row["a_file"] = "";
            row["q_file"] = "2026/01/02/q.pdf";
            row["q_file_word"] = "w.doc";
            AddUser(11, coreId: 91001);

            var result = await service.ImportAsync();

            Assert.Equal((0, 1), (result.Data!.PapersAdded, result.Data.PapersUpdated));
            Assert.Equal((1, 1, 1), (result.Data.FilesAdded, result.Data.FilesUpdated, result.Data.FilesRemoved));
            var paper = await PaperAsync(31617);
            Assert.Equal(("New title", 11L), (paper.Title, paper.OwnerUserId!.Value));
            Assert.Equal(["2026/01/02/q.pdf", "w.doc"], paper.Files!.OrderBy(t => t.Kind.Value).Select(t => t.Path));
            _ = Assert.Single(context.Set<PastPaper>());
        }

        [Fact]
        public async Task ExtraFilesAreCopiedOnceAndFollowGamaApi()
        {
            AddPaper(31617, owner: 91001, title: "English", questionFile: "q.pdf");
            gamaApiDb["tests_extra_files"] =
            [
                new() { ["id"] = 5L, ["test_id"] = 31617L, ["type"] = 6351, ["file"] = "2025/06/22/audio.mp3", ["length"] = 120 },
                new() { ["id"] = 6L, ["test_id"] = 31617L, ["type"] = 6705, ["file"] = "insert.pdf", ["length"] = null },
                new() { ["id"] = 7L, ["test_id"] = 99999L, ["type"] = 6705, ["file"] = "orphan.pdf", ["length"] = null },
            ];

            var first = await service.ImportAsync();
            Assert.Equal((3, 1), (first.Data!.ExtraFilesRead, first.Data.ExtraFilesWithoutPaper));

            gamaApiDb["tests_extra_files"].RemoveAt(1);
            var second = await service.ImportAsync();

            Assert.Equal(1, second.Data!.FilesRemoved);
            var extra = Assert.Single((await PaperAsync(31617)).Files!, t => t.Kind == PastPaperFileKind.Extra);
            Assert.Equal(("2025/06/22/audio.mp3", "mp3", 6351, 120, 5L), (extra.Path, extra.Extension, extra.CoreTypeId!.Value, extra.Length!.Value, extra.CoreExtraFileId!.Value));
        }

        [Fact]
        public async Task ManyPapersAreReadInBatches()
        {
            for (var id = 1; id <= 2500; id++)
            {
                AddPaper(id, owner: 91001, title: $"Paper {id}", questionFile: $"{id}.pdf");
            }

            var result = await service.ImportAsync();

            Assert.Equal(2500, result.Data!.PapersRead);
            Assert.Equal(2500, await context.Set<PastPaper>().CountAsync());
            Assert.Equal(2500, await context.Set<PastPaperFile>().CountAsync());
        }

        [Fact]
        public async Task ATestServerCopiesOnlyTheFirstPapers()
        {
            maxPapers = "1000";
            for (var id = 1; id <= 1500; id++)
            {
                AddPaper(id, owner: 91001, title: $"Paper {id}", questionFile: $"{id}.pdf");
            }

            gamaApiDb["tests_extra_files"] =
            [
                new() { ["id"] = 1L, ["test_id"] = 10L, ["type"] = 6705, ["file"] = "kept.pdf" },
                new() { ["id"] = 2L, ["test_id"] = 1200L, ["type"] = 6705, ["file"] = "left-out.pdf" },
            ];

            var result = await service.ImportAsync();

            Assert.Equal((1000, 1), (result.Data!.PapersRead, result.Data.ExtraFilesWithoutPaper));
            Assert.Equal(1000, await context.Set<PastPaper>().CountAsync());
            Assert.Equal(1000L, await context.Set<PastPaper>().MaxAsync(t => t.Id));
        }

        [Fact]
        public async Task NothingIsReadWithoutAConfiguredDatabase()
        {
            configured = false;
            AddPaper(31617, owner: 91001, title: "Physics", questionFile: "q.pdf");

            var result = await service.ImportAsync();

            Assert.Equal(OperationResult.NotValid, result.OperationResult);
            Assert.Empty(context.Set<PastPaper>());
        }

        public void Dispose()
        {
            context.Dispose();
            serviceProvider.Dispose();
        }

        private void AddUser(long id, long coreId)
        {
            _ = context.Set<ApplicationUser>().Add(new() { Id = id, UserName = $"user{id}", NormalizedUserName = $"USER{id}", ProfileVisibility = ProfileVisibility.Private, CoreId = coreId, Enabled = true });
            _ = context.SaveChanges();
            context.ChangeTracker.Clear();
        }

        private void AddPaper(long id, long owner, string title, int status = 0, string? questionFile = null, string? answerFile = null, string? wordFile = null)
        {
            if (!gamaApiDb.TryGetValue("tests", out var rows))
            {
                gamaApiDb["tests"] = rows = [];
            }

            rows.Add(new()
            {
                ["id"] = id,
                ["user_"] = owner,
                ["teacher_"] = 8001L,
                ["title"] = title,
                ["description"] = "",
                ["status"] = status,
                ["bit_delete"] = 0,
                ["section"] = 6627,
                ["base"] = 22,
                ["lesson"] = 6529,
                ["course"] = 0,
                ["code"] = "9702",
                ["price_multiplier"] = 1.5f,
                ["q_file"] = questionFile,
                ["q_file_ext"] = questionFile is null ? null : "pdf",
                ["a_file"] = answerFile,
                ["q_file_word"] = wordFile,
                ["subdate"] = new DateTime(2025, 6, 1, 12, 0, 0, DateTimeKind.Unspecified),
            });
        }

        private async Task<PastPaper> PaperAsync(long id)
        {
            context.ChangeTracker.Clear();
            return await context.Set<PastPaper>().Include(t => t.Files).SingleAsync(t => t.Id == id);
        }

        /// <summary>gama-api's database: rows per table, read in id order after an id, a batch at a time.</summary>
        public class GamaApiDbFake : DispatchProxy
        {
            private Dictionary<string, List<Dictionary<string, object?>>> tables = [];
            private Func<bool> isConfigured = () => true;

            public static ILegacyDatabaseReader Create(Dictionary<string, List<Dictionary<string, object?>>> tables, Func<bool> isConfigured)
            {
                var fake = Create<ILegacyDatabaseReader, GamaApiDbFake>();
                ((GamaApiDbFake)(object)fake).tables = tables;
                ((GamaApiDbFake)(object)fake).isConfigured = isConfigured;
                return fake;
            }

            protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
            {
                ArgumentNullException.ThrowIfNull(targetMethod);
                if (targetMethod.Name == "get_IsConfigured")
                {
                    return isConfigured();
                }

                ArgumentNullException.ThrowIfNull(args);
                var (table, afterId, batchSize) = ((string)args[0]!, (long)args[1]!, (int)args[2]!);
                IReadOnlyList<IReadOnlyDictionary<string, object?>> rows = [.. (tables.TryGetValue(table, out var all) ? all : [])
                    .Where(t => Convert.ToInt64(t["id"], System.Globalization.CultureInfo.InvariantCulture) > afterId)
                    .OrderBy(t => Convert.ToInt64(t["id"], System.Globalization.CultureInfo.InvariantCulture))
                    .Take(batchSize)
                    .Select(t => (IReadOnlyDictionary<string, object?>)new Dictionary<string, object?>(t, StringComparer.Ordinal))];
                return Task.FromResult(new ResultData<IReadOnlyList<IReadOnlyDictionary<string, object?>>>(OperationResult.Succeeded) { Data = rows });
            }
        }
    }
}
