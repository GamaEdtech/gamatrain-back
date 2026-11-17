namespace GamaEdtech.Test.Application
{
    using System.Diagnostics.CodeAnalysis;

    using GamaEdtech.Application.Interface;
    using GamaEdtech.Common.Data;
    using GamaEdtech.Common.Service.Factory;
    using GamaEdtech.Data.Dto.Migration;
    using GamaEdtech.Domain.Enumeration;

    /// <summary>The migration switch for a service under test: every call goes to the one given source of its kind, and
    /// every read route is <see cref="Route"/> (Legacy unless a test sets it), or the domain's own in <see cref="Routes"/>.</summary>
    internal sealed class PassThroughMigrationRouter(params object[] sources) : IMigrationRouter
    {
        public MigrationRoute Route { get; init; } = MigrationRoute.Legacy;

        public IReadOnlyDictionary<string, MigrationRoute> Routes { get; init; } = new Dictionary<string, MigrationRoute>(StringComparer.Ordinal);

        public MigrationRoute GetRoute([NotNull] MigrationDomain domain, string? feature = null) => Routes.GetValueOrDefault(domain.Name, Route);

        public MigrationRoute WriteRoute { get; init; } = MigrationRoute.Legacy;

        /// <summary>Write routes by "Domain" or "Domain.Feature" (the feature's wins), else <see cref="WriteRoute"/>.</summary>
        public IReadOnlyDictionary<string, MigrationRoute> WriteRoutes { get; init; } = new Dictionary<string, MigrationRoute>(StringComparer.Ordinal);

        public MigrationRoute GetWriteRoute([NotNull] MigrationDomain domain, string? feature = null) =>
            WriteRoutes.GetValueOrDefault($"{domain.Name}.{feature}", WriteRoutes.GetValueOrDefault(domain.Name, WriteRoute));

        public Task<ResultData<T>> RunAsync<TSource, T>([NotNull] MigrationDomain domain, string? feature, [NotNull] Func<TSource, Task<ResultData<T>>> call)
            where TSource : class, IProvider<MigrationRoute> => call(sources.OfType<TSource>().Single());

        public Task<ResultData<T>> RunWriteAsync<TSource, T>([NotNull] MigrationDomain domain, string? feature, [NotNull] Func<TSource, Task<ResultData<T>>> call)
            where TSource : class, IProvider<MigrationRoute> => call(sources.OfType<TSource>().Single());

        public IReadOnlyCollection<MigrationShadowStatisticsDto> GetShadowStatistics() => [];
    }
}
