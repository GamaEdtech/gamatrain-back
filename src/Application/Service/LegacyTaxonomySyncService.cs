namespace GamaEdtech.Application.Service
{
    using System.Linq;

    using GamaEdtech.Application.Interface;
    using GamaEdtech.Common.Core;
    using GamaEdtech.Common.Data;
    using GamaEdtech.Common.DataAccess.UnitOfWork;
    using GamaEdtech.Common.Service;
    using GamaEdtech.Data.Dto.Taxonomy;
    using GamaEdtech.Domain.Entity;
    using GamaEdtech.Infrastructure.Interface;

    using Microsoft.AspNetCore.Http;
    using Microsoft.EntityFrameworkCore;
    using Microsoft.Extensions.Logging;

    using static GamaEdtech.Common.Core.Constants;

    /// <summary>
    /// Copies gama-api's lookup lists from its database table (<c>type_slave</c>), read only, through a column list: every
    /// item of the copied lists, live or deleted, with its metadata (price multipliers, <c>is_paper</c>, <c>season</c>), which
    /// gama-api's public <c>types/list</c> does not show. The whole table is read before anything is written, so a failed read
    /// changes nothing.
    /// </summary>
    public sealed class LegacyTaxonomySyncService(Lazy<IUnitOfWorkProvider> unitOfWorkProvider, Lazy<IHttpContextAccessor> httpContextAccessor,
        Lazy<ILogger<LegacyTaxonomySyncService>> logger, Lazy<ILegacyDatabaseReader> reader)
        : ServiceBase<LegacyTaxonomySyncService>(unitOfWorkProvider, httpContextAccessor, logger), ILegacyTaxonomySyncService
    {
        private const string Table = "type_slave";
        private const int BatchSize = 2000;
        private const int MaxTitle = 1000;
        private static readonly string[] Columns = ["id", "master_", "list_order", "title", "parent", "parent2", "metadata", "bit_delete"];

        private static readonly short[] CopiedKinds = [.. LegacyTaxonomyKinds.ByListName.Values];

        private const string RelationsTable = "types_relation";
        private static readonly string[] RelationColumns = ["id", "from_id", "to_id"];

        public async Task<ResultData<LegacyTaxonomySyncResultDto>> SyncAsync()
        {
            try
            {
                if (!reader.Value.IsConfigured)
                {
                    return new(OperationResult.NotValid) { Errors = [new() { Message = "gama-api's database is not configured (Migration:LegacyDatabase:ConnectionString)." }] };
                }

                Dictionary<int, TaxonomyItemDto> items = [];
                var calls = 0;
                long afterId = 0;
                while (true)
                {
                    var batch = await reader.Value.ReadBatchAsync(Table, Columns, afterId, BatchSize);
                    calls++;
                    if (batch.OperationResult is not OperationResult.Succeeded || batch.Data is null)
                    {
                        Logger.Value.LogWarning("Taxonomy sync stopped: a read of gama-api's {Table} failed; nothing was changed", Table);
                        return new(batch.OperationResult) { Errors = batch.Errors };
                    }

                    if (batch.Data.Count == 0)
                    {
                        break;
                    }

                    afterId = batch.Data.Max(t => Long(t, "id") ?? 0);
                    foreach (var item in batch.Data.Select(ToItem).OfType<TaxonomyItemDto>().Where(t => CopiedKinds.Contains(t.Kind)))
                    {
                        items[item.Id] = item;
                    }
                }

                // The links between boards' items, read whole too before anything is written.
                Dictionary<int, (int From, int To)> relations = [];
                afterId = 0;
                while (true)
                {
                    var batch = await reader.Value.ReadBatchAsync(RelationsTable, RelationColumns, afterId, BatchSize);
                    calls++;
                    if (batch.OperationResult is not OperationResult.Succeeded || batch.Data is null)
                    {
                        Logger.Value.LogWarning("Taxonomy sync stopped: a read of gama-api's {Table} failed; nothing was changed", RelationsTable);
                        return new(batch.OperationResult) { Errors = batch.Errors };
                    }

                    if (batch.Data.Count == 0)
                    {
                        break;
                    }

                    afterId = batch.Data.Max(t => Long(t, "id") ?? 0);
                    foreach (var row in batch.Data)
                    {
                        if (Long(row, "id") is long id && Long(row, "from_id") is long from && Long(row, "to_id") is long to)
                        {
                            relations[(int)id] = ((int)from, (int)to);
                        }
                    }
                }

                var result = await SaveAsync(items);
                (result.Calls, result.Read) = (calls, items.Count);
                result.Relations = await SaveRelationsAsync(relations);
                Logger.Value.LogWarning("Taxonomy sync: {Read} items read, {Added} added, {Updated} updated, {Deleted} deleted, {Unchanged} unchanged; {Relations} links",
                    result.Read, result.Added, result.Updated, result.Deleted, result.Unchanged, result.Relations);
                return new(OperationResult.Succeeded) { Data = result };
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return new(OperationResult.Failed) { Errors = [new() { Message = exc.Message }] };
            }
        }

        /// <summary>A parent field split into its keys, the way gama-api's <c>LIKE</c> filter reads it.</summary>
        internal static List<string> ParentKeys(string? parent) =>
            [.. (parent ?? string.Empty).Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(t => t.Length is > 0 and <= 50)
                .Distinct(StringComparer.Ordinal)];

        /// <summary>A lookup item from a <c>type_slave</c> row; null without an id or a list. Internal for the tests.</summary>
        internal static TaxonomyItemDto? ToItem(IReadOnlyDictionary<string, object?> row)
        {
            if (Long(row, "id") is not long id || Long(row, "master_") is not long kind)
            {
                return null;
            }

            var metadata = Text(row, "metadata") is { } m && !string.IsNullOrWhiteSpace(m) && m.Trim() != "null" ? m.Trim() : null;
            return new()
            {
                Id = (int)id,
                Kind = (short)kind,
                Title = Text(row, "title"),
                ListOrder = (int)(Long(row, "list_order") ?? 0),
                Parent = Text(row, "parent") ?? string.Empty,
                Parent2 = Text(row, "parent2") ?? string.Empty,
                Metadata = metadata,
                IsSeason = MetadataFlag(metadata, "season"),
                IsPaper = MetadataFlag(metadata, "is_paper"),
                IsDeleted = Long(row, "bit_delete") is > 0,
            };
        }

        /// <summary>A true/false flag in an item's metadata JSON (<c>1</c>, <c>"1"</c>, <c>true</c>); null when it is not there.</summary>
        internal static bool? MetadataFlag(string? metadata, string name)
        {
            if (metadata is null)
            {
                return null;
            }

            try
            {
                using var json = System.Text.Json.JsonDocument.Parse(metadata);
                return json.RootElement.ValueKind != System.Text.Json.JsonValueKind.Object || !json.RootElement.TryGetProperty(name, out var value)
                    ? null
                    : value.ValueKind switch
                    {
                        System.Text.Json.JsonValueKind.True => true,
                        System.Text.Json.JsonValueKind.False => false,
                        System.Text.Json.JsonValueKind.Null => null,
                        _ => value.ToString() is "1" or "true",
                    };
            }
            catch (System.Text.Json.JsonException)
            {
                return null;
            }
        }

        private static long? Long(IReadOnlyDictionary<string, object?> row, string column) =>
            row.TryGetValue(column, out var value) && value is not null
                && long.TryParse(Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture), System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var number)
                ? number
                : null;

        private static string? Text(IReadOnlyDictionary<string, object?> row, string column) =>
            row.TryGetValue(column, out var value) && value is not null ? Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture) : null;

        /// <summary>Mirrors gama-api's links exactly: adds, updates and removes. Returns how many there are.</summary>
        private async Task<int> SaveRelationsAsync(Dictionary<int, (int From, int To)> relations)
        {
            var now = DateTimeOffset.UtcNow;
            var uow = UnitOfWorkProvider.Value.CreateUnitOfWork();
            var repository = uow.GetRepository<LegacyTaxonomyRelation, int>();
            var existing = await repository.GetManyQueryable(tracking: true).ToListAsync();
            Dictionary<int, (int From, int To)> wanted = new(relations);
            foreach (var current in existing)
            {
                if (!wanted.Remove(current.Id, out var link))
                {
                    repository.Remove(current);
                    continue;
                }

                if (current.FromId != link.From || current.ToId != link.To)
                {
                    (current.FromId, current.ToId, current.SyncDate) = (link.From, link.To, now);
                }
            }

            foreach (var (id, link) in wanted)
            {
                repository.Add(new() { Id = id, FromId = link.From, ToId = link.To, SyncDate = now });
            }

            _ = await uow.SaveChangesAsync();
            return relations.Count;
        }

        private async Task<LegacyTaxonomySyncResultDto> SaveAsync(Dictionary<int, TaxonomyItemDto> items)
        {
            LegacyTaxonomySyncResultDto result = new();
            var now = DateTimeOffset.UtcNow;
            var uow = UnitOfWorkProvider.Value.CreateUnitOfWork();
            var repository = uow.GetRepository<LegacyTaxonomyItem, int>();
            var existing = await repository
                .GetManyQueryable(t => CopiedKinds.Contains(t.Kind), includes: t => t.Include(i => i.Parents), tracking: true)
                .ToDictionaryAsync(t => t.Id);

            foreach (var item in items.Values)
            {
                if (!existing.TryGetValue(item.Id, out var entity))
                {
                    entity = new() { Id = item.Id, Parents = [] };
                    Copy(item, entity, now);
                    repository.Add(entity);
                    result.Added++;
                    continue;
                }

                if (Same(item, entity))
                {
                    result.Unchanged++;
                    continue;
                }

                if (!string.Equals(entity.Parent, item.Parent, StringComparison.Ordinal))
                {
                    entity.Parents!.Clear();
                }

                Copy(item, entity, now);
                result.Updated++;
            }

            foreach (var entity in existing.Values.Where(t => !t.IsDeleted && !items.ContainsKey(t.Id)))
            {
                entity.IsDeleted = true;
                entity.SyncDate = now;
                result.Deleted++;
            }

            _ = await uow.SaveChangesAsync();
            return result;
        }

        private static bool Same(TaxonomyItemDto item, LegacyTaxonomyItem entity) =>
            entity.IsDeleted == item.IsDeleted
            && entity.Kind == item.Kind
            && entity.ListOrder == item.ListOrder
            && string.Equals(entity.Title, item.Title, StringComparison.Ordinal)
            && string.Equals(entity.Parent, item.Parent, StringComparison.Ordinal)
            && string.Equals(entity.Parent2, item.Parent2, StringComparison.Ordinal)
            && string.Equals(entity.Metadata, item.Metadata, StringComparison.Ordinal)
            && entity.IsSeason == item.IsSeason
            && entity.IsPaper == item.IsPaper;

        private static void Copy(TaxonomyItemDto item, LegacyTaxonomyItem entity, DateTimeOffset now)
        {
            entity.Kind = item.Kind;
            entity.Title = item.Title is { Length: > MaxTitle } ? item.Title[..MaxTitle] : item.Title;
            entity.ListOrder = item.ListOrder;
            entity.Parent = item.Parent;
            entity.Parent2 = item.Parent2;
            entity.Metadata = item.Metadata;
            entity.IsSeason = item.IsSeason;
            entity.IsPaper = item.IsPaper;
            entity.IsDeleted = item.IsDeleted;
            entity.SyncDate = now;
            if (entity.Parents!.Count == 0)
            {
                foreach (var key in ParentKeys(item.Parent))
                {
                    entity.Parents.Add(new() { ParentKey = key });
                }
            }
        }
    }
}
