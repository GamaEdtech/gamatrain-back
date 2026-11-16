namespace GamaEdtech.Infrastructure.Interface
{
    using System.Diagnostics.CodeAnalysis;

    using GamaEdtech.Common.Data;
    using GamaEdtech.Common.DataAnnotation;

    /// <summary>
    /// Read-only access to gama-api's MySQL database, for copying its tables into this backend. Configured by
    /// <c>Migration:LegacyDatabase:ConnectionString</c>, which is never in a tracked file; without it nothing is read.
    /// Use an account that can only read.
    /// </summary>
    [Injectable]
    public interface ILegacyDatabaseReader
    {
        bool IsConfigured { get; }

        /// <summary>Up to <paramref name="batchSize"/> rows of <paramref name="table"/> with an id above
        /// <paramref name="afterId"/>, in id order, each as column name to value.</summary>
        Task<ResultData<IReadOnlyList<IReadOnlyDictionary<string, object?>>>> ReadBatchAsync([NotNull] string table, long afterId, int batchSize);

        /// <summary>Like <see cref="ReadBatchAsync(string, long, int)"/>, but reads only <paramref name="columns"/>, so a
        /// table's sensitive columns (card numbers, IPs) never leave gama-api.</summary>
        Task<ResultData<IReadOnlyList<IReadOnlyDictionary<string, object?>>>> ReadBatchAsync([NotNull] string table, [NotNull] IReadOnlyList<string> columns, long afterId, int batchSize);

        /// <summary>The rows of <paramref name="table"/> with one of <paramref name="ids"/> (at most 1000), reading only
        /// <paramref name="columns"/>.</summary>
        Task<ResultData<IReadOnlyList<IReadOnlyDictionary<string, object?>>>> ReadByIdsAsync([NotNull] string table, [NotNull] IReadOnlyList<string> columns, [NotNull] IReadOnlyCollection<long> ids);
    }
}
