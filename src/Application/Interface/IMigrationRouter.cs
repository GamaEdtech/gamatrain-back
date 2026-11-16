namespace GamaEdtech.Application.Interface
{
    using System.Diagnostics.CodeAnalysis;

    using GamaEdtech.Common.Data;
    using GamaEdtech.Common.DataAnnotation;
    using GamaEdtech.Common.Service.Factory;
    using GamaEdtech.Data.Dto.Migration;
    using GamaEdtech.Domain.Enumeration;

    /// <summary>
    /// The switch between gama-api and this backend's own code for a migrating domain. Every call into a migrating
    /// domain goes through <see cref="RunAsync"/> (reads) or <see cref="RunWriteAsync"/> (writes); which side answers is a
    /// setting (<c>Migration:Routes</c>, <c>Migration:WriteRoutes</c>), so a domain
    /// is switched, and switched back, without a deploy. See <c>docs/architecture/legacy-migration.md</c>.
    /// </summary>
    [Injectable]
    public interface IMigrationRouter
    {
        /// <summary>
        /// The route of a domain, or of one feature of it when that feature has its own setting. Missing or unknown
        /// settings mean <see cref="MigrationRoute.Legacy"/>.
        /// </summary>
        MigrationRoute GetRoute([NotNull] MigrationDomain domain, string? feature = null);

        /// <summary>
        /// Runs <paramref name="call"/> on the source the route picks. <see cref="MigrationRoute.Native"/> without a native
        /// source falls back to gama-api. With <see cref="MigrationRoute.Shadow"/> gama-api's answer is returned and the
        /// native source is called afterwards, off the request, only to compare; <paramref name="call"/> must therefore
        /// not depend on the current HTTP request and must have no side effects on a native source.
        /// </summary>
        /// <param name="domain">The migrating domain the call belongs to.</param>
        /// <param name="feature">Who is calling (see <c>MigrationFeatures</c>), so one feature can be switched on its own.</param>
        /// <param name="call">The call to make on the chosen source.</param>
        Task<ResultData<T>> RunAsync<TSource, T>([NotNull] MigrationDomain domain, string? feature, [NotNull] Func<TSource, Task<ResultData<T>>> call)
            where TSource : class, IProvider<MigrationRoute>;

        /// <summary>
        /// The route of a domain's writes (<c>Migration:WriteRoutes</c>), set apart from its reads so reads move first:
        /// <see cref="MigrationRoute.Native"/> or <see cref="MigrationRoute.Legacy"/>, never <see cref="MigrationRoute.Shadow"/>.
        /// </summary>
        MigrationRoute GetWriteRoute([NotNull] MigrationDomain domain, string? feature = null);

        /// <summary>
        /// <see cref="RunAsync"/> for a call that changes something, or that must not run twice (a charge, a one-time
        /// link): it follows <see cref="GetWriteRoute"/> and is never repeated for a comparison.
        /// </summary>
        /// <param name="domain">The migrating domain the call belongs to.</param>
        /// <param name="feature">Who is calling (see <c>MigrationFeatures</c>).</param>
        /// <param name="call">The call to make on the chosen source.</param>
        Task<ResultData<T>> RunWriteAsync<TSource, T>([NotNull] MigrationDomain domain, string? feature, [NotNull] Func<TSource, Task<ResultData<T>>> call)
            where TSource : class, IProvider<MigrationRoute>;

        /// <summary>The shadow comparison's counters of this process, one entry per domain and feature compared so far.</summary>
        IReadOnlyCollection<MigrationShadowStatisticsDto> GetShadowStatistics();
    }
}
