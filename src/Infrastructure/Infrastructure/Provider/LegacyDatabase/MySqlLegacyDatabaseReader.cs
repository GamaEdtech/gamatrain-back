namespace GamaEdtech.Infrastructure.Provider.LegacyDatabase
{
    using System.Diagnostics.CodeAnalysis;
    using System.Text.RegularExpressions;

    using GamaEdtech.Common.Core;
    using GamaEdtech.Common.Data;
    using GamaEdtech.Infrastructure.Interface;

    using Microsoft.Extensions.Configuration;
    using Microsoft.Extensions.Logging;

    using MySqlConnector;

    using static GamaEdtech.Common.Core.Constants;

    public sealed partial class MySqlLegacyDatabaseReader(Lazy<IConfiguration> configuration, Lazy<ILogger<MySqlLegacyDatabaseReader>> logger)
        : ILegacyDatabaseReader
    {
        private const string ConnectionStringKey = "Migration:LegacyDatabase:ConnectionString";

        public bool IsConfigured => !string.IsNullOrWhiteSpace(configuration.Value[ConnectionStringKey]);

        public Task<ResultData<IReadOnlyList<IReadOnlyDictionary<string, object?>>>> ReadBatchAsync([NotNull] string table, long afterId, int batchSize) =>
            ReadAsync(table, null, afterId, batchSize);

        public Task<ResultData<IReadOnlyList<IReadOnlyDictionary<string, object?>>>> ReadBatchAsync([NotNull] string table, [NotNull] IReadOnlyList<string> columns, long afterId, int batchSize) =>
            ReadAsync(table, columns, afterId, batchSize);

        private async Task<ResultData<IReadOnlyList<IReadOnlyDictionary<string, object?>>>> ReadAsync(string table, IReadOnlyList<string>? columns, long afterId, int batchSize)
        {
            try
            {
                if (!IsConfigured)
                {
                    return new(OperationResult.NotValid) { Errors = [new() { Message = $"{ConnectionStringKey} is not set." }] };
                }

                // The names come from fixed lists, but they are still the one part of the query that is not a parameter.
                if (!TableNameRegex().IsMatch(table))
                {
                    return new(OperationResult.NotValid) { Errors = [new() { Message = "Not a table name." }] };
                }

                if (columns is not null && (columns.Count == 0 || !columns.Contains("id", StringComparer.Ordinal) || columns.Any(t => !TableNameRegex().IsMatch(t))))
                {
                    return new(OperationResult.NotValid) { Errors = [new() { Message = "Not a column list (it must name columns, including id)." }] };
                }

                var selected = columns is null ? "*" : string.Join(", ", columns.Select(t => $"`{t}`"));

                MySqlConnectionStringBuilder builder = new(configuration.Value[ConnectionStringKey]!)
                {
                    // gama-api has rows with a 0000-00-00 date; read them as empty instead of failing the batch.
                    ConvertZeroDateTime = true,
                    AllowUserVariables = false,
                };
                await using MySqlConnection connection = new(builder.ConnectionString);
                await connection.OpenAsync();
                await using var command = connection.CreateCommand();
#pragma warning disable CA2100 // The table name is checked above; the values are parameters.
                command.CommandText = $"SELECT {selected} FROM `{table}` WHERE `id` > @afterId ORDER BY `id` LIMIT @batchSize";
#pragma warning restore CA2100
                _ = command.Parameters.AddWithValue("@afterId", afterId);
                _ = command.Parameters.AddWithValue("@batchSize", batchSize);

                List<IReadOnlyDictionary<string, object?>> rows = [];
                await using var reader = await command.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    Dictionary<string, object?> row = new(reader.FieldCount, StringComparer.Ordinal);
                    for (var i = 0; i < reader.FieldCount; i++)
                    {
                        row[reader.GetName(i)] = await reader.IsDBNullAsync(i) ? null : reader.GetValue(i);
                    }

                    rows.Add(row);
                }

                return new(OperationResult.Succeeded) { Data = rows };
            }
            catch (Exception exc)
            {
                logger.Value.LogException(exc);
                return new(OperationResult.Failed) { Errors = [new() { Message = exc.Message }] };
            }
        }

        [GeneratedRegex("^[a-z][a-z0-9_]{0,63}$")]
        private static partial Regex TableNameRegex();
    }
}
