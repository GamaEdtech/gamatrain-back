namespace GamaEdtech.Application.Service
{
    using System.Linq;

    using GamaEdtech.Application.Interface;
    using GamaEdtech.Common.Core;
    using GamaEdtech.Common.Data;
    using GamaEdtech.Common.DataAccess.UnitOfWork;
    using GamaEdtech.Common.Service;
    using GamaEdtech.Common.Service.Factory;
    using GamaEdtech.Data.Dto.Taxonomy;
    using GamaEdtech.Domain.Entity;
    using GamaEdtech.Domain.Enumeration;
    using GamaEdtech.Infrastructure.Interface;

    using Microsoft.AspNetCore.Http;
    using Microsoft.EntityFrameworkCore;
    using Microsoft.Extensions.Logging;

    using static GamaEdtech.Common.Core.Constants;

    /// <summary>
    /// Walks gama-api's lookup lists from the top (boards, then each board's grades, courses and classifications, each
    /// grade's subjects, each subject's topics, and the paper types) and copies every item it sees. It always reads gama-api
    /// directly (the legacy source), whatever the Types route is: it is the copy, not a caller of the domain.
    /// </summary>
    public sealed class LegacyTaxonomySyncService(Lazy<IUnitOfWorkProvider> unitOfWorkProvider, Lazy<IHttpContextAccessor> httpContextAccessor,
        Lazy<ILogger<LegacyTaxonomySyncService>> logger, Lazy<IGenericFactory<ITaxonomySource, MigrationRoute>> sourceFactory)
        : ServiceBase<LegacyTaxonomySyncService>(unitOfWorkProvider, httpContextAccessor, logger), ILegacyTaxonomySyncService
    {
        // gama-api is a live site: one list at a time, with a pause between them.
        private static readonly TimeSpan PauseBetweenCalls = TimeSpan.FromMilliseconds(150);

        private static readonly short[] CopiedKinds = [.. LegacyTaxonomyKinds.ByListName.Values];

        public async Task<ResultData<LegacyTaxonomySyncResultDto>> SyncAsync()
        {
            try
            {
                var source = sourceFactory.Value.GetProvider(MigrationRoute.Legacy);
                if (source is null)
                {
                    return new(OperationResult.Failed) { Errors = [new() { Message = "gama-api's types source is not registered." }] };
                }

                var (items, calls, failed) = await CrawlAsync(source);
                if (failed > 0)
                {
                    // A partial copy would mark everything it missed as deleted: keep the old copy instead.
                    Logger.Value.LogWarning("Taxonomy sync stopped: {Failed} of {Calls} gama-api lists failed; nothing was changed", failed, calls);
                    return new(OperationResult.Failed) { Errors = [new() { Message = $"{failed} of {calls} gama-api lists failed; nothing was changed." }] };
                }

                var result = await SaveAsync(items);
                result.Calls = calls;
                Logger.Value.LogWarning("Taxonomy sync: {Calls} lists read, {Added} added, {Updated} updated, {Deleted} deleted, {Unchanged} unchanged",
                    result.Calls, result.Added, result.Updated, result.Deleted, result.Unchanged);
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

        private static async Task<(Dictionary<int, TaxonomyItemDto> Items, int Calls, int Failed)> CrawlAsync(ITaxonomySource source)
        {
            Dictionary<int, TaxonomyItemDto> items = [];
            var calls = 0;
            var failed = 0;

            async Task<List<TaxonomyItemDto>> ReadAsync(TaxonomyListRequestDto request)
            {
                if (calls > 0)
                {
                    await Task.Delay(PauseBetweenCalls);
                }

                calls++;
                var result = await source.GetTypeItemsAsync(request);
                if (result.OperationResult is not OperationResult.Succeeded || result.Data is null)
                {
                    failed++;
                    return [];
                }

                foreach (var item in result.Data)
                {
                    _ = items.TryAdd(item.Id, item);
                }

                return [.. result.Data];
            }

            static long Id(TaxonomyItemDto item) => item.Id;

            _ = await ReadAsync(new() { Type = "exam_type" });
            _ = await ReadAsync(new() { Type = "test_type" });
            foreach (var board in await ReadAsync(new() { Type = "section" }))
            {
                _ = await ReadAsync(new() { Type = "test_type", SectionId = Id(board) });
                var courses = await ReadAsync(new() { Type = "course", SectionId = Id(board) });
                foreach (var grade in await ReadAsync(new() { Type = "base", SectionId = Id(board) }))
                {
                    List<TaxonomyItemDto> subjects = [.. await ReadAsync(new() { Type = "lesson", BaseId = Id(grade) })];
                    foreach (var course in courses)
                    {
                        subjects.AddRange(await ReadAsync(new() { Type = "lesson", BaseId = Id(grade), CourseId = Id(course) }));
                    }

                    foreach (var subject in subjects.DistinctBy(t => t.Id))
                    {
                        _ = await ReadAsync(new() { Type = "topic", LessonId = Id(subject) });
                    }
                }
            }

            return (items, calls, failed);
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
            !entity.IsDeleted
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
            entity.Title = item.Title is { Length: > 500 } ? item.Title[..500] : item.Title;
            entity.ListOrder = item.ListOrder;
            entity.Parent = item.Parent;
            entity.Parent2 = item.Parent2;
            entity.Metadata = item.Metadata;
            entity.IsSeason = item.IsSeason;
            entity.IsPaper = item.IsPaper;
            entity.IsDeleted = false;
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
