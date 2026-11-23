namespace GamaEdtech.Test.Application
{
    using GamaEdtech.Application.Interface;
    using GamaEdtech.Application.Service;
    using GamaEdtech.Common.Data;
    using GamaEdtech.Common.DataAccess.Context;
    using GamaEdtech.Common.DataAccess.Repositories;
    using GamaEdtech.Common.DataAccess.UnitOfWork;
    using GamaEdtech.Data.Dto.Transaction;
    using GamaEdtech.Domain.Entity;
    using GamaEdtech.Domain.Entity.Identity;
    using GamaEdtech.Domain.Enumeration;

    using Microsoft.AspNetCore.Http;
    using Microsoft.Extensions.Configuration;
    using Microsoft.Extensions.DependencyInjection;
    using Microsoft.Extensions.Logging.Abstractions;

    using Xunit;

    using static GamaEdtech.Common.Core.Constants;

    using CdnFake = GamaEdtech.Test.Application.NativePastPaperSourceTests.CdnFake;
    using Fake = GamaEdtech.Test.ExamImport.McpSignOutTests.Fake;
    using TestApplicationDBContext = GamaEdtech.Test.Application.ExamAnalysisServiceTests.TestApplicationDBContext;

    // Exam reviews: only a participant reviews, every review is public at once, an admin hides a bad one.
    public sealed class ExamReviewServiceTests : IDisposable
    {
        private readonly ServiceProvider serviceProvider;
        private readonly TestApplicationDBContext context;
        private readonly ExamReviewService service;
        private readonly List<(long UserId, long Points)> pointMoves = [];
        private long? reviewPoints;

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

            ResultData<long> Moved(object?[] args, int sign)
            {
                var request = (CreateTransactionRequestDto)args[0]!;
                pointMoves.Add((request.UserId, sign * request.Points));
                return new(OperationResult.Succeeded) { Data = 1 };
            }

            var transactions = Fake.Create<ITransactionService>(new()
            {
                ["IncreaseBalanceAsync"] = args => Task.FromResult(Moved(args, 1)),
                ["DecreaseBalanceAsync"] = args => Task.FromResult(Moved(args, -1)),
            });
            var settings = Fake.Create<IApplicationSettingsService>(new()
            {
                ["GetSettingAsync"] = _ => Task.FromResult(new ResultData<long?>(OperationResult.Succeeded) { Data = reviewPoints }),
            });
            service = new(new(serviceProvider.GetRequiredService<IUnitOfWorkProvider>), new(serviceProvider.GetRequiredService<IHttpContextAccessor>),
                new(() => NullLogger<ExamReviewService>.Instance), new(CdnFake.Create), new(() => transactions), new(() => settings));
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

            // A review is changed for 30 minutes after posting, then it stands (there is no delete).
            Assert.True(rating.EditableUntil > DateTimeOffset.UtcNow);
            context.ChangeTracker.Clear();
            var stored = context.Set<ExamReview>().Single(t => t.UserId == 1);
            stored.CreationDate = DateTimeOffset.UtcNow.AddMinutes(-31);
            _ = await context.SaveChangesAsync();
            Assert.Equal("editWindowClosed", (await service.SaveReviewAsync(new() { UserId = 1, ExamId = 10, Rating = 1 })).Errors!.Single().Reference);
        }

        [Fact]
        public async Task AReviewWithACommentEarnsThePointsOnceAndLosesThemWhenHidden()
        {
            // A rating alone earns nothing; adding a comment pays the default 2, once, however often it is edited.
            var review = (await service.SaveReviewAsync(new() { UserId = 1, ExamId = 10, Rating = 4 })).Data!;
            Assert.Empty(pointMoves);
            _ = await service.SaveReviewAsync(new() { UserId = 1, ExamId = 10, Rating = 4, Comment = "Clear questions" });
            _ = await service.SaveReviewAsync(new() { UserId = 1, ExamId = 10, Rating = 5, Comment = "Clear questions, fair timing" });
            Assert.Equal([(1L, 2L)], pointMoves);

            // Hidden by an admin: the points paid go back; shown again: paid again, at the setting of the day.
            _ = await service.ModerateAsync(review.Id, confirm: false, "Spam");
            reviewPoints = 5;
            _ = await service.ModerateAsync(review.Id, confirm: true, null);
            Assert.Equal([(1L, 2L), (1L, -2L), (1L, 5L)], pointMoves);

            // Removing the comment (within the edit window) takes back what was paid.
            _ = await service.SaveReviewAsync(new() { UserId = 1, ExamId = 10, Rating = 5 });
            Assert.Equal((1L, -5L), pointMoves[^1]);
        }

        public void Dispose()
        {
            context.Dispose();
            serviceProvider.Dispose();
        }
    }
}
