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
    using Microsoft.EntityFrameworkCore;
    using Microsoft.Extensions.Configuration;
    using Microsoft.Extensions.DependencyInjection;
    using Microsoft.Extensions.Logging.Abstractions;

    using Xunit;

    using static GamaEdtech.Common.Core.Constants;

    using TestApplicationDBContext = GamaEdtech.Test.Application.ExamAnalysisServiceTests.TestApplicationDBContext;

    /// <summary>
    /// A past paper's files served from the copy, by gama-api's download rules: the price the page shows, the owner and
    /// admins free, others only a published paper's stored files, and a signed link saved under gama-api's file name. The
    /// ids and names here are made up.
    /// </summary>
    public sealed class PastPaperDownloadServiceTests : IDisposable
    {
        private const long Owner = 56;
        private const long Buyer = 57;
        private const long Admin = 58;
        private const int ConfidentialInstructions = 10517;

        private readonly ServiceProvider serviceProvider;
        private readonly TestApplicationDBContext context;
        private readonly PastPaperDownloadService service;
        private readonly System.Collections.ObjectModel.Collection<FileUriRequestDto> links = [];

        public PastPaperDownloadServiceTests()
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
                new(() => NullLogger<PastPaperDownloadService>.Instance),
                new(() => SignedLinksFake.Create(links)));
            Seed();
        }

        [Theory]
        [InlineData(1L, "Question", 190, "45830_qp_Gamatrain.com.pdf")]
        [InlineData(2L, "QuestionWord", 370, "45830_qm_Gamatrain.com.docx")]
        [InlineData(4L, "Extra:5953", 0, "45830_attached_Gamatrain.com.pdf")]
        public async Task AFileCostsWhatThePaperPageShows(long fileId, string variant, int points, string name)
        {
            var result = await service.GetFileAsync(Buyer, 45830, fileId);

            Assert.Equal(OperationResult.Succeeded, result.OperationResult);
            Assert.Equal((45830L, variant, points, false, name, Owner),
                (result.Data!.PaperId, result.Data.Variant, result.Data.Points, result.Data.Privileged, result.Data.DownloadName, result.Data.OwnerUserId!.Value));
        }

        [Theory]
        [InlineData(45900L, 5L, "p.pdf", "45900_qp_Gamatrain.com.pdf")]
        [InlineData(45901L, 6L, "d.pdf", "45901_qp_Gamatrain.com.pdf")]
        [InlineData(45830L, 3L, "2026/09/38/a.pdf", "45830_ms_Gamatrain.com.pdf")]
        [InlineData(45830L, 4L, "2026/09/38/x.pdf", "45830_attached_Gamatrain.com.pdf")]
        public async Task TheAdminReviewOpensAFileInAnyStatusWithoutADownload(long paperId, long fileId, string path, string name)
        {
            // A paper waiting for review, a deleted one, a file the size check hasn't reached: the review still opens them.
            var result = await AdminService().GetFileLinkAsync(paperId, fileId);

            Assert.Equal($"https://private.test/{path}?signed", (string?)result.Data!["url"]);
            var request = links.Single();
            Assert.Equal((path, ContainerType.PastPaper, name), (request.FileId, request.ContainerType, request.DownloadName));
            context.ChangeTracker.Clear();
            Assert.Equal(0, (await context.Set<PastPaper>().SingleAsync(t => t.Id == paperId)).DownloadCount);
        }

        [Theory]
        [InlineData(45830L, 99L)]
        [InlineData(45900L, 3L)]
        [InlineData(99999L, 1L)]
        public async Task TheAdminReviewRefusesAFileThatIsNotThere(long paperId, long fileId)
        {
            // Another paper's file is not this paper's.
            Assert.Equal("fileNotExist", (await AdminService().GetFileLinkAsync(paperId, fileId)).Errors!.Single().Message);
            Assert.Empty(links);
        }

        [Theory]
        [InlineData(3L)]
        [InlineData(5L)]
        [InlineData(99L)]
        public async Task OnlyAStoredFileOfThePaperIsServed(long fileId) =>
            Assert.Equal("fileNotExist", (await service.GetFileAsync(Buyer, 45830, fileId)).Errors!.Single().Message);

        [Fact]
        public async Task AnUnpublishedPaperIsOnlyItsOwnersAndAdmins()
        {
            Assert.Equal("accessDenied", (await service.GetFileAsync(Buyer, 45900, 5)).Errors!.Single().Message);
            Assert.True((await service.GetFileAsync(Owner, 45900, 5)).Data!.Privileged);
            Assert.True((await service.GetFileAsync(Admin, 45900, 5)).Data!.Privileged);
            Assert.Equal("noResult", (await service.GetFileAsync(Admin, 45901, 6)).Errors!.Single().Message);
        }

        [Fact]
        public async Task TheLinkIsASignedPrivateLinkUnderItsDownloadName()
        {
            var file = (await service.GetFileAsync(Buyer, 45830, 1)).Data!;

            var link = await service.GetLinkAsync(file);

            Assert.Equal(OperationResult.Succeeded, link.OperationResult);
            var request = Assert.Single(links);
            Assert.Equal(("2026/09/38/q.pdf", ContainerType.PastPaper, "45830_qp_Gamatrain.com.pdf"), (request.FileId, request.ContainerType, request.DownloadName));
        }

        [Fact]
        public async Task OwnedFilesAreTheBoughtOnesOrAllForTheOwnerAndAdmins()
        {
            _ = context.Set<ContentPurchase>().Add(new()
            {
                UserId = Buyer,
                ContentType = PurchasableContentType.PastPaper,
                ContentId = 45830,
                Variant = PastPaperFileKind.QuestionWord.Name,
                Points = 0,
                PaidBy = SpendSource.GamaApi,
                CreationDate = DateTimeOffset.UtcNow,
            });
            _ = await context.SaveChangesAsync();

            var buyer = (await service.GetOwnershipAsync(Buyer, 45830)).Data!;
            var owner = (await service.GetOwnershipAsync(Owner, 45830)).Data!;
            var admin = (await service.GetOwnershipAsync(Admin, 45830)).Data!;

            // By file id; the answer (not stored yet) is no file to own.
            Assert.Equal([2L], buyer.FileIds);
            Assert.Equal([1L, 2L, 4L], owner.FileIds.Order());
            Assert.Equal([1L, 2L, 4L], admin.FileIds.Order());
        }

        /// <summary>The admin panel's paper service; the file links read only the copy and the file service.</summary>
        private PastPaperAdminService AdminService() => new(
            new(serviceProvider.GetRequiredService<IUnitOfWorkProvider>),
            new(serviceProvider.GetRequiredService<IHttpContextAccessor>),
            new(() => NullLogger<PastPaperAdminService>.Instance),
            new(() => new PassThroughMigrationRouter()),
            new(() => throw new NotSupportedException("The file links use no paper source.")),
            new(() => SignedLinksFake.Create(links)));

        public void Dispose()
        {
            context.Dispose();
            serviceProvider.Dispose();
        }

        private void Seed()
        {
            context.Set<ApplicationUser>().AddRange(
                User(Owner, "owner"),
                User(Buyer, "buyer"),
                User(Admin, "admin"));
            ApplicationRole adminRole = new() { Id = 1, Name = nameof(Role.Admin), NormalizedName = "ADMIN", ConcurrencyStamp = "1" };
            _ = context.Set<ApplicationRole>().Add(adminRole);
            _ = context.Set<ApplicationUserRole>().Add(new() { UserId = Admin, RoleId = adminRole.Id });
            context.Set<PastPaperFilePrice>().AddRange(
                new PastPaperFilePrice { Id = 1, CoreType = 1, Extensions = "pdf", BasePrice = 50, SyncDate = DateTimeOffset.UtcNow },
                new PastPaperFilePrice { Id = 2, CoreType = 1, Extensions = "doc,docx", BasePrice = 100, SyncDate = DateTimeOffset.UtcNow });
            _ = context.Set<LegacyTaxonomyItem>().Add(new()
            {
                Id = ConfidentialInstructions,
                Kind = LegacyTaxonomyKinds.ExtraFileType,
                Title = "Confidental Instructions",
                Parent = string.Empty,
                Metadata = """{"price_multiplier":"0"}""",
                SyncDate = DateTimeOffset.UtcNow,
                Parents = [],
            });

            // Paper 45830's prices as gama-api shows them: a 14-page PDF 190, its Word file 370, a free extra file.
            var paper = Paper(45830, PastPaperStatus.Confirmed);
            paper.Files = [
                new() { Id = 1, Kind = PastPaperFileKind.Question, Path = "2026/09/38/q.pdf", PageCount = 14, Size = 659_456 },
                new() { Id = 2, Kind = PastPaperFileKind.QuestionWord, Path = "2026/09/38/q.docx", Size = 2_097_152 },
                new() { Id = 3, Kind = PastPaperFileKind.Answer, Path = "2026/09/38/a.pdf", PageCount = 3, Size = null },
                new() { Id = 4, Kind = PastPaperFileKind.Extra, Path = "2026/09/38/x.pdf", Length = 8, CoreTypeId = ConfidentialInstructions, CoreExtraFileId = 5953, Size = 217_088 },
            ];
            var pending = Paper(45900, PastPaperStatus.Pending);
            pending.Files = [new() { Id = 5, Kind = PastPaperFileKind.Question, Path = "p.pdf", PageCount = 2, Size = 1000 }];
            var deleted = Paper(45901, PastPaperStatus.Confirmed);
            deleted.IsDeleted = true;
            deleted.Files = [new() { Id = 6, Kind = PastPaperFileKind.Question, Path = "d.pdf", PageCount = 2, Size = 1000 }];
            context.Set<PastPaper>().AddRange(paper, pending, deleted);
            _ = context.SaveChanges();
            context.ChangeTracker.Clear();
        }

        private static ApplicationUser User(long id, string name) => new()
        {
            Id = id,
            UserName = $"{name}@example.test",
            NormalizedUserName = $"{name.ToUpperInvariant()}@EXAMPLE.TEST",
            ProfileVisibility = ProfileVisibility.Private,
            Enabled = true,
        };

        private static PastPaper Paper(long id, PastPaperStatus status) => new()
        {
            Id = id,
            CoreOwnerId = 900001,
            OwnerUserId = Owner,
            DisplayOwnerUserId = Owner,
            Title = $"Paper {id}",
            Status = status,
            CoreBoardId = 6627,
            CoreGradeId = 4161,
            CoreSubjectId = 4164,
            IsPaper = true,
            RefereeScore = 4,
            PriceMultiplier = 5,
            CreationDate = DateTimeOffset.UtcNow,
            SyncDate = DateTimeOffset.UtcNow,
        };

        /// <summary>The file service: a private file's signed link, recording what was asked.</summary>
        public class SignedLinksFake : DispatchProxy
        {
            private System.Collections.ObjectModel.Collection<FileUriRequestDto> requests = [];

            public static IFileService Create(System.Collections.ObjectModel.Collection<FileUriRequestDto> requests)
            {
                var proxy = Create<IFileService, SignedLinksFake>();
                ((SignedLinksFake)(object)proxy).requests = requests;
                return proxy;
            }

            protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
            {
                ArgumentNullException.ThrowIfNull(args);
                var request = (FileUriRequestDto)args[0]!;
                requests.Add(request);
                return Task.FromResult<Uri?>(new($"https://private.test/{request.FileId}?signed"));
            }
        }
    }
}
