namespace GamaEdtech.Test.Application
{
    using GamaEdtech.Application.Interface;
    using GamaEdtech.Application.Service;
    using GamaEdtech.Common.Data.Enumeration;
    using GamaEdtech.Common.Service.Factory;
    using GamaEdtech.Domain.Enumeration;
    using GamaEdtech.Infrastructure.Interface;
    using GamaEdtech.Infrastructure.Provider.Core;

    using Microsoft.Extensions.DependencyInjection;

    using Xunit;

    /// <summary>
    /// The migration switch in the application's real container: it resolves, the gama-api proxy is the legacy source of
    /// the types domain, the services that go through it still resolve, and the tracked settings leave everything on gama-api.
    /// </summary>
    public class MigrationWiringTests : TestBase
    {
        [Fact]
        public void ContainerResolvesTheSwitchWithGamaApiAsTheLegacySource()
        {
            using var scope = Services.Value!.CreateScope();
            var services = scope.ServiceProvider;

            _ = Assert.IsType<MigrationRouter>(services.GetRequiredService<IMigrationRouter>());
            var factory = services.GetRequiredService<IGenericFactory<ITaxonomySource, MigrationRoute>>();
            _ = Assert.IsType<CoreProvider>(factory.GetProvider(MigrationRoute.Legacy));
            _ = Assert.IsType<NativeTaxonomySource>(factory.GetProvider(MigrationRoute.Native));
            Assert.NotNull(services.GetRequiredService<ILegacyTaxonomySyncService>());
            _ = Assert.IsType<CoreProvider>(services.GetRequiredService<IGenericFactory<IPastPaperSource, MigrationRoute>>().GetProvider(MigrationRoute.Legacy));
            _ = Assert.IsType<CoreProvider>(services.GetRequiredService<IGenericFactory<IExamSource, MigrationRoute>>().GetProvider(MigrationRoute.Legacy));

            Assert.NotNull(services.GetRequiredService<IBoardService>());
            Assert.NotNull(services.GetRequiredService<IExamImportService>());
            Assert.NotNull(services.GetRequiredService<IExamService>());
            Assert.NotNull(services.GetRequiredService<IExamAnalysisService>());
            Assert.NotNull(services.GetRequiredService<IGameService>());
            Assert.NotNull(services.GetRequiredService<ISearchService>());
        }

        [Fact]
        public void TrackedSettingsKeepEveryDomainOnGamaApi()
        {
            using var scope = Services.Value!.CreateScope();
            var router = scope.ServiceProvider.GetRequiredService<IMigrationRouter>();

            var domains = EnumerationExtensions.GetAll<MigrationDomain, byte>()!.ToList();
            Assert.Equal(6, domains.Count);
            Assert.All(domains, domain =>
            {
                Assert.Equal(MigrationRoute.Legacy, router.GetRoute(domain!));
                Assert.Equal(MigrationRoute.Legacy, router.GetRoute(domain!, MigrationFeatures.Mcp));
                Assert.Equal(MigrationRoute.Legacy, router.GetRoute(domain!, MigrationFeatures.BoardSync));
                Assert.Equal(MigrationRoute.Legacy, router.GetWriteRoute(domain!));
                Assert.Equal(MigrationRoute.Legacy, router.GetWriteRoute(domain!, MigrationFeatures.Mcp));
            });
        }
    }
}
