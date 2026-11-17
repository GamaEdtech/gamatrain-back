namespace GamaEdtech.Application.Service
{
    using System.Globalization;
    using System.Linq;

    using GamaEdtech.Application.Interface;
    using GamaEdtech.Common.Core;
    using GamaEdtech.Common.Data;
    using GamaEdtech.Common.Data.Enumeration;
    using GamaEdtech.Common.DataAccess.UnitOfWork;
    using GamaEdtech.Common.Service;
    using GamaEdtech.Data.Dto.Tutorial;
    using GamaEdtech.Domain.Entity;
    using GamaEdtech.Domain.Entity.Identity;
    using GamaEdtech.Domain.Enumeration;
    using GamaEdtech.Infrastructure.Interface;

    using Microsoft.AspNetCore.Http;
    using Microsoft.EntityFrameworkCore;
    using Microsoft.Extensions.Configuration;
    using Microsoft.Extensions.Logging;

    using static GamaEdtech.Common.Core.Constants;

    public sealed class TutorialImportService(Lazy<IUnitOfWorkProvider> unitOfWorkProvider, Lazy<IHttpContextAccessor> httpContextAccessor,
        Lazy<ILogger<TutorialImportService>> logger, Lazy<ILegacyDatabaseReader> reader, Lazy<IConfiguration> configuration,
        Lazy<IMigrationRouter> migrationRouter)
        : ServiceBase<TutorialImportService>(unitOfWorkProvider, httpContextAccessor, logger), ITutorialImportService
    {
        // A tutorial's HTML can be large; smaller batches keep each read and save modest.
        private const int TutorialBatchSize = 200;
        private const int ImageBatchSize = 2000;
        private const string TutorialsTable = "dars";
        private const string ImagesTable = "tutorials_files";

        // The columns read, never the rest (elastic_sync, sub_topics and keywords are internal or empty in gama-api).
        private static readonly string[] TutorialColumns =
        [
            "id", "user_", "referee", "section", "base", "course", "lesson", "topic", "topic_order", "title", "description",
            "content", "content_size", "status", "views", "subdate", "up_date",
        ];

        private static readonly string[] ImageColumns = ["id", "tutorial_", "file_name", "subdate"];

        private static readonly Dictionary<byte, TutorialStatus> Statuses =
            EnumerationExtensions.GetAll<TutorialStatus, byte>()!.ToDictionary(t => t!.Value, t => t!);

        public async Task<ResultData<TutorialImportResultDto>> ImportAsync()
        {
            try
            {
                if (!reader.Value.IsConfigured)
                {
                    return new(OperationResult.NotValid) { Errors = [new() { Message = "gama-api's database is not configured (Migration:LegacyDatabase:ConnectionString)." }] };
                }

                // Once tutorials are edited here, the copy is over: it would put gama-api's older version back over the edits.
                if (migrationRouter.Value.GetWriteRoute(MigrationDomain.Tutorials) == MigrationRoute.Native)
                {
                    return new(OperationResult.NotValid) { Errors = [new() { Message = "Tutorials are edited here (Migration:WriteRoutes:Tutorials is Native); the copy from gama-api no longer runs." }] };
                }

                TutorialImportResultDto result = new();
                var users = await CoreUserMapAsync();
                var fallbackOwner = await FallbackOwnerAsync();
                var timeZone = LegacyTimeZone();

                var tutorials = await ImportTutorialsAsync(result, users, fallbackOwner, timeZone);
                if (tutorials.OperationResult is not OperationResult.Succeeded)
                {
                    return new(tutorials.OperationResult) { Data = result, Errors = tutorials.Errors };
                }

                var images = await ImportImagesAsync(result, timeZone);
                if (images.OperationResult is not OperationResult.Succeeded)
                {
                    return new(images.OperationResult) { Data = result, Errors = images.Errors };
                }

                Logger.Value.LogWarning("Tutorial copy: {Read} tutorials read, {Added} added, {Updated} updated, {WithoutUser} without a linked user; "
                    + "{ImagesRead} images read, {ImagesAdded} added, {ImagesUpdated} updated, {ImagesSkipped} skipped",
                    result.TutorialsRead, result.TutorialsAdded, result.TutorialsUpdated, result.TutorialsWithoutUser,
                    result.ImagesRead, result.ImagesAdded, result.ImagesUpdated, result.ImagesSkipped);
                return new(OperationResult.Succeeded) { Data = result };
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return new(OperationResult.Failed) { Errors = [new() { Message = exc.Message }] };
            }
        }

        /// <summary>A tutorial from a <c>dars</c> row. Internal for the tests.</summary>
        internal static Tutorial ToTutorial(IReadOnlyDictionary<string, object?> row, IReadOnlyDictionary<long, long> users, TimeZoneInfo timeZone,
            DateTimeOffset now, long? fallbackOwner = null)
        {
            var coreOwnerId = Long(row, "user_") ?? 0;
            long? ownerUserId = users.TryGetValue(coreOwnerId, out var linked) ? linked : null;
            var created = Date(row, "subdate", timeZone) ?? now;
            return new()
            {
                Id = Long(row, "id") ?? throw new InvalidOperationException("A gama-api tutorial without an id."),
                CoreOwnerId = coreOwnerId,
                OwnerUserId = ownerUserId,
                DisplayOwnerUserId = ownerUserId ?? fallbackOwner,
                CoreReferee = Cut(Empty(Text(row, "referee")), 10),
                CoreBoardId = Id(row, "section"),
                CoreGradeId = Id(row, "base"),
                CoreCourseId = Id(row, "course"),
                CoreSubjectId = Id(row, "lesson"),
                CoreTopicIds = Cut(Empty(Text(row, "topic")), 100),
                TopicOrder = (int?)Long(row, "topic_order"),
                Title = Cut(Text(row, "title")?.Trim() ?? string.Empty, 1000),
                Description = Cut(Empty(Text(row, "description")), 1000),
                Content = Text(row, "content"),
                ContentSize = (int?)Long(row, "content_size"),
                Status = Statuses.TryGetValue((byte)(Long(row, "status") ?? 0), out var status) ? status : TutorialStatus.Pending,
                ViewCount = (int)Math.Clamp(Long(row, "views") ?? 0, 0, int.MaxValue),
                CreationDate = created,
                LastModifyDate = Date(row, "up_date", timeZone) ?? created,
                SyncDate = now,
            };
        }

        /// <summary>An image row from a <c>tutorials_files</c> row; null without a tutorial or a file. Internal for the tests.</summary>
        internal static TutorialImage? ToImage(IReadOnlyDictionary<string, object?> row, TimeZoneInfo timeZone) =>
            Long(row, "id") is long id && Long(row, "tutorial_") is long tutorialId and > 0 && Empty(Text(row, "file_name")) is { } path
                ? new() { CoreFileId = id, TutorialId = tutorialId, Path = Cut(path.TrimStart('/'), 200), CreationDate = Date(row, "subdate", timeZone) }
                : null;

        private async Task<ResultData<bool>> ImportTutorialsAsync(TutorialImportResultDto result, Dictionary<long, long> users, long? fallbackOwner, TimeZoneInfo timeZone)
        {
            long afterId = 0;
            while (true)
            {
                var batch = await reader.Value.ReadBatchAsync(TutorialsTable, TutorialColumns, afterId, TutorialBatchSize);
                if (batch.OperationResult is not OperationResult.Succeeded || batch.Data is null)
                {
                    return new(batch.OperationResult) { Errors = batch.Errors };
                }

                if (batch.Data.Count == 0)
                {
                    return new(OperationResult.Succeeded) { Data = true };
                }

                var now = DateTimeOffset.UtcNow;
                List<Tutorial> tutorials = [.. batch.Data.Select(t => ToTutorial(t, users, timeZone, now, fallbackOwner))];
                List<long> ids = [.. tutorials.Select(t => t.Id)];

                var uow = UnitOfWorkProvider.Value.CreateUnitOfWork();
                var repository = uow.GetRepository<Tutorial>();
                var existing = await repository.GetManyQueryable(t => ids.Contains(t.Id), tracking: true).ToDictionaryAsync(t => t.Id);
                foreach (var tutorial in tutorials)
                {
                    if (existing.TryGetValue(tutorial.Id, out var current))
                    {
                        CopyInto(tutorial, current);
                        result.TutorialsUpdated++;
                    }
                    else
                    {
                        repository.Add(tutorial);
                        result.TutorialsAdded++;
                    }

                    if (tutorial.OwnerUserId is null)
                    {
                        result.TutorialsWithoutUser++;
                    }
                }

                _ = await uow.SaveChangesAsync();
                result.TutorialsRead += tutorials.Count;
                afterId = ids.Max();
            }
        }

        private async Task<ResultData<bool>> ImportImagesAsync(TutorialImportResultDto result, TimeZoneInfo timeZone)
        {
            var uow = UnitOfWorkProvider.Value.CreateUnitOfWork();
            var tutorialIds = (await uow.GetRepository<Tutorial>().GetManyQueryable(t => true).Select(t => t.Id).ToListAsync()).ToHashSet();
            long afterId = 0;
            while (true)
            {
                var batch = await reader.Value.ReadBatchAsync(ImagesTable, ImageColumns, afterId, ImageBatchSize);
                if (batch.OperationResult is not OperationResult.Succeeded || batch.Data is null)
                {
                    return new(batch.OperationResult) { Errors = batch.Errors };
                }

                if (batch.Data.Count == 0)
                {
                    return new(OperationResult.Succeeded) { Data = true };
                }

                result.ImagesRead += batch.Data.Count;
                afterId = batch.Data.Max(t => Long(t, "id") ?? 0);
                List<TutorialImage> images = [];
                foreach (var row in batch.Data)
                {
                    if (ToImage(row, timeZone) is { } image && tutorialIds.Contains(image.TutorialId))
                    {
                        images.Add(image);
                    }
                    else
                    {
                        result.ImagesSkipped++;
                    }
                }

                List<long> fileIds = [.. images.Select(t => t.CoreFileId)];
                var batchUow = UnitOfWorkProvider.Value.CreateUnitOfWork();
                var repository = batchUow.GetRepository<TutorialImage>();
                var existing = await repository.GetManyQueryable(t => fileIds.Contains(t.CoreFileId), tracking: true).ToDictionaryAsync(t => t.CoreFileId);
                foreach (var image in images)
                {
                    if (existing.TryGetValue(image.CoreFileId, out var current))
                    {
                        if (current.TutorialId != image.TutorialId || current.Path != image.Path || current.CreationDate != image.CreationDate)
                        {
                            current.TutorialId = image.TutorialId;
                            current.Path = image.Path;
                            current.CreationDate = image.CreationDate;
                            result.ImagesUpdated++;
                        }
                    }
                    else
                    {
                        repository.Add(image);
                        result.ImagesAdded++;
                    }
                }

                _ = await batchUow.SaveChangesAsync();
            }
        }

        private static void CopyInto(Tutorial from, Tutorial to)
        {
            to.CoreOwnerId = from.CoreOwnerId;
            to.OwnerUserId = from.OwnerUserId;
            to.DisplayOwnerUserId = from.DisplayOwnerUserId;
            to.CoreReferee = from.CoreReferee;
            to.CoreBoardId = from.CoreBoardId;
            to.CoreGradeId = from.CoreGradeId;
            to.CoreCourseId = from.CoreCourseId;
            to.CoreSubjectId = from.CoreSubjectId;
            to.CoreTopicIds = from.CoreTopicIds;
            to.TopicOrder = from.TopicOrder;
            to.Title = from.Title;
            to.Description = from.Description;
            to.Content = from.Content;
            to.ContentSize = from.ContentSize;
            to.Status = from.Status;
            to.ViewCount = from.ViewCount;
            to.CreationDate = from.CreationDate;
            to.LastModifyDate = from.LastModifyDate;
            to.SyncDate = from.SyncDate;
        }

        private static long? Long(IReadOnlyDictionary<string, object?> row, string column) =>
            row.TryGetValue(column, out var value) && value is not null
                && long.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture), NumberStyles.Integer, CultureInfo.InvariantCulture, out var number)
                ? number
                : null;

        // A type id: gama-api writes 0 for none.
        private static int? Id(IReadOnlyDictionary<string, object?> row, string column) => Long(row, column) is long id and > 0 and <= int.MaxValue ? (int)id : null;

        private static string? Text(IReadOnlyDictionary<string, object?> row, string column) =>
            row.TryGetValue(column, out var value) && value is not null ? Convert.ToString(value, CultureInfo.InvariantCulture) : null;

        private static string? Empty(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

        private static string? Cut(string? value, int length) => value is { Length: > 0 } && value.Length > length ? value[..length] : value;

        // gama-api's MySQL runs in UTC (Migration:LegacyDatabase:TimeZone overrides it); a unix time is exact either way.
        private static DateTimeOffset? Date(IReadOnlyDictionary<string, object?> row, string column, TimeZoneInfo timeZone) =>
            row.TryGetValue(column, out var value) switch
            {
                false => null,
                _ when value is DateTime date => date == DateTime.MinValue
                    ? null
                    : new DateTimeOffset(DateTime.SpecifyKind(date, DateTimeKind.Unspecified), timeZone.GetUtcOffset(date)),
                _ => Long(row, column) is long seconds and > 0 ? DateTimeOffset.FromUnixTimeSeconds(seconds) : null,
            };

        private TimeZoneInfo LegacyTimeZone()
        {
            var id = configuration.Value["Migration:LegacyDatabase:TimeZone"];
            return !string.IsNullOrWhiteSpace(id) && TimeZoneInfo.TryFindSystemTimeZoneById(id, out var zone) ? zone : TimeZoneInfo.Utc;
        }

        /// <summary>The user shown as author of the tutorials whose owner has no account here: the same fallback user as the
        /// past papers' (<c>Migration:PastPapers:FallbackOwnerUserId</c>); null when unset or missing.</summary>
        private async Task<long?> FallbackOwnerAsync()
        {
            if (configuration.Value.GetValue<long?>("Migration:PastPapers:FallbackOwnerUserId") is not long id || id <= 0)
            {
                return null;
            }

            var uow = UnitOfWorkProvider.Value.CreateUnitOfWork();
            return await uow.GetRepository<ApplicationUser>().AnyAsync(t => t.Id == id) ? id : null;
        }

        /// <summary>gama-api user id → this backend's user id, for every user linked by <c>CoreId</c>.</summary>
        private async Task<Dictionary<long, long>> CoreUserMapAsync()
        {
            var uow = UnitOfWorkProvider.Value.CreateUnitOfWork();
            var pairs = await uow.GetRepository<ApplicationUser>().GetManyQueryable(t => t.CoreId != null)
                .Select(t => new { CoreId = t.CoreId!.Value, t.Id }).ToListAsync();
            return pairs.GroupBy(t => t.CoreId).ToDictionary(t => t.Key, t => t.Min(u => u.Id));
        }
    }
}
