namespace GamaEdtech.Test.Application
{
    using GamaEdtech.Application.Interface;
    using GamaEdtech.Application.Service;
    using GamaEdtech.Common.Data;
    using GamaEdtech.Common.DataAccess.Context;
    using GamaEdtech.Common.DataAccess.Repositories;
    using GamaEdtech.Common.DataAccess.UnitOfWork;
    using GamaEdtech.Data.Dto.Email;
    using GamaEdtech.Domain.Entity;
    using GamaEdtech.Domain.Entity.Identity;
    using GamaEdtech.Domain.Enumeration;

    using Microsoft.AspNetCore.DataProtection;
    using Microsoft.AspNetCore.Http;
    using Microsoft.EntityFrameworkCore;
    using Microsoft.Extensions.Configuration;
    using Microsoft.Extensions.DependencyInjection;
    using Microsoft.Extensions.Logging.Abstractions;

    using Xunit;

    using static GamaEdtech.Common.Core.Constants;

    using Fake = GamaEdtech.Test.ExamImport.McpSignOutTests.Fake;
    using TestApplicationDBContext = GamaEdtech.Test.Application.ExamAnalysisServiceTests.TestApplicationDBContext;

    // The exam review reminder: once per finished exam, only to someone who hasn't reviewed it, at most one a day.
    public sealed class ExamReviewReminderTests : IDisposable
    {
        private readonly ServiceProvider serviceProvider;
        private readonly TestApplicationDBContext context;
        private readonly NudgeService service;
        private readonly List<SendEmailRequestDto> sent = [];

        public ExamReviewReminderTests()
        {
            Environment.SetEnvironmentVariable("Test", "True");
            ServiceCollection services = new();
            _ = services.AddLogging();
            var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["EnableAudit"] = bool.FalseString,
                ["Notifications:UnsubscribeBaseUrl"] = "https://site.test/unsubscribe",
            }).Build();
            _ = services.AddSingleton<IConfiguration>(configuration);
            _ = services.AddSingleton<IHttpContextAccessor, HttpContextAccessor>();
            _ = services.AddSingleton<TestApplicationDBContext>();
            _ = services.AddSingleton<IEntityContext>(provider => provider.GetRequiredService<TestApplicationDBContext>());
            _ = services.AddTransient(typeof(IRepository<,>), typeof(GenericEntityRepository<,>));
            _ = services.AddSingleton<IUnitOfWorkProvider, UnitOfWorkProvider>();
            serviceProvider = services.BuildServiceProvider();
            context = serviceProvider.GetRequiredService<TestApplicationDBContext>();

            var now = DateTimeOffset.UtcNow;
            context.Set<ApplicationUser>().AddRange(
                new ApplicationUser { Id = 1, UserName = "ana", NormalizedUserName = "ANA", FirstName = "Ana", Email = "ana@example.test", ProfileVisibility = ProfileVisibility.Private, Enabled = true },
                new ApplicationUser { Id = 2, UserName = "ben", NormalizedUserName = "BEN", FirstName = "Ben", Email = "ben@example.test", NudgesOptedOutAt = now, ProfileVisibility = ProfileVisibility.Private, Enabled = true });
            context.Set<Exam>().AddRange(
                new Exam { Id = 10, Title = "Biology <Paper 1>", Status = 1, CreationDate = now, SyncDate = now },
                new Exam { Id = 12, Title = "Chemistry", Status = 1, CreationDate = now, SyncDate = now });
            context.Set<ExamAttempt>().AddRange(
                new ExamAttempt { Id = 100, UserId = 1, ExamId = 10, Status = 1, CreationDate = now, SyncDate = now },
                new ExamAttempt { Id = 101, UserId = 1, ExamId = 12, Status = 1, CreationDate = now, SyncDate = now },
                new ExamAttempt { Id = 102, UserId = 2, ExamId = 10, Status = 1, CreationDate = now, SyncDate = now });
            _ = context.Set<NudgeTemplate>().Add(new()
            {
                NudgeType = NudgeType.ExamReview,
                Subject = "How was [EXAM_TITLE]?",
                Body = "Hi [RECEIVER_NAME], rate [EXAM_TITLE]: [CTA_URL]",
                CtaLabel = "Rate this exam",
                CtaUrl = "https://site.test/exam/[EXAM_ID]/[EXAM_SLUG]#reviews",
                IsActive = true,
                CreationDate = now,
            });
            _ = context.SaveChanges();
            context.ChangeTracker.Clear();

            var email = Fake.Create<IEmailService>(new()
            {
                ["GetNoReplyEmail"] = _ => "noreply@site.test",
                ["SendEmailAsync"] = args =>
                {
                    sent.Add((SendEmailRequestDto)args[0]!);
                    return Task.FromResult(new ResultData<GamaEdtech.Common.Data.Void>(OperationResult.Succeeded));
                },
            });
            service = new(new(serviceProvider.GetRequiredService<IUnitOfWorkProvider>), new(serviceProvider.GetRequiredService<IHttpContextAccessor>),
                new(() => null!), new(() => NullLogger<NudgeService>.Instance), new(() => email),
                new(() => new EphemeralDataProtectionProvider()), new(() => configuration), new(() => null!));
        }

        [Fact]
        public async Task AFinishedExamIsRemindedOnceWithALinkToItsReviews()
        {
            Assert.True((await service.SendExamReviewNudgeAsync(1, 10)).Data);
            Assert.False((await service.SendExamReviewNudgeAsync(1, 10)).Data);

            var email = Assert.Single(sent);
            Assert.Equal("How was Biology <Paper 1>?", email.Subject);
            Assert.StartsWith("Hi Ana, rate Biology &lt;Paper 1&gt;: https://site.test/exam/10/Biology-Paper-1#reviews", email.Body, StringComparison.Ordinal);
            Assert.Contains("Unsubscribe", email.Body, StringComparison.Ordinal);
            Assert.Equal(["ana@example.test"], email.EmailAddresses);
        }

        [Fact]
        public async Task SeveralExamsInADayGetOneEmail()
        {
            _ = await service.SendExamReviewNudgeAsync(1, 10);
            Assert.False((await service.SendExamReviewNudgeAsync(1, 12)).Data);
            _ = Assert.Single(sent);
        }

        [Fact]
        public async Task NoReminderAfterAReviewAnOptOutOrWithTheTemplateOff()
        {
            _ = context.Set<ExamReview>().Add(new() { ExamId = 10, UserId = 1, Rating = 5, CreationDate = DateTimeOffset.UtcNow });
            _ = await context.SaveChangesAsync();
            Assert.False((await service.SendExamReviewNudgeAsync(1, 10)).Data);
            Assert.False((await service.SendExamReviewNudgeAsync(2, 10)).Data);

            await context.Set<NudgeTemplate>().ForEachAsync(t => t.IsActive = false);
            _ = await context.SaveChangesAsync();
            Assert.False((await service.SendExamReviewNudgeAsync(1, 12)).Data);
            Assert.Empty(sent);
        }

        public void Dispose()
        {
            context.Dispose();
            serviceProvider.Dispose();
        }
    }
}
