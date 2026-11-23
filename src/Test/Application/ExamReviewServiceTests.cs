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

    using CdnFake = GamaEdtech.Test.Application.NativePastPaperSourceTests.CdnFake;
    using TestApplicationDBContext = GamaEdtech.Test.Application.ExamAnalysisServiceTests.TestApplicationDBContext;

    // Exam reviews: only a participant reviews, every review is public at once, an admin hides a bad one.
    public sealed class ExamReviewServiceTests : IDisposable
    {
        private readonly ServiceProvider serviceProvider;
        private readonly TestApplicationDBContext context;
        private readonly ExamReviewService service;

        public ExamReviewServiceTests()
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
            context.Set<ApplicationUser>().AddRange(
                new ApplicationUser { Id = 1, UserName = "ana", NormalizedUserName = "ANA", FirstName = "Ana", LastName = "lee", ProfileVisibility = ProfileVisibility.Private, Enabled = true },
                new ApplicationUser { Id = 2, UserName = "ben", NormalizedUserName = "BEN", FirstName = "Ben", LastName = "Ray", ProfileVisibility = ProfileVisibility.Private, Enabled = true },
                new ApplicationUser { Id = 3, UserName = "cy", NormalizedUserName = "CY", FirstName = "Cy", ProfileVisibility = ProfileVisibility.Private, Enabled = true });
            context.Set<Exam>().AddRange(
                new Exam { Id = 10, Title = "Biology", Status = 1, CreationDate = now, SyncDate = now },
                new Exam { Id = 11, Title = "Draft", Status = 6, CreationDate = now, SyncDate = now });
            context.Set<ExamAttempt>().AddRange(
                new ExamAttempt { Id = 100, UserId = 1, ExamId = 10, Status = 1, CreationDate = now, SyncDate = now },
                new ExamAttempt { Id = 101, UserId = 2, ExamId = 10, Status = 1, CreationDate = now, SyncDate = now },
                new ExamAttempt { Id = 102, UserId = 3, ExamId = 10, Status = 0, CreationDate = now, SyncDate = now });
            _ = context.SaveChanges();

            service = new(new(serviceProvider.GetRequiredService<IUnitOfWorkProvider>), new(serviceProvider.GetRequiredService<IHttpContextAccessor>),
                new(() => NullLogger<ExamReviewService>.Instance), new(CdnFake.Create));
        }

        [Fact]
        public async Task OnlyAParticipantReviewsAndAnAdminHidesABadOne()
        {
            // 3 only started the exam; 11 isn't public.
            Assert.Equal("notParticipated", (await service.SaveReviewAsync(new() { UserId = 3, ExamId = 10, Rating = 5 })).Errors!.Single().Reference);
            Assert.Equal("noResult", (await service.SaveReviewAsync(new() { UserId = 1, ExamId = 11, Rating = 5 })).Errors!.Single().Reference);
            Assert.Equal("rating", (await service.SaveReviewAsync(new() { UserId = 1, ExamId = 10, Rating = 6 })).Errors!.Single().Reference);

            var rating = (await service.SaveReviewAsync(new() { UserId = 1, ExamId = 10, Rating = 4 })).Data!;
            var comment = (await service.SaveReviewAsync(new() { UserId = 2, ExamId = 10, Rating = 2, Comment = "  Too hard  " })).Data!;
            Assert.Equal(Status.Confirmed, rating.Status);
            Assert.Equal((Status.Confirmed, "Too hard"), (comment.Status, comment.Comment));

            // Both are public at once, like a school's comments.
            var page = (await service.GetReviewsAsync(10, 1, 1, 10)).Data!;
            Assert.Equal((2, 3.0m, 1), (page.Count, page.Average!.Value, page.CommentCount));
            Assert.Equal([0, 1, 0, 1, 0], page.Distribution);
            Assert.Equal("Ben R.", page.Items.Single().Name);
            Assert.True(page.CanReview);
            Assert.Equal((byte)4, page.Mine!.Rating);

            // An admin hides a bad one; its author editing it doesn't bring it back.
            Assert.True((await service.ModerateAsync(comment.Id, confirm: false, "Not about the exam")).Data);
            var edited = (await service.SaveReviewAsync(new() { UserId = 2, ExamId = 10, Rating = 5, Comment = "Great" })).Data!;
            Assert.Equal((Status.Rejected, "Not about the exam"), (edited.Status, edited.RejectionComment));
            Assert.Equal((1, 0), ((await service.GetReviewsAsync(10, null, 1, 10)).Data!.Count, (await service.GetReviewsAsync(10, null, 1, 10)).Data!.CommentCount));
            Assert.Equal(1, (await service.GetAdminReviewsAsync(new() { Status = Status.Rejected })).Data!.Total);

            Assert.True((await service.DeleteReviewAsync(1, 10)).Data);
            Assert.Equal(0, (await service.GetReviewsAsync(10, null, 1, 10)).Data!.Count);
        }

        public void Dispose()
        {
            context.Dispose();
            serviceProvider.Dispose();
        }
    }
}
