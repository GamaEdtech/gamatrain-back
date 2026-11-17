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
    using GamaEdtech.Data.Dto.TopicMatch;
    using GamaEdtech.Domain.Entity;
    using GamaEdtech.Domain.Entity.Identity;
    using GamaEdtech.Domain.Enumeration;
    using GamaEdtech.Infrastructure.Interface;

    using Microsoft.AspNetCore.Http;
    using Microsoft.EntityFrameworkCore;
    using Microsoft.Extensions.Configuration;
    using Microsoft.Extensions.Logging;

    using static GamaEdtech.Common.Core.Constants;

    public sealed class TopicMatchImportService(Lazy<IUnitOfWorkProvider> unitOfWorkProvider, Lazy<IHttpContextAccessor> httpContextAccessor,
        Lazy<ILogger<TopicMatchImportService>> logger, Lazy<ILegacyDatabaseReader> reader, Lazy<IConfiguration> configuration,
        Lazy<IMigrationRouter> migrationRouter)
        : ServiceBase<TopicMatchImportService>(unitOfWorkProvider, httpContextAccessor, logger), ITopicMatchImportService
    {
        private const string Table = "topic_found_matches";
        private const int BatchSize = 2000;

        private static readonly string[] Columns =
        [
            "id", "gama_topic_id", "gama_lesson_id", "external_topic_id", "external_lesson_id", "confidence", "reasoning", "ai_model",
            "status", "reviewed_by", "sub_date", "up_date",
        ];

        private static readonly Dictionary<byte, TopicMatchStatus> Statuses =
            EnumerationExtensions.GetAll<TopicMatchStatus, byte>()!.ToDictionary(t => t!.Value, t => t!);

        public async Task<ResultData<TopicMatchImportResultDto>> ImportAsync()
        {
            try
            {
                if (!reader.Value.IsConfigured)
                {
                    return new(OperationResult.NotValid) { Errors = [new() { Message = "gama-api's database is not configured (Migration:LegacyDatabase:ConnectionString)." }] };
                }

                // Once the matches are reviewed here, the copy is over: it would undo the reviews.
                if (migrationRouter.Value.GetWriteRoute(MigrationDomain.Types, MigrationFeatures.TopicMatches) == MigrationRoute.Native)
                {
                    return new(OperationResult.NotValid) { Errors = [new() { Message = "Topic matches are reviewed here (Migration:WriteRoutes:Types.TopicMatches is Native); the copy from gama-api no longer runs." }] };
                }

                Dictionary<long, TopicMatch> read = [];
                var now = DateTimeOffset.UtcNow;
                var timeZone = LegacyTimeZone();
                var users = await CoreUserMapAsync();
                long afterId = 0;
                while (true)
                {
                    var batch = await reader.Value.ReadBatchAsync(Table, Columns, afterId, BatchSize);
                    if (batch.OperationResult is not OperationResult.Succeeded || batch.Data is null)
                    {
                        Logger.Value.LogWarning("Topic match copy stopped: a read of gama-api's {Table} failed; nothing was changed", Table);
                        return new(batch.OperationResult) { Errors = batch.Errors };
                    }

                    if (batch.Data.Count == 0)
                    {
                        break;
                    }

                    afterId = batch.Data.Max(t => Long(t, "id") ?? 0);
                    foreach (var match in batch.Data.Select(t => ToMatch(t, users, timeZone, now)).OfType<TopicMatch>())
                    {
                        read[match.Id] = match;
                    }
                }

                TopicMatchImportResultDto result = new() { Read = read.Count };
                var uow = UnitOfWorkProvider.Value.CreateUnitOfWork();
                var repository = uow.GetRepository<TopicMatch>();
                foreach (var current in await repository.GetManyQueryable(t => true, tracking: true).ToListAsync())
                {
                    if (!read.Remove(current.Id, out var match))
                    {
                        repository.Remove(current);
                        result.Removed++;
                    }
                    else if (CopyInto(match, current))
                    {
                        result.Updated++;
                    }
                }

                foreach (var match in read.Values)
                {
                    repository.Add(match);
                    result.Added++;
                }

                _ = await uow.SaveChangesAsync();
                Logger.Value.LogWarning("Topic match copy: {Read} read, {Added} added, {Updated} updated, {Removed} removed", result.Read, result.Added, result.Updated, result.Removed);
                return new(OperationResult.Succeeded) { Data = result };
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return new(OperationResult.Failed) { Errors = [new() { Message = exc.Message }] };
            }
        }

        /// <summary>A match from a <c>topic_found_matches</c> row; null without both topics. Internal for the tests.</summary>
        internal static TopicMatch? ToMatch(IReadOnlyDictionary<string, object?> row, IReadOnlyDictionary<long, long> users, TimeZoneInfo timeZone, DateTimeOffset now)
        {
            if (Long(row, "id") is not long id || Int(row, "gama_topic_id") is not int gamaTopic || Int(row, "external_topic_id") is not int externalTopic)
            {
                return null;
            }

            var reviewer = Long(row, "reviewed_by") is long core and > 0 ? core : (long?)null;
            var created = Date(row, "sub_date", timeZone) ?? now;
            return new()
            {
                Id = id,
                GamaTopicId = gamaTopic,
                GamaSubjectId = Int(row, "gama_lesson_id") ?? 0,
                ExternalTopicId = externalTopic,
                ExternalSubjectId = Int(row, "external_lesson_id") ?? 0,
                Confidence = (byte)Math.Clamp(Long(row, "confidence") ?? 0, 0, 100),
                Reasoning = Cut(Text(row, "reasoning")?.Trim(), 2000),
                AiModel = Cut(Text(row, "ai_model")?.Trim(), 50),
                Status = Statuses.TryGetValue((byte)Math.Clamp(Long(row, "status") ?? 0, 0, 255), out var status) ? status : TopicMatchStatus.Pending,
                CoreReviewedBy = reviewer,
                ReviewedByUserId = reviewer is long r && users.TryGetValue(r, out var user) ? user : null,
                CreationDate = created,
                LastModifyDate = Date(row, "up_date", timeZone) ?? created,
                SyncDate = now,
            };
        }

        private static bool CopyInto(TopicMatch from, TopicMatch to)
        {
            var changed = to.GamaTopicId != from.GamaTopicId || to.GamaSubjectId != from.GamaSubjectId || to.ExternalTopicId != from.ExternalTopicId
                || to.ExternalSubjectId != from.ExternalSubjectId || to.Confidence != from.Confidence || to.Reasoning != from.Reasoning
                || to.AiModel != from.AiModel || to.Status != from.Status || to.CoreReviewedBy != from.CoreReviewedBy
                || to.ReviewedByUserId != from.ReviewedByUserId || to.LastModifyDate != from.LastModifyDate;
            if (changed)
            {
                to.GamaTopicId = from.GamaTopicId;
                to.GamaSubjectId = from.GamaSubjectId;
                to.ExternalTopicId = from.ExternalTopicId;
                to.ExternalSubjectId = from.ExternalSubjectId;
                to.Confidence = from.Confidence;
                to.Reasoning = from.Reasoning;
                to.AiModel = from.AiModel;
                to.Status = from.Status;
                to.CoreReviewedBy = from.CoreReviewedBy;
                to.ReviewedByUserId = from.ReviewedByUserId;
                to.CreationDate = from.CreationDate;
                to.LastModifyDate = from.LastModifyDate;
                to.SyncDate = from.SyncDate;
            }

            return changed;
        }

        private static long? Long(IReadOnlyDictionary<string, object?> row, string column) =>
            row.TryGetValue(column, out var value) && value is not null
                && long.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture), NumberStyles.Integer, CultureInfo.InvariantCulture, out var number)
                ? number
                : null;

        private static int? Int(IReadOnlyDictionary<string, object?> row, string column) => Long(row, column) is long id and > 0 and <= int.MaxValue ? (int)id : null;

        private static string? Text(IReadOnlyDictionary<string, object?> row, string column) =>
            row.TryGetValue(column, out var value) && value is not null ? Convert.ToString(value, CultureInfo.InvariantCulture) : null;

        private static string? Cut(string? value, int length) => value is { Length: > 0 } && value.Length > length ? value[..length] : value;

        private static DateTimeOffset? Date(IReadOnlyDictionary<string, object?> row, string column, TimeZoneInfo timeZone) =>
            row.TryGetValue(column, out var value) switch
            {
                false => null,
                _ when value is DateTime date => date == DateTime.MinValue
                    ? null
                    : new DateTimeOffset(DateTime.SpecifyKind(date, DateTimeKind.Unspecified), timeZone.GetUtcOffset(date)),
                _ => null,
            };

        private TimeZoneInfo LegacyTimeZone()
        {
            var id = configuration.Value["Migration:LegacyDatabase:TimeZone"];
            return !string.IsNullOrWhiteSpace(id) && TimeZoneInfo.TryFindSystemTimeZoneById(id, out var zone) ? zone : TimeZoneInfo.Utc;
        }

        private async Task<Dictionary<long, long>> CoreUserMapAsync()
        {
            var uow = UnitOfWorkProvider.Value.CreateUnitOfWork();
            var pairs = await uow.GetRepository<ApplicationUser>().GetManyQueryable(t => t.CoreId != null)
                .Select(t => new { CoreId = t.CoreId!.Value, t.Id }).ToListAsync();
            return pairs.GroupBy(t => t.CoreId).ToDictionary(t => t.Key, t => t.Min(u => u.Id));
        }
    }
}
