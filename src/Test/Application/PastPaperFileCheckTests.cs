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
    using GamaEdtech.Domain.Entity;
    using GamaEdtech.Domain.Enumeration;

    using Microsoft.AspNetCore.Http;
    using Microsoft.EntityFrameworkCore;
    using Microsoft.Extensions.Configuration;
    using Microsoft.Extensions.DependencyInjection;
    using Microsoft.Extensions.Logging.Abstractions;

    using Xunit;

    using static GamaEdtech.Common.Core.Constants;

    using TestApplicationDBContext = GamaEdtech.Test.Application.ExamAnalysisServiceTests.TestApplicationDBContext;

    /// <summary>
    /// Checking the copied past paper files: each row's file is looked up by its path in the private bucket, its size is kept,
    /// missing and unused files are counted, the listing is read page by page, and a failed listing changes nothing.
    /// </summary>
    public sealed class PastPaperFileCheckTests : IDisposable
    {
        private readonly ServiceProvider serviceProvider;
        private readonly TestApplicationDBContext context;
        private readonly SortedDictionary<string, long> bucket = new(StringComparer.Ordinal);
        private readonly PastPaperFileCheckService service;
        private bool listingFails;

        public PastPaperFileCheckTests()
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
                new(() => NullLogger<PastPaperFileCheckService>.Instance),
                new(() => BucketFake.Create(bucket, () => listingFails)));
        }

        [Fact]
        public async Task EachFileGetsItsSizeAndMissingAndUnusedFilesAreCounted()
        {
            AddPaper(1, "2025/06/22/q.pdf", "a.pdf", "gone.pdf");
            bucket["2025/06/22/q.pdf"] = 644_000;
            bucket["a.pdf"] = 12_000;
            bucket["left-over.pdf"] = 5;

            var result = await service.CheckAsync();

            Assert.Equal(OperationResult.Succeeded, result.OperationResult);
            Assert.Equal((3, 3, 2, 1, 1), (result.Data!.StoredFiles, result.Data.Rows, result.Data.Found, result.Data.Missing, result.Data.Unused));
            Assert.Equal(["gone.pdf"], result.Data.MissingExamples);
            context.ChangeTracker.Clear();
            var files = await context.Set<PastPaperFile>().OrderBy(t => t.Path).ToListAsync();
            Assert.Equal([("2025/06/22/q.pdf", 644_000), ("a.pdf", 12_000), ("gone.pdf", null)], files.Select(t => (t.Path!, t.Size)));
            Assert.All(files, t => Assert.NotNull(t.CheckDate));
        }

        [Fact]
        public async Task AListingOfManyPagesIsReadWhole()
        {
            for (var i = 0; i < 2500; i++)
            {
                bucket[$"f{i:D5}.pdf"] = i;
            }

            AddPaper(1, "f02499.pdf");

            var result = await service.CheckAsync();

            Assert.Equal((2500, 1, 2499), (result.Data!.StoredFiles, result.Data.Found, result.Data.Unused));
        }

        [Fact]
        public async Task AFailedListingChangesNothing()
        {
            AddPaper(1, "q.pdf");
            listingFails = true;

            var result = await service.CheckAsync();

            Assert.Equal(OperationResult.Failed, result.OperationResult);
            context.ChangeTracker.Clear();
            Assert.Null((await context.Set<PastPaperFile>().SingleAsync()).CheckDate);
        }

        public void Dispose()
        {
            context.Dispose();
            serviceProvider.Dispose();
        }

        private void AddPaper(long id, params string[] paths)
        {
            _ = context.Set<PastPaper>().Add(new()
            {
                Id = id,
                CoreOwnerId = 91999,
                Title = "Physics",
                Files = [.. paths.Select((t, i) => new PastPaperFile { Kind = i switch { 0 => PastPaperFileKind.Question, 1 => PastPaperFileKind.Answer, _ => PastPaperFileKind.Extra }, Path = t })],
            });
            _ = context.SaveChanges();
            context.ChangeTracker.Clear();
        }

        /// <summary>The private bucket: stored names and sizes, listed in name order a page at a time.</summary>
        public class BucketFake : DispatchProxy
        {
            private SortedDictionary<string, long> files = [];
            private Func<bool> fails = () => false;

            public static IFileService Create(SortedDictionary<string, long> files, Func<bool> fails)
            {
                var fake = Create<IFileService, BucketFake>();
                ((BucketFake)(object)fake).files = files;
                ((BucketFake)(object)fake).fails = fails;
                return fake;
            }

            protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
            {
                ArgumentNullException.ThrowIfNull(targetMethod);
                ArgumentNullException.ThrowIfNull(args);
                if (targetMethod.Name != nameof(IFileService.ListFilesAsync))
                {
                    throw new InvalidOperationException("The check only lists files.");
                }

                if (fails())
                {
                    return Task.FromResult(new ResultData<ListFilesResponseDto>(OperationResult.Failed) { Errors = [new() { Message = "bucket unreachable" }] });
                }

                var request = (ListFilesRequestDto)args[0]!;
                Assert.Equal(ContainerType.PastPaper, request.ContainerType);
                var page = files.Where(t => request.StartAfter is null || string.CompareOrdinal(t.Key, request.StartAfter) > 0).Take(request.MaxCount + 1).ToList();
                var more = page.Count > request.MaxCount;
                List<StoredFileDto> list = [.. page.Take(request.MaxCount).Select(t => new StoredFileDto { Name = t.Key, Size = t.Value })];
                return Task.FromResult(new ResultData<ListFilesResponseDto>(OperationResult.Succeeded)
                {
                    Data = new() { Files = list, NextStartAfter = more ? list[^1].Name : null },
                });
            }
        }
    }
}
