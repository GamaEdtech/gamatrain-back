namespace GamaEdtech.Test.Application
{
    using GamaEdtech.Application.Interface;
    using GamaEdtech.Application.Service;
    using GamaEdtech.Common.Data;
    using GamaEdtech.Common.DataAccess.Context;
    using GamaEdtech.Common.DataAccess.Repositories;
    using GamaEdtech.Common.DataAccess.UnitOfWork;
    using GamaEdtech.Common.Service.Factory;
    using GamaEdtech.Data.Dto.ApplicationSettings;
    using GamaEdtech.Data.Dto.Content;
    using GamaEdtech.Data.Dto.ContentPurchase;
    using GamaEdtech.Data.Dto.PastPaper;
    using GamaEdtech.Domain.Entity;
    using GamaEdtech.Domain.Enumeration;
    using GamaEdtech.Infrastructure.Interface;

    using Microsoft.AspNetCore.Http;
    using Microsoft.Extensions.Configuration;
    using Microsoft.Extensions.DependencyInjection;
    using Microsoft.Extensions.Logging.Abstractions;

    using Xunit;

    using static GamaEdtech.Common.Core.Constants;

    using Fake = GamaEdtech.Test.ExamImport.McpSignOutTests.Fake;
    using TestApplicationDBContext = GamaEdtech.Test.Application.ExamAnalysisServiceTests.TestApplicationDBContext;

    /// <summary>
    /// A past paper download once <c>Migration:WriteRoutes:PastPapers.Downloads</c> is Native: charged once through the
    /// pay-once purchases, the link made only after the charge, a failed link refunded, the owner's commission, and
    /// gama-api never called.
    /// </summary>
    public sealed class NativePaperDownloadTests : IDisposable
    {
        private const long Downloader = 57;
        private const long Owner = 56;

        private readonly ServiceProvider serviceProvider;
        private readonly TestApplicationDBContext context;
        private readonly List<string> calls = [];

        public NativePaperDownloadTests()
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
        }

        [Fact]
        public async Task APaidFileIsChargedThenLinkedThenRecordedAndItsOwnerEarns()
        {
            var service = Service(File(points: 190));

            var result = await service.DownloadContentAsync(Request("pdf"));

            Assert.Equal(OperationResult.Succeeded, result.OperationResult);
            Assert.Equal(("https://private.test/q.pdf", "45830_qp_Gamatrain.com.pdf", true), (result.Data!.Url, result.Data.Name, result.Data.Spent));
            Assert.Equal(["charge:Question:190", "link", "complete"], calls);
            var commission = Assert.Single(context.Set<ContentOwnerCommission>());
            Assert.Equal((Owner, Downloader, ContentSource.Native, 190L), (commission.OwnerUserId, commission.DownloaderUserId, commission.Source, commission.Points));
        }

        [Fact]
        public async Task AFailedLinkRefundsTheCharge()
        {
            var service = Service(File(points: 190), linkWorks: false);

            var result = await service.DownloadContentAsync(Request("pdf"));

            Assert.Equal(OperationResult.Failed, result.OperationResult);
            Assert.Equal(["charge:Question:190", "link", "refund"], calls);
            Assert.Empty(context.Set<ContentOwnerCommission>());
        }

        [Fact]
        public async Task ARefusedChargeNeverMakesALink()
        {
            var service = Service(File(points: 190), chargeWorks: false);

            var result = await service.DownloadContentAsync(Request("pdf"));

            Assert.Equal(OperationResult.Failed, result.OperationResult);
            Assert.Equal(QuotaFailureReason.QuotaExhausted, result.Data!.Reason);
            Assert.Equal(["charge:Question:190"], calls);
        }

        [Theory]
        [InlineData(0, false)]
        [InlineData(190, true)]
        public async Task AFreeFileOrTheOwnersOwnIsLinkedWithoutACharge(int points, bool privileged)
        {
            var service = Service(File(points, privileged));

            var result = await service.DownloadContentAsync(Request("pdf"));

            Assert.False(result.Data!.Spent);
            Assert.Equal(["link"], calls);
        }

        public void Dispose()
        {
            context.Dispose();
            serviceProvider.Dispose();
        }

        private static DownloadContentRequestDto Request(string fileType) => new()
        {
            UserId = Downloader,
            Token = "token",
            Id = 45830,
            ContentType = DownloadContentType.PastPaper,
            FileType = fileType,
        };

        private static PastPaperDownloadDto File(int points, bool privileged = false) => new()
        {
            PaperId = 45830,
            Variant = PastPaperFileKind.Question.Name,
            Points = points,
            Privileged = privileged,
            Path = "q.pdf",
            DownloadName = "45830_qp_Gamatrain.com.pdf",
            OwnerUserId = Owner,
        };

        private ContentDeliveryService Service(PastPaperDownloadDto file, bool chargeWorks = true, bool linkWorks = true) => new(
            new(serviceProvider.GetRequiredService<IUnitOfWorkProvider>),
            new(serviceProvider.GetRequiredService<IHttpContextAccessor>),
            new(() => null!),
            new(() => NullLogger<ContentDeliveryService>.Instance),
            new(() => Fake.Create<IGenericFactory<IContentDeliveryProvider, ContentSource>>(new()
            {
                ["GetProvider"] = _ => throw new InvalidOperationException("gama-api must not be called"),
            })),
            new(() => Fake.Create<IGameService>([])),
            new(() => Fake.Create<IApplicationSettingsService>(new()
            {
                ["GetApplicationSettingsAsync"] = _ => Task.FromResult(new ResultData<ApplicationSettingsDto>(OperationResult.Succeeded)
                {
                    Data = new() { ContentOwnerCommissionPercent = 20 },
                }),
            })),
            new(() => new PassThroughMigrationRouter { WriteRoute = MigrationRoute.Native }),
            new(() => Fake.Create<IPastPaperDownloadService>(new()
            {
                ["GetFileAsync"] = _ => Task.FromResult(new ResultData<PastPaperDownloadDto>(OperationResult.Succeeded) { Data = file }),
                ["GetLinkAsync"] = _ =>
                {
                    calls.Add("link");
                    return Task.FromResult(linkWorks
                        ? new ResultData<Uri>(OperationResult.Succeeded) { Data = new("https://private.test/q.pdf") }
                        : new ResultData<Uri>(OperationResult.Failed) { Errors = [new() { Message = "fileNotExist" }] });
                },
            })),
            new(() => Fake.Create<IContentPurchaseService>(new()
            {
                ["ChargeAsync"] = args =>
                {
                    var purchase = (ContentPurchaseRequestDto)args[0]!;
                    calls.Add($"charge:{purchase.Variant}:{purchase.Points}");
                    return Task.FromResult(chargeWorks
                        ? new ResultData<ContentChargeResponseDto>(OperationResult.Succeeded)
                        {
                            Data = new() { Points = purchase.Points, Spend = new() { Spent = true, PaidBy = SpendSource.Points } },
                        }
                        : new ResultData<ContentChargeResponseDto>(OperationResult.Failed)
                        {
                            Data = new() { Points = purchase.Points, Refused = new() { Reason = QuotaFailureReason.QuotaExhausted } },
                        });
                },
                ["CompletePurchaseAsync"] = _ =>
                {
                    calls.Add("complete");
                    return Task.CompletedTask;
                },
                ["RefundAsync"] = _ =>
                {
                    calls.Add("refund");
                    return Task.CompletedTask;
                },
            })));
    }
}
