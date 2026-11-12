namespace GamaEdtech.Test.Application
{
    using System.Diagnostics.CodeAnalysis;

    using GamaEdtech.Application.Interface;
    using GamaEdtech.Common.Data;
    using GamaEdtech.Common.Service.Factory;
    using GamaEdtech.Data.Dto.Migration;
    using GamaEdtech.Domain.Enumeration;

    /// <summary>The migration switch for a service under test: every call goes to the one given source of its kind.</summary>
    internal sealed class PassThroughMigrationRouter(params object[] sources) : IMigrationRouter
    {
        public MigrationRoute GetRoute([NotNull] MigrationDomain domain, string? feature = null) => MigrationRoute.Legacy;

        public MigrationRoute GetWriteRoute([NotNull] MigrationDomain domain, string? feature = null) => MigrationRoute.Legacy;

        public Task<ResultData<T>> RunAsync<TSource, T>([NotNull] MigrationDomain domain, string? feature, [NotNull] Func<TSource, Task<ResultData<T>>> call)
            where TSource : class, IProvider<MigrationRoute> => call(sources.OfType<TSource>().Single());

        public Task<ResultData<T>> RunWriteAsync<TSource, T>([NotNull] MigrationDomain domain, string? feature, [NotNull] Func<TSource, Task<ResultData<T>>> call)
            where TSource : class, IProvider<MigrationRoute> => call(sources.OfType<TSource>().Single());

        public IReadOnlyCollection<MigrationShadowStatisticsDto> GetShadowStatistics() => [];
    }
}
