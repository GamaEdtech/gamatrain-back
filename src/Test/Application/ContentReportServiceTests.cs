namespace GamaEdtech.Test.Application
{
    using GamaEdtech.Application.Interface;
    using GamaEdtech.Application.Service;
    using GamaEdtech.Common.Data;
    using GamaEdtech.Common.Data.Enumeration;
    using GamaEdtech.Common.DataAccess.Context;
    using GamaEdtech.Common.DataAccess.Repositories;
    using GamaEdtech.Common.DataAccess.UnitOfWork;
    using GamaEdtech.Data.Dto.ApplicationSettings;
    using GamaEdtech.Data.Dto.Email;
    using GamaEdtech.Domain.Entity;
    using GamaEdtech.Domain.Entity.Identity;
    using GamaEdtech.Domain.Enumeration;

    using Microsoft.AspNetCore.Http;
    using Microsoft.Extensions.Configuration;
    using Microsoft.Extensions.DependencyInjection;
    using Microsoft.Extensions.Logging.Abstractions;

    using Xunit;

    using static GamaEdtech.Common.Core.Constants;

    using Fake = GamaEdtech.Test.ExamImport.McpSignOutTests.Fake;
    using TestApplicationDBContext = GamaEdtech.Test.Application.ExamAnalysisServiceTests.TestApplicationDBContext;

    // "Report a problem": the message's length, one open report per person and item, titles for the list, closing, and the
    // email that tells the reporter the outcome.
    public sealed class ContentReportServiceTests : IDisposable
    {
        private readonly ServiceProvider serviceProvider;
        private readonly TestApplicationDBContext context;
        private readonly ContentReportService service;
        private readonly List<SendEmailRequestDto> sent = [];
        private readonly Dictionary<string, string?> templates = new()
        {
            [nameof(ApplicationSettingsDto.ContentReportResolvedEmailTemplate)] = new ApplicationSettingsDto().ContentReportResolvedEmailTemplate,
            [nameof(ApplicationSettingsDto.ContentReportDismissedEmailTemplate)] = new ApplicationSettingsDto().ContentReportDismissedEmailTemplate,
        };

        private int scheduled;

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
            _ = context.Set<ApplicationUser>().Add(new ApplicationUser { Id = 1, UserName = "ana", NormalizedUserName = "ANA", Email = "ana@example.test", FirstName = "Ana", LastName = "Lee", ProfileVisibility = ProfileVisibility.Private, Enabled = true });
            _ = context.Set<Exam>().Add(new Exam { Id = 2560, Title = "Nucleic Acids", Status = 1, CreationDate = now, SyncDate = now });
            _ = context.Set<PastPaper>().Add(new PastPaper { Id = 45830, Title = "Physics <P2> 2023", CreationDate = now, SyncDate = now });
            _ = context.SaveChanges();

            var settings = Fake.Create<IApplicationSettingsService>(new()
            {
                ["GetSettingAsync"] = args => Task.FromResult(new ResultData<string?>(OperationResult.Succeeded) { Data = templates.GetValueOrDefault((string)args[0]!) }),
            });
            var email = Fake.Create<IEmailService>(new()
            {
                ["GetNoReplyEmail"] = _ => "noreply@site.test",
                ["SendEmailAsync"] = args =>
                {
                    sent.Add((SendEmailRequestDto)args[0]!);
                    return Task.FromResult(new ResultData<GamaEdtech.Common.Data.Void>(OperationResult.Succeeded));
                },
            });
            var jobs = Fake.Create<IJobScheduler>(new() { ["Schedule"] = _ => scheduled++ });
            var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["ContentReports:SiteUrl"] = "https://site.test/" }).Build();
            service = new(new(serviceProvider.GetRequiredService<IUnitOfWorkProvider>), new(serviceProvider.GetRequiredService<IHttpContextAccessor>),
                new(() => NullLogger<ContentReportService>.Instance), new(() => settings), new(() => email), new(() => jobs), new(() => configuration));
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

        [Fact]
        public async Task ClosingAReportEmailsTheReporterOnceWithTheOutcomeButNeverTheStaffNote()
        {
            var id = await ReportAsync(ReportedContentType.Paper, 45830);

            Assert.True((await service.SetStatusAsync(id, ContentReportStatus.Resolved, "Internal: re-uploaded the PDF.", 9)).Data);
            Assert.True((await service.SetStatusAsync(id, ContentReportStatus.Resolved, "Edited the note only.", 9)).Data);
            Assert.Equal(1, scheduled);

            await service.SendClosedEmailAsync(id);
            var email = Assert.Single(sent);
            Assert.Equal(["ana@example.test"], email.EmailAddresses);
            Assert.Contains("fixed", email.Subject, StringComparison.Ordinal);
            Assert.Contains("Hi Ana Lee", email.Body, StringComparison.Ordinal);
            Assert.Contains("<a href=\"https://site.test/paper/45830/Physics-P2-2023\">Physics &lt;P2&gt; 2023</a>", email.Body, StringComparison.Ordinal);
            Assert.DoesNotContain("Internal", email.Body, StringComparison.Ordinal);
        }

        [Fact]
        public async Task ADismissedReportGetsTheDismissedEmail()
        {
            var id = await ReportAsync(ReportedContentType.Exam, 2560);
            _ = await service.SetStatusAsync(id, ContentReportStatus.Dismissed, null, 9);

            await service.SendClosedEmailAsync(id);
            Assert.Contains("didn't find anything to change", Assert.Single(sent).Body, StringComparison.Ordinal);
        }

        [Fact]
        public async Task NoEmailWhenReopenedBeforeTheJobRanOrTheTemplateIsEmpty()
        {
            var id = await ReportAsync(ReportedContentType.Exam, 2560);
            _ = await service.SetStatusAsync(id, ContentReportStatus.Resolved, null, 9);
            _ = await service.SetStatusAsync(id, ContentReportStatus.Open, null, 9);
            await service.SendClosedEmailAsync(id);

            _ = await service.SetStatusAsync(id, ContentReportStatus.Resolved, null, 9);
            templates[nameof(ApplicationSettingsDto.ContentReportResolvedEmailTemplate)] = null;
            await service.SendClosedEmailAsync(id);

            Assert.Empty(sent);
            Assert.Equal(2, scheduled);
        }

        [Theory]
        [InlineData("Paper", 45830L, "Physics <P2> 2023", "https://site.test/paper/45830/Physics-P2-2023")]
        [InlineData("Tutorial", 7L, null, "https://site.test/tutorial/7")]
        [InlineData("ExamQuestion", 9L, "Q-9", "https://site.test")]
        [InlineData("Multimedia", 9L, null, "https://site.test")]
        public void TheLinkIsTheItemsPageOrTheSite(string type, long id, string? title, string link) =>
            Assert.Equal(link, service.ContentLink(type.ToEnumeration<ReportedContentType, byte>(), id, title));

        private async Task<long> ReportAsync(ReportedContentType type, long contentId) =>
            (await service.CreateAsync(new() { UserId = 1, ContentType = type, ContentId = contentId, ReportType = 3, Message = "Question 4 has two correct options in the key." })).Data;

        public void Dispose()
        {
            context.Dispose();
            serviceProvider.Dispose();
        }
    }
}
