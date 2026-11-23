namespace GamaEdtech.Test.Application
{
    using GamaEdtech.Application.Service;
    using GamaEdtech.Common.DataAccess.Context;
    using GamaEdtech.Common.DataAccess.Repositories;
    using GamaEdtech.Common.DataAccess.UnitOfWork;
    using GamaEdtech.Domain.Entity;
    using GamaEdtech.Domain.Entity.Identity;
    using GamaEdtech.Domain.Enumeration;

    using Microsoft.AspNetCore.Http;
    using Microsoft.Extensions.Configuration;
    using Microsoft.Extensions.DependencyInjection;
    using Microsoft.Extensions.Logging.Abstractions;

    using Xunit;

    using TestApplicationDBContext = GamaEdtech.Test.Application.ExamAnalysisServiceTests.TestApplicationDBContext;

    // "Report a problem": the message's length, one open report per person and item, titles for the list, closing.
    public sealed class ContentReportServiceTests : IDisposable
    {
        private readonly ServiceProvider serviceProvider;
        private readonly TestApplicationDBContext context;
        private readonly ContentReportService service;

        public ContentReportServiceTests()
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

            var now = DateTimeOffset.UtcNow;
            _ = context.Set<ApplicationUser>().Add(new ApplicationUser { Id = 1, UserName = "ana", NormalizedUserName = "ANA", FirstName = "Ana", LastName = "Lee", ProfileVisibility = ProfileVisibility.Private, Enabled = true });
            _ = context.Set<Exam>().Add(new Exam { Id = 2560, Title = "Nucleic Acids", Status = 1, CreationDate = now, SyncDate = now });
            _ = context.SaveChanges();
            service = new(new(serviceProvider.GetRequiredService<IUnitOfWorkProvider>), new(serviceProvider.GetRequiredService<IHttpContextAccessor>),
                new(() => NullLogger<ContentReportService>.Instance));
        }

        [Fact]
        public async Task AReportIsCheckedOpenedOnceAndClosed()
        {
            const string message = "Question 4 has two correct options in the key.";
            Assert.Equal("message", (await service.CreateAsync(new() { UserId = 1, ContentType = ReportedContentType.Exam, ContentId = 2560, ReportType = 3, Message = "too short" })).Errors!.Single().Reference);
            Assert.Equal("reportType", (await service.CreateAsync(new() { UserId = 1, ContentType = ReportedContentType.Exam, ContentId = 2560, ReportType = 0, Message = message })).Errors!.Single().Reference);

            var id = (await service.CreateAsync(new() { UserId = 1, ContentType = ReportedContentType.Exam, ContentId = 2560, ReportType = 3, Message = message })).Data;
            Assert.Equal("alreadyReported", (await service.CreateAsync(new() { UserId = 1, ContentType = ReportedContentType.Exam, ContentId = 2560, ReportType = 3, Message = message })).Errors!.Single().Reference);

            var list = (await service.GetReportsAsync(new() { Status = ContentReportStatus.Open })).Data!;
            Assert.Equal(("Nucleic Acids", "Ana Lee", (byte)3), (list.Items.Single().ContentTitle, list.Items.Single().UserName, list.Items.Single().ReportType));

            Assert.True((await service.SetStatusAsync(id, ContentReportStatus.Resolved, "Fixed the key.", 9)).Data);
            Assert.Equal(0, (await service.GetReportsAsync(new() { Status = ContentReportStatus.Open })).Data!.Total);

            // Once closed, the same person can report the same item again.
            Assert.True((await service.CreateAsync(new() { UserId = 1, ContentType = ReportedContentType.Exam, ContentId = 2560, ReportType = 1, Message = message })).Data > 0);

            // The website's old names for gama-api still work.
            Assert.Equal(ReportedContentType.Paper, ReportedContentType.FromLegacyName("test"));
            Assert.Equal(ReportedContentType.ExamQuestion, ReportedContentType.FromLegacyName("examTest"));
            Assert.Null(ReportedContentType.FromLegacyName("nope"));
        }

        public void Dispose()
        {
            context.Dispose();
            serviceProvider.Dispose();
        }
    }
}
