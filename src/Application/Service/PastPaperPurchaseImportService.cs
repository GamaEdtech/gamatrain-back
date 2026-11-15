namespace GamaEdtech.Application.Service
{
    using System.Globalization;
    using System.Linq;

    using GamaEdtech.Application.Interface;
    using GamaEdtech.Common.Core;
    using GamaEdtech.Common.Data;
    using GamaEdtech.Common.DataAccess.UnitOfWork;
    using GamaEdtech.Common.Service;
    using GamaEdtech.Data.Dto.PastPaper;
    using GamaEdtech.Domain.Entity;
    using GamaEdtech.Domain.Entity.Identity;
    using GamaEdtech.Domain.Enumeration;
    using GamaEdtech.Infrastructure.Interface;

    using Microsoft.AspNetCore.Http;
    using Microsoft.EntityFrameworkCore;
    using Microsoft.Extensions.Configuration;
    using Microsoft.Extensions.Logging;

    using static GamaEdtech.Common.Core.Constants;

    /// <summary>
    /// gama-api's payments are read in id order, a batch at a time, through a column list (card numbers, IPs and bank
    /// fields never leave gama-api). Each batch is saved in its own unit of work; a rerun updates by payment id.
    /// </summary>
    public sealed class PastPaperPurchaseImportService(Lazy<IUnitOfWorkProvider> unitOfWorkProvider, Lazy<IHttpContextAccessor> httpContextAccessor,
        Lazy<ILogger<PastPaperPurchaseImportService>> logger, Lazy<ILegacyDatabaseReader> reader, Lazy<IConfiguration> configuration)
        : ServiceBase<PastPaperPurchaseImportService>(unitOfWorkProvider, httpContextAccessor, logger), IPastPaperPurchaseImportService
    {
        private const int BatchSize = 2000;
        private const string PaymentsTable = "payments";
        private static readonly string[] PaymentColumns = ["id", "user_", "usage_id", "type", "status", "price", "subdate"];

        // gama-api's payment types for a past paper file, and the statuses its own "already paid" check accepts.
        private const byte QuestionType = 1;
        private const byte AnswerType = 2;
        private const byte WordType = 8;
        private const byte ExtraType = 12;
        private static readonly HashSet<long> PaperFileTypes = [QuestionType, AnswerType, WordType, ExtraType];
        private static readonly HashSet<long> PaidStatuses = [1, 2, 3];

        public async Task<ResultData<PastPaperPurchaseImportResultDto>> ImportAsync()
        {
            try
            {
                if (!reader.Value.IsConfigured)
                {
                    return new(OperationResult.NotValid) { Errors = [new() { Message = "gama-api's database is not configured (Migration:LegacyDatabase:ConnectionString)." }] };
                }

                PastPaperPurchaseImportResultDto result = new();
                var timeZone = LegacyTimeZone();
                long afterId = 0;
                while (true)
                {
                    var batch = await reader.Value.ReadBatchAsync(PaymentsTable, PaymentColumns, afterId, BatchSize);
                    if (batch.OperationResult is not OperationResult.Succeeded || batch.Data is null)
                    {
                        return new(batch.OperationResult) { Data = result, Errors = batch.Errors };
                    }

                    if (batch.Data.Count == 0)
                    {
                        break;
                    }

                    afterId = batch.Data.Max(t => Long(t, "id") ?? 0);
                    result.PaymentsRead += batch.Data.Count;

                    var now = DateTimeOffset.UtcNow;
                    var purchases = batch.Data.Select(t => ToPurchase(t, timeZone, now)).OfType<LegacyPastPaperPurchase>().ToList();
                    if (purchases.Count == 0)
                    {
                        continue;
                    }

                    result.Purchases += purchases.Count;
                    var ids = purchases.Select(t => t.Id).ToList();
                    var uow = UnitOfWorkProvider.Value.CreateUnitOfWork();
                    var repository = uow.GetRepository<LegacyPastPaperPurchase>();
                    var existing = await repository.GetManyQueryable(t => ids.Contains(t.Id), tracking: true).ToDictionaryAsync(t => t.Id);
                    foreach (var purchase in purchases)
                    {
                        if (existing.TryGetValue(purchase.Id, out var current))
                        {
                            current.CoreStatus = purchase.CoreStatus;
                            current.Price = purchase.Price;
                            current.SyncDate = purchase.SyncDate;
                            result.Updated++;
                        }
                        else
                        {
                            repository.Add(purchase);
                            result.Added++;
                        }
                    }

                    _ = await uow.SaveChangesAsync();
                }

                var link = await LinkAsync();
                if (link.Data is not null)
                {
                    (result.Owned, result.AlreadyOwned, result.WithoutUser, result.WithoutPaper) =
                        (link.Data.Owned, link.Data.AlreadyOwned, link.Data.WithoutUser, link.Data.WithoutPaper);
                }

                Logger.Value.LogWarning("Past paper purchase copy: {Read} payments read, {Purchases} paper file purchases ({Added} added, {Updated} updated); "
                    + "{Owned} now owned, {AlreadyOwned} already owned, {WithoutUser} waiting for the buyer, {WithoutPaper} waiting for the paper",
                    result.PaymentsRead, result.Purchases, result.Added, result.Updated, result.Owned, result.AlreadyOwned, result.WithoutUser, result.WithoutPaper);
                return new(link.OperationResult) { Data = result, Errors = link.Errors };
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return new(OperationResult.Failed) { Errors = [new() { Message = exc.Message }] };
            }
        }

        public async Task<ResultData<PastPaperPurchaseImportResultDto>> LinkAsync()
        {
            try
            {
                PastPaperPurchaseImportResultDto result = new();
                var users = await CoreUserMapAsync();
                long afterId = 0;
                while (true)
                {
                    var uow = UnitOfWorkProvider.Value.CreateUnitOfWork();
                    var batch = await uow.GetRepository<LegacyPastPaperPurchase>()
                        .GetManyQueryable(t => t.ContentPurchaseId == null && t.Id > afterId, tracking: true)
                        .OrderBy(t => t.Id).Take(BatchSize).ToListAsync();
                    if (batch.Count == 0)
                    {
                        break;
                    }

                    afterId = batch[^1].Id;
                    await ResolvePapersAsync(uow, batch);

                    // The ownership rows these purchases map to, already there or made in this batch, by (user, paper, variant).
                    var keys = batch.Where(t => t.PastPaperId is not null && users.ContainsKey(t.CoreUserId))
                        .Select(t => (UserId: users[t.CoreUserId], PaperId: t.PastPaperId!.Value, Variant: t.Variant!)).Distinct().ToList();
                    var userIds = keys.Select(t => t.UserId).Distinct().ToList();
                    var paperIds = keys.Select(t => t.PaperId).Distinct().ToList();
                    var owned = (await uow.GetRepository<ContentPurchase>()
                        .GetManyQueryable(t => t.ContentType == PurchasableContentType.PastPaper && userIds.Contains(t.UserId) && paperIds.Contains(t.ContentId), tracking: true)
                        .ToListAsync())
                        .ToDictionary(t => (t.UserId, PaperId: t.ContentId, t.Variant));

                    foreach (var purchase in batch)
                    {
                        if (purchase.PastPaperId is null)
                        {
                            result.WithoutPaper++;
                            continue;
                        }

                        if (!users.TryGetValue(purchase.CoreUserId, out var userId))
                        {
                            result.WithoutUser++;
                            continue;
                        }

                        var key = (userId, purchase.PastPaperId.Value, purchase.Variant!);
                        if (owned.TryGetValue(key, out var ownership))
                        {
                            result.AlreadyOwned++;
                        }
                        else
                        {
                            ownership = new()
                            {
                                UserId = userId,
                                ContentType = PurchasableContentType.PastPaper,
                                ContentId = purchase.PastPaperId.Value,
                                Variant = purchase.Variant!,
                                Points = 0,
                                PaidBy = SpendSource.GamaApi,
                                CreationDate = purchase.PurchaseDate,
                            };
                            uow.GetRepository<ContentPurchase>().Add(ownership);
                            owned[key] = ownership;
                            result.Owned++;
                        }

                        purchase.ContentPurchase = ownership;
                    }

                    _ = await uow.SaveChangesAsync();
                }

                return new(OperationResult.Succeeded) { Data = result };
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return new(OperationResult.Failed) { Errors = [new() { Message = exc.Message }] };
            }
        }

        /// <summary>A paid past paper file from a <c>payments</c> row; null for anything else. Internal for the tests.</summary>
        internal static LegacyPastPaperPurchase? ToPurchase(IReadOnlyDictionary<string, object?> row, TimeZoneInfo timeZone, DateTimeOffset now)
        {
            var type = Long(row, "type");
            var status = Long(row, "status");
            return type is null || status is null || !PaperFileTypes.Contains(type.Value) || !PaidStatuses.Contains(status.Value)
                || Long(row, "id") is not long id || Long(row, "user_") is not long user || Long(row, "usage_id") is not long usage || usage <= 0
                ? null
                : new()
                {
                    Id = id,
                    CoreUserId = user,
                    CoreUsageId = usage,
                    CoreType = (byte)type.Value,
                    CoreStatus = (byte)status.Value,
                    Price = Long(row, "price") ?? 0,
                    PurchaseDate = Date(row, "subdate", timeZone) ?? now,
                    SyncDate = now,
                };
        }

        /// <summary>The <c>ContentPurchases</c> variant of a gama-api payment type. Internal for the tests.</summary>
        internal static string? Variant(byte coreType, long usageId) => coreType switch
        {
            QuestionType => PastPaperFileKind.Question.Name,
            AnswerType => PastPaperFileKind.Answer.Name,
            WordType => PastPaperFileKind.QuestionWord.Name,
            ExtraType => $"{PastPaperFileKind.Extra.Name}:{usageId.ToString(CultureInfo.InvariantCulture)}",
            _ => null,
        };

        // A paper file's purchase names the paper; an extra file's names the file, whose paper comes from PastPaperFiles.
        private static async Task ResolvePapersAsync(IUnitOfWork uow, List<LegacyPastPaperPurchase> batch)
        {
            var open = batch.Where(t => t.PastPaperId is null).ToList();
            if (open.Count == 0)
            {
                return;
            }

            var paperIds = open.Where(t => t.CoreType != ExtraType).Select(t => t.CoreUsageId).Distinct().ToList();
            var papers = (await uow.GetRepository<PastPaper>().GetManyQueryable(t => paperIds.Contains(t.Id)).Select(t => t.Id).ToListAsync()).ToHashSet();
            List<long?> extraIds = [.. open.Where(t => t.CoreType == ExtraType).Select(t => (long?)t.CoreUsageId).Distinct()];
            var extras = await uow.GetRepository<PastPaperFile>().GetManyQueryable(t => extraIds.Contains(t.CoreExtraFileId))
                .ToDictionaryAsync(t => t.CoreExtraFileId!.Value, t => t.PastPaperId);

            foreach (var purchase in open)
            {
                long? paperId = null;
                if (purchase.CoreType == ExtraType && extras.TryGetValue(purchase.CoreUsageId, out var extraPaper))
                {
                    paperId = extraPaper;
                }
                else if (purchase.CoreType != ExtraType && papers.Contains(purchase.CoreUsageId))
                {
                    paperId = purchase.CoreUsageId;
                }

                if (paperId is not null)
                {
                    purchase.PastPaperId = paperId;
                    purchase.Variant = Variant(purchase.CoreType, purchase.CoreUsageId);
                }
            }
        }

        private static long? Long(IReadOnlyDictionary<string, object?> row, string column) =>
            row.TryGetValue(column, out var value) && value is not null
                && long.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture), NumberStyles.Integer, CultureInfo.InvariantCulture, out var number)
                ? number
                : null;

        // gama-api's MySQL runs in UTC (Migration:LegacyDatabase:TimeZone overrides it).
        private static DateTimeOffset? Date(IReadOnlyDictionary<string, object?> row, string column, TimeZoneInfo timeZone) =>
            row.TryGetValue(column, out var value) && value is DateTime date && date != DateTime.MinValue
                ? new DateTimeOffset(DateTime.SpecifyKind(date, DateTimeKind.Unspecified), timeZone.GetUtcOffset(date))
                : null;

        private TimeZoneInfo LegacyTimeZone()
        {
            var id = configuration.Value["Migration:LegacyDatabase:TimeZone"];
            return !string.IsNullOrWhiteSpace(id) && TimeZoneInfo.TryFindSystemTimeZoneById(id, out var zone) ? zone : TimeZoneInfo.Utc;
        }

        /// <summary>gama-api user id → this backend's user id, for every user linked by <c>CoreId</c>.</summary>
        private async Task<Dictionary<long, long>> CoreUserMapAsync()
        {
            var uow = UnitOfWorkProvider.Value.CreateUnitOfWork();
            var pairs = await uow.GetRepository<ApplicationUser>().GetManyQueryable(t => t.CoreId != null)
                .Select(t => new { CoreId = t.CoreId!.Value, t.Id }).ToListAsync();

            // CoreId is not unique in the schema; if two users ever share one, the first account owns the purchases.
            return pairs.GroupBy(t => t.CoreId).ToDictionary(t => t.Key, t => t.Min(u => u.Id));
        }
    }
}
