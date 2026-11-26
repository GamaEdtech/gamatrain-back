namespace GamaEdtech.Test.Application
{
    using GamaEdtech.Application.Service;
    using GamaEdtech.Common.DataAccess.Context;
    using GamaEdtech.Common.DataAccess.Repositories;
    using GamaEdtech.Common.DataAccess.UnitOfWork;
    using GamaEdtech.Data.Dto.PastPaper;
    using GamaEdtech.Domain.Entity;

    using Microsoft.AspNetCore.Http;
    using Microsoft.EntityFrameworkCore;
    using Microsoft.Extensions.Configuration;
    using Microsoft.Extensions.DependencyInjection;
    using Microsoft.Extensions.Logging.Abstractions;

    using Xunit;

    using static GamaEdtech.Common.Core.Constants;

    using TestApplicationDBContext = GamaEdtech.Test.Application.ExamAnalysisServiceTests.TestApplicationDBContext;

    /// <summary>The admin's past paper base price list: one row per extension, ids after the copied ones.</summary>
    public sealed class PastPaperFilePriceServiceTests : IDisposable
    {
        private readonly ServiceProvider serviceProvider;
        private readonly TestApplicationDBContext context;
        private readonly PastPaperFilePriceService service;

        public PastPaperFilePriceServiceTests()
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
                new(() => NullLogger<PastPaperFilePriceService>.Instance));

            context.Set<PastPaperFilePrice>().AddRange(
                new PastPaperFilePrice { Id = 1, CoreType = 1, Extensions = "pdf", BasePrice = 50, SyncDate = DateTimeOffset.UtcNow },
                new PastPaperFilePrice { Id = 2, CoreType = 1, Extensions = "doc,docx", BasePrice = 100, SyncDate = DateTimeOffset.UtcNow },
                new PastPaperFilePrice { Id = 7, CoreType = 2, Extensions = "mp4", BasePrice = 10, SyncDate = DateTimeOffset.UtcNow });
            _ = context.SaveChanges();
            context.ChangeTracker.Clear();
        }

        [Fact]
        public async Task OnlyPaperFilePricesAreListed()
        {
            var result = await service.GetPricesAsync();

            Assert.Equal([(1L, "pdf", 50), (2L, "doc,docx", 100)], result.Data!.Select(t => (t.Id, t.Extensions, t.BasePrice)));
        }

        [Fact]
        public async Task ANewRowTakesTheNextFreeIdAndCleanExtensions()
        {
            var result = await service.ManagePriceAsync(new ManagePastPaperFilePriceRequestDto { Extensions = " .MP3, wav ,mp3", BasePrice = 30 });

            Assert.Equal(OperationResult.Succeeded, result.OperationResult);
            Assert.Equal(8L, result.Data);
            context.ChangeTracker.Clear();
            var row = await context.Set<PastPaperFilePrice>().SingleAsync(t => t.Id == 8);
            Assert.Equal(((byte)1, 0, "mp3,wav", 30), (row.CoreType, row.CorePaperTypeId, row.Extensions, row.BasePrice));
        }

        [Fact]
        public async Task AnEditChangesTheRowAndKeepsItsId()
        {
            var result = await service.ManagePriceAsync(new ManagePastPaperFilePriceRequestDto { Id = 2, Extensions = "docx", BasePrice = 120 });

            Assert.Equal(2L, result.Data);
            context.ChangeTracker.Clear();
            var row = await context.Set<PastPaperFilePrice>().SingleAsync(t => t.Id == 2);
            Assert.Equal(("docx", 120), (row.Extensions, row.BasePrice));
        }

        [Fact]
        public async Task AnExtensionIsPricedByOneRowOnly()
        {
            var result = await service.ManagePriceAsync(new ManagePastPaperFilePriceRequestDto { Extensions = "PDF,zip", BasePrice = 10 });

            Assert.Equal((OperationResult.NotValid, "extensions"), (result.OperationResult, result.Errors!.Single().Reference));
            Assert.Contains("pdf", result.Errors!.Single().Message, StringComparison.Ordinal);
            Assert.Equal(3, await context.Set<PastPaperFilePrice>().CountAsync());
        }

        [Theory]
        [InlineData("", 10, "extensions")]
        [InlineData("p d f", 10, "extensions")]
        [InlineData("averyverylongext", 10, "extensions")]
        [InlineData("zip", -1, "basePrice")]
        [InlineData("zip", 100_001, "basePrice")]
        public async Task ABadRowIsRefused(string extensions, int basePrice, string reference)
        {
            var result = await service.ManagePriceAsync(new ManagePastPaperFilePriceRequestDto { Extensions = extensions, BasePrice = basePrice });

            Assert.Equal((OperationResult.NotValid, reference), (result.OperationResult, result.Errors!.Single().Reference));
        }

        [Theory]
        [InlineData(7L)]
        [InlineData(99L)]
        public async Task OnlyAPaperFilePriceCanBeEditedOrDeleted(long id)
        {
            Assert.Equal(OperationResult.NotFound, (await service.ManagePriceAsync(new ManagePastPaperFilePriceRequestDto { Id = id, Extensions = "zip", BasePrice = 1 })).OperationResult);
            Assert.Equal(OperationResult.NotFound, (await service.DeletePriceAsync(id)).OperationResult);
        }

        [Fact]
        public async Task DeletingARowMakesItsExtensionsFree()
        {
            Assert.True((await service.DeletePriceAsync(1)).Data);

            context.ChangeTracker.Clear();
            Assert.Equal([2L, 7L], await context.Set<PastPaperFilePrice>().OrderBy(t => t.Id).Select(t => t.Id).ToListAsync());
        }

        public void Dispose()
        {
            context.Dispose();
            serviceProvider.Dispose();
        }
    }
}
