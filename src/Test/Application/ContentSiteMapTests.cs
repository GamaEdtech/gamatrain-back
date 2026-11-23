namespace GamaEdtech.Test.Application
{
    using GamaEdtech.Application.Service;
    using GamaEdtech.Common.DataAccess.Context;
    using GamaEdtech.Common.DataAccess.Repositories;
    using GamaEdtech.Common.DataAccess.UnitOfWork;
    using GamaEdtech.Domain.Entity;
    using GamaEdtech.Domain.Enumeration;

    using Microsoft.AspNetCore.Http;
    using Microsoft.Extensions.Configuration;
    using Microsoft.Extensions.DependencyInjection;

    using Xunit;

    using TestApplicationDBContext = GamaEdtech.Test.Application.ExamAnalysisServiceTests.TestApplicationDBContext;

    // The sitemap lists only public papers, exams and tutorials, each under the slug its page declares canonical.
    public sealed class ContentSiteMapTests : IDisposable
    {
        private readonly ServiceProvider serviceProvider;
        private readonly TestApplicationDBContext context;

        public ContentSiteMapTests()
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
            context.Set<Exam>().AddRange(
                new Exam { Id = 1, Title = "Nucleic Acids | DNA, RNA", Status = 1, CreationDate = now, SyncDate = now },
                new Exam { Id = 2, Title = "Teacher's exam", Status = 7, CreationDate = now, SyncDate = now },
                new Exam { Id = 3, Title = "Draft", Status = 6, CreationDate = now, SyncDate = now },
                new Exam { Id = 4, Title = "Hidden", Status = 1, IsDeleted = true, CreationDate = now, SyncDate = now });
            _ = context.SaveChanges();
        }

        [Fact]
        public void OnlyPublicExamsAreListedUnderTheirPageSlug()
        {
            ExamSiteMapHandler handler = new();
            var items = handler.GetSiteMapData(serviceProvider.GetRequiredService<IUnitOfWorkProvider>().CreateUnitOfWork()).OrderBy(t => t.Id).ToList();

            Assert.Equal([1L, 2L], items.Select(t => t.Id));
            Assert.Equal(ItemType.Exam, handler.ItemType);
            Assert.Equal("exam", handler.ItemType.Identifier);

            // gama-api's title_url, the one the exam page redirects to.
            Assert.Equal(PastPaperOutput.TitleUrl(items[0].Path2), Uri.UnescapeDataString(handler.Slug(items[0].Path2)!));
            Assert.Equal("Nucleic-Acids-DNA-RNA", handler.Slug(items[0].Path2));
            Assert.Null(handler.Slug("|||"));
        }

        public void Dispose()
        {
            context.Dispose();
            serviceProvider.Dispose();
        }
    }
}
