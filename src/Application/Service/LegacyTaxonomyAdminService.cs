namespace GamaEdtech.Application.Service
{
    using System.Diagnostics.CodeAnalysis;
    using System.Linq;

    using GamaEdtech.Application.Interface;
    using GamaEdtech.Common.Core;
    using GamaEdtech.Common.Data;
    using GamaEdtech.Common.DataAccess.UnitOfWork;
    using GamaEdtech.Common.Service;
    using GamaEdtech.Data.Dto.Taxonomy;
    using GamaEdtech.Domain.Entity;
    using GamaEdtech.Domain.Enumeration;

    using Microsoft.AspNetCore.Http;
    using Microsoft.EntityFrameworkCore;
    using Microsoft.Extensions.Logging;

    using static GamaEdtech.Common.Core.Constants;

    public sealed class LegacyTaxonomyAdminService(Lazy<IUnitOfWorkProvider> unitOfWorkProvider, Lazy<IHttpContextAccessor> httpContextAccessor,
        Lazy<ILogger<LegacyTaxonomyAdminService>> logger, Lazy<IMigrationRouter> migrationRouter)
        : ServiceBase<LegacyTaxonomyAdminService>(unitOfWorkProvider, httpContextAccessor, logger), ILegacyTaxonomyAdminService
    {
        private const int MaxPageSize = 500;

        public async Task<ResultData<TaxonomyItemListDto>> GetItemsAsync(short kind, string? parentKey, string? title, bool includeDeleted, int page, int pageSize)
        {
            try
            {
                var uow = UnitOfWorkProvider.Value.CreateUnitOfWork();
                var items = uow.GetRepository<LegacyTaxonomyItem, int>().GetManyQueryable(t => t.Kind == kind && (includeDeleted || !t.IsDeleted));
                if (!string.IsNullOrWhiteSpace(parentKey))
                {
                    var key = parentKey.Trim();
                    items = items.Where(t => t.Parents!.Any(p => p.ParentKey == key));
                }

                if (!string.IsNullOrWhiteSpace(title))
                {
                    var text = title.Trim();
                    items = items.Where(t => t.Title!.Contains(text));
                }

                var size = Math.Clamp(pageSize, 1, MaxPageSize);
                var total = await items.CountAsync();
                var list = await items.OrderBy(t => t.ListOrder).ThenBy(t => t.Id).Skip((Math.Max(page, 1) - 1) * size).Take(size)
                    .Select(t => new TaxonomyItemDto
                    {
                        Id = t.Id,
                        Kind = t.Kind,
                        Title = t.Title,
                        ListOrder = t.ListOrder,
                        Parent = t.Parent,
                        Parent2 = t.Parent2,
                        Metadata = t.Metadata,
                        IsSeason = t.IsSeason,
                        IsPaper = t.IsPaper,
                        Picture = t.Picture,
                        IsDeleted = t.IsDeleted,
                    })
                    .ToListAsync();
                return new(OperationResult.Succeeded) { Data = new() { Items = list, Total = total } };
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return new(OperationResult.Failed) { Errors = [new() { Message = exc.Message }] };
            }
        }

        public async Task<ResultData<TaxonomyItemDto>> GetItemAsync(int id)
        {
            var items = await GetByIdAsync(id);
            return items;
        }

        public async Task<ResultData<int>> CreateItemAsync([NotNull] TaxonomyItemEditDto requestDto)
        {
            try
            {
                if (Refusal<int>(requestDto) is { } refused)
                {
                    return refused;
                }

                var uow = UnitOfWorkProvider.Value.CreateUnitOfWork();
                var repository = uow.GetRepository<LegacyTaxonomyItem, int>();
                LegacyTaxonomyItem item = new()
                {
                    Id = (await repository.GetManyQueryable(t => true).MaxAsync(t => (int?)t.Id) ?? 0) + 1,
                    Kind = requestDto.Kind,
                    SyncDate = DateTimeOffset.UtcNow,
                    Parents = [],
                };
                Apply(item, requestDto);
                repository.Add(item);
                _ = await uow.SaveChangesAsync();
                return new(OperationResult.Succeeded) { Data = item.Id };
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return new(OperationResult.Failed) { Errors = [new() { Message = exc.Message }] };
            }
        }

        public async Task<ResultData<bool>> UpdateItemAsync(int id, [NotNull] TaxonomyItemEditDto requestDto)
        {
            try
            {
                if (Refusal<bool>(requestDto) is { } refused)
                {
                    return refused;
                }

                var uow = UnitOfWorkProvider.Value.CreateUnitOfWork();
                var item = await uow.GetRepository<LegacyTaxonomyItem, int>()
                    .GetManyQueryable(t => t.Id == id, includes: t => t.Include(p => p.Parents), tracking: true).FirstOrDefaultAsync();
                if (item is null)
                {
                    return new(OperationResult.NotFound) { Errors = [new() { Message = "No such item." }] };
                }

                if (item.Kind != requestDto.Kind)
                {
                    return new(OperationResult.NotValid) { Errors = [new() { Message = "An item stays in its list." }] };
                }

                Apply(item, requestDto);
                _ = await uow.SaveChangesAsync();
                return new(OperationResult.Succeeded) { Data = true };
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return new(OperationResult.Failed) { Errors = [new() { Message = exc.Message }] };
            }
        }

        public async Task<ResultData<bool>> RemoveItemAsync(int id)
        {
            try
            {
                if (NotNative<bool>() is { } refused)
                {
                    return refused;
                }

                var uow = UnitOfWorkProvider.Value.CreateUnitOfWork();
                var item = await uow.GetRepository<LegacyTaxonomyItem, int>().GetManyQueryable(t => t.Id == id, tracking: true).FirstOrDefaultAsync();
                if (item is null)
                {
                    return new(OperationResult.NotFound) { Errors = [new() { Message = "No such item." }] };
                }

                item.IsDeleted = true;
                item.SyncDate = DateTimeOffset.UtcNow;
                _ = await uow.SaveChangesAsync();
                return new(OperationResult.Succeeded) { Data = true };
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return new(OperationResult.Failed) { Errors = [new() { Message = exc.Message }] };
            }
        }

        private async Task<ResultData<TaxonomyItemDto>> GetByIdAsync(int id)
        {
            try
            {
                var uow = UnitOfWorkProvider.Value.CreateUnitOfWork();
                var item = await uow.GetRepository<LegacyTaxonomyItem, int>().GetManyQueryable(t => t.Id == id)
                    .Select(t => new TaxonomyItemDto
                    {
                        Id = t.Id,
                        Kind = t.Kind,
                        Title = t.Title,
                        ListOrder = t.ListOrder,
                        Parent = t.Parent,
                        Parent2 = t.Parent2,
                        Metadata = t.Metadata,
                        IsSeason = t.IsSeason,
                        IsPaper = t.IsPaper,
                        Picture = t.Picture,
                        IsDeleted = t.IsDeleted,
                    })
                    .FirstOrDefaultAsync();
                return item is null
                    ? new(OperationResult.NotFound) { Errors = [new() { Message = "No such item." }] }
                    : new(OperationResult.Succeeded) { Data = item };
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return new(OperationResult.Failed) { Errors = [new() { Message = exc.Message }] };
            }
        }

        /// <summary>Sets the item's columns, and the flags and parent keys the copy derives from them.</summary>
        private static void Apply(LegacyTaxonomyItem item, TaxonomyItemEditDto requestDto)
        {
            item.Title = requestDto.Title!.Trim();
            item.ListOrder = requestDto.ListOrder;
            item.Parent = string.IsNullOrWhiteSpace(requestDto.Parent) ? string.Empty : requestDto.Parent.Trim();
            item.Parent2 = string.IsNullOrWhiteSpace(requestDto.Parent2) ? null : requestDto.Parent2.Trim();
            item.Metadata = string.IsNullOrWhiteSpace(requestDto.Metadata) ? null : requestDto.Metadata.Trim();
            item.IsSeason = LegacyTaxonomySyncService.MetadataFlag(item.Metadata, "season");
            item.IsPaper = LegacyTaxonomySyncService.MetadataFlag(item.Metadata, "is_paper");
            item.SyncDate = DateTimeOffset.UtcNow;

            var keys = LegacyTaxonomySyncService.ParentKeys(item.Parent);
            var parents = item.Parents ??= [];
            foreach (var stale in parents.Where(t => !keys.Contains(t.ParentKey!)).ToList())
            {
                _ = parents.Remove(stale);
            }

            foreach (var key in keys.Where(t => !parents.Any(p => p.ParentKey == t)))
            {
                parents.Add(new() { ParentKey = key });
            }
        }

        /// <summary>Why an add or edit can't be made, or none: a list the copy holds, a title, metadata that is a JSON object.</summary>
        private ResultData<T>? Refusal<T>(TaxonomyItemEditDto requestDto)
        {
            if (NotNative<T>() is { } refused)
            {
                return refused;
            }

            if (!LegacyTaxonomyKinds.ByListName.Values.Contains(requestDto.Kind))
            {
                return new(OperationResult.NotValid) { Errors = [new() { Message = "Not a list this backend holds." }] };
            }

            if (string.IsNullOrWhiteSpace(requestDto.Title) || requestDto.Title.Trim().Length > 500)
            {
                return new(OperationResult.NotValid) { Errors = [new() { Message = "The title is required (at most 500 characters)." }] };
            }

            if (!string.IsNullOrWhiteSpace(requestDto.Metadata))
            {
                try
                {
                    if (System.Text.Json.Nodes.JsonNode.Parse(requestDto.Metadata) is not System.Text.Json.Nodes.JsonObject)
                    {
                        return new(OperationResult.NotValid) { Errors = [new() { Message = "The metadata must be a JSON object." }] };
                    }
                }
                catch (System.Text.Json.JsonException)
                {
                    return new(OperationResult.NotValid) { Errors = [new() { Message = "The metadata must be a JSON object." }] };
                }
            }

            return null;
        }

        private ResultData<T>? NotNative<T>() =>
            migrationRouter.Value.GetWriteRoute(MigrationDomain.Types) == MigrationRoute.Native
                ? null
                : new(OperationResult.NotValid)
                {
                    Errors = [new() { Message = "The lists are edited on gama-api until Migration:WriteRoutes:Types is Native.", Reference = "notNative" }],
                };
    }
}
