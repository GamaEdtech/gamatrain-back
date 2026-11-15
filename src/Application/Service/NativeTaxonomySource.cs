namespace GamaEdtech.Application.Service
{
    using System.Diagnostics.CodeAnalysis;
    using System.Globalization;
    using System.Linq;

    using GamaEdtech.Common.Core;
    using GamaEdtech.Common.Data;
    using GamaEdtech.Common.DataAccess.UnitOfWork;
    using GamaEdtech.Common.Service;
    using GamaEdtech.Data.Dto.ExamImport;
    using GamaEdtech.Data.Dto.Taxonomy;
    using GamaEdtech.Domain.Entity;
    using GamaEdtech.Domain.Enumeration;
    using GamaEdtech.Infrastructure.Interface;

    using Microsoft.AspNetCore.Http;
    using Microsoft.EntityFrameworkCore;
    using Microsoft.Extensions.Logging;

    using static GamaEdtech.Common.Core.Constants;

    /// <summary>
    /// The Types domain answered from this backend's copy of gama-api's lookup lists (<see cref="LegacyTaxonomyItem"/>),
    /// with gama-api's rules: deleted items are left out, a list is ordered by its list order, and "under a parent" means
    /// the parent is one of the item's parent keys. Used only when <c>Migration:Routes:Types</c> says so. It does not
    /// read the current request (the shadow comparison calls it off the request).
    /// </summary>
    public sealed class NativeTaxonomySource(Lazy<IUnitOfWorkProvider> unitOfWorkProvider, Lazy<IHttpContextAccessor> httpContextAccessor,
        Lazy<ILogger<NativeTaxonomySource>> logger)
        : ServiceBase<NativeTaxonomySource>(unitOfWorkProvider, httpContextAccessor, logger), ITaxonomySource
    {
        // The MCP import's option kinds, as gama-api's list and the request field that is its parent (see CoreProvider).
        private static readonly Dictionary<string, (string List, Func<ExamImportOptionsRequestDto, TaxonomyListRequestDto> Request)> ExamOptions = new(StringComparer.Ordinal)
        {
            ["board"] = ("section", _ => new() { Type = "section" }),
            ["grade"] = ("base", t => new() { Type = "base", SectionId = t.ParentId }),
            ["course"] = ("course", t => new() { Type = "course", SectionId = t.ParentId }),
            ["subject"] = ("lesson", t => new() { Type = "lesson", BaseId = t.ParentId, CourseId = t.CourseId }),
            ["topic"] = ("topic", t => new() { Type = "topic", LessonId = t.ParentId }),
            ["paper"] = ("exam_type", _ => new() { Type = "exam_type" }),
            ["classification"] = ("test_type", t => new() { Type = "test_type", SectionId = t.ParentId }),
        };

        public MigrationRoute ProviderType => MigrationRoute.Native;

        public async Task<ResultData<IEnumerable<KeyValuePair<int, string?>>>> GetBoardsAsync()
        {
            var items = await GetTypeItemsAsync(new() { Type = "section" });
            return items.OperationResult is OperationResult.Succeeded
                ? new(OperationResult.Succeeded) { Data = [.. items.Data!.Select(t => new KeyValuePair<int, string?>(t.Id, t.Title))] }
                : new(items.OperationResult) { Errors = items.Errors };
        }

        public async Task<ResultData<IEnumerable<ExamImportOptionDto>>> GetExamOptionsAsync([NotNull] ExamImportOptionsRequestDto requestDto)
        {
            if (!ExamOptions.TryGetValue(requestDto.Kind, out var option))
            {
                return new(OperationResult.NotValid) { Errors = [new() { Message = $"Unknown kind {requestDto.Kind}." }] };
            }

            var items = await GetTypeItemsAsync(option.Request(requestDto));
            return items.OperationResult is OperationResult.Succeeded
                ? new(OperationResult.Succeeded) { Data = [.. items.Data!.Select(t => new ExamImportOptionDto { Id = t.Id, Title = t.Title?.Trim() })] }
                : new(items.OperationResult) { Errors = items.Errors };
        }

        public async Task<ResultData<IReadOnlyList<TaxonomyItemDto>>> GetTypeItemsAsync([NotNull] TaxonomyListRequestDto requestDto)
        {
            try
            {
                if (!LegacyTaxonomyKinds.ByListName.TryGetValue(requestDto.Type, out var kind))
                {
                    return new(OperationResult.NotValid) { Errors = [new() { Message = $"Unknown list {requestDto.Type}." }] };
                }

                // gama-api's own required parents: without them it answers an error, not an empty list.
                var parentKey = ParentKey(requestDto, kind, out var missing);
                if (missing is not null)
                {
                    return new(OperationResult.Failed) { Errors = [new() { Message = $"The field {missing} is required." }] };
                }

                var uow = UnitOfWorkProvider.Value.CreateUnitOfWork();
                var query = uow.GetRepository<LegacyTaxonomyItem, int>().GetManyQueryable(t => t.Kind == kind && !t.IsDeleted);
                if (parentKey is not null)
                {
                    query = query.Where(t => t.Parents!.Any(p => p.ParentKey == parentKey));
                }

                var list = await query
                    .OrderBy(t => t.ListOrder).ThenBy(t => t.Id)
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
                    })
                    .ToListAsync();
                return new(OperationResult.Succeeded) { Data = list };
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return new(OperationResult.Failed) { Errors = [new() { Message = exc.Message }] };
            }
        }

        /// <summary>The parent key a list is filtered by, or null for the whole list. gama-api ignores a parent of 0.</summary>
        internal static string? ParentKey(TaxonomyListRequestDto requestDto, short kind, out string? missingField)
        {
            missingField = null;
            static string? Key(long? id) => id is > 0 ? id.Value.ToString(CultureInfo.InvariantCulture) : null;
            switch (kind)
            {
                case LegacyTaxonomyKinds.Grade or LegacyTaxonomyKinds.Course:
                    missingField = Key(requestDto.SectionId) is null ? "section_id" : null;
                    return Key(requestDto.SectionId);
                case LegacyTaxonomyKinds.Subject:
                    missingField = Key(requestDto.BaseId) is null ? "base_id" : null;
                    return missingField is null ? $"{Key(requestDto.CourseId) ?? "0"},{Key(requestDto.BaseId)}" : null;
                case LegacyTaxonomyKinds.Topic:
                    missingField = Key(requestDto.LessonId) is null ? "lesson_id" : null;
                    return Key(requestDto.LessonId);
                case LegacyTaxonomyKinds.Classification:
                    return Key(requestDto.SectionId);
                default:
                    return null;
            }
        }
    }
}
