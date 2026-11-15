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
    /// Reads gama-api's paper tables in id order, a batch at a time, and saves each batch in its own unit of work, so memory
    /// stays flat and a failure loses one batch at most (running again picks up everything; copies are updated, never
    /// duplicated). gama-api is only read.
    /// </summary>
    public sealed class PastPaperImportService(Lazy<IUnitOfWorkProvider> unitOfWorkProvider, Lazy<IHttpContextAccessor> httpContextAccessor,
        Lazy<ILogger<PastPaperImportService>> logger, Lazy<ILegacyDatabaseReader> reader, Lazy<IConfiguration> configuration)
        : ServiceBase<PastPaperImportService>(unitOfWorkProvider, httpContextAccessor, logger), IPastPaperImportService
    {
        private const int BatchSize = 1000;
        private const string PapersTable = "tests";
        private const string ExtraFilesTable = "tests_extra_files";

        // gama-api writes these into a file column when a paper has no file.
        private static readonly HashSet<string> NoFile = new(["", "0", "fileMissed"], StringComparer.OrdinalIgnoreCase);

        private static readonly Dictionary<byte, PastPaperStatus> Statuses =
            EnumerationExtensions.GetAll<PastPaperStatus, byte>()!.ToDictionary(t => t!.Value, t => t!);

        public async Task<ResultData<PastPaperImportResultDto>> ImportAsync()
        {
            try
            {
                if (!reader.Value.IsConfigured)
                {
                    return new(OperationResult.NotValid) { Errors = [new() { Message = "gama-api's database is not configured (Migration:LegacyDatabase:ConnectionString)." }] };
                }

                PastPaperImportResultDto result = new();
                var users = await CoreUserMapAsync();
                var timeZone = LegacyTimeZone();

                var papers = await ImportPapersAsync(result, users, timeZone);
                if (papers.OperationResult is not OperationResult.Succeeded)
                {
                    return new(papers.OperationResult) { Data = result, Errors = papers.Errors };
                }

                var extras = await ImportExtraFilesAsync(result, timeZone);
                if (extras.OperationResult is not OperationResult.Succeeded)
                {
                    return new(extras.OperationResult) { Data = result, Errors = extras.Errors };
                }

                Logger.Value.LogWarning("Past paper copy: {Read} papers read, {Added} added, {Updated} updated, {WithoutUser} without a linked user; "
                    + "{ExtraRead} extra files read, {ExtraOrphans} without a paper; files {FilesAdded} added, {FilesUpdated} updated, {FilesRemoved} removed",
                    result.PapersRead, result.PapersAdded, result.PapersUpdated, result.PapersWithoutUser,
                    result.ExtraFilesRead, result.ExtraFilesWithoutPaper, result.FilesAdded, result.FilesUpdated, result.FilesRemoved);
                return new(OperationResult.Succeeded) { Data = result };
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return new(OperationResult.Failed) { Errors = [new() { Message = exc.Message }] };
            }
        }

        /// <summary>A paper from a <c>tests</c> row. Internal for the tests.</summary>
        internal static PastPaper ToPaper(IReadOnlyDictionary<string, object?> row, IReadOnlyDictionary<long, long> users, TimeZoneInfo timeZone, DateTimeOffset now)
        {
            var coreOwnerId = Long(row, "user_") ?? 0;
            return new()
            {
                Id = Long(row, "id") ?? throw new InvalidOperationException("A gama-api paper without an id."),
                CoreOwnerId = coreOwnerId,
                OwnerUserId = users.TryGetValue(coreOwnerId, out var userId) ? userId : null,
                CoreTeacherId = Positive(Long(row, "teacher_")),
                Title = Cut(Text(row, "title"), 500) ?? string.Empty,
                Description = Empty(Text(row, "description")),
                Status = Statuses.TryGetValue((byte)(Long(row, "status") ?? 0), out var status) ? status : PastPaperStatus.Pending,
                ReviewNote = Empty(Text(row, "msg")),
                IsDeleted = Long(row, "bit_delete") is > 0,
                CoreBoardId = (int)(Long(row, "section") ?? 0),
                CoreGradeId = (int)(Long(row, "base") ?? 0),
                CoreSubjectId = (int)(Long(row, "lesson") ?? 0),
                CoreCourseId = (int?)Positive(Long(row, "course")),
                CoreTopicIds = Cut(Empty(Text(row, "topic")), 200),
                CorePaperTypeId = (int?)Positive(Long(row, "test_type")),
                Variant = (short?)Long(row, "variant"),
                Code = Cut(Empty(Text(row, "code")), 10),
                Level = (byte?)Long(row, "level"),
                AnswerType = (byte?)Long(row, "answer_type"),
                HoldingLevel = (short?)Long(row, "holding_level"),
                State = Cut(Empty(Text(row, "state")), 50),
                CoreAreaId = (int?)Positive(Long(row, "area")),
                CoreSchoolId = Positive(Long(row, "school")),
                EduYear = (short?)Positive(Long(row, "edu_year")),
                EduMonth = (byte?)Positive(Long(row, "edu_month")),
                EduSession = (byte?)Positive(Long(row, "edu_session")),
                CoreExamIds = Cut(Empty(Text(row, "exam_id")), 300),
                IsPaper = Long(row, "is_paper") is > 0,
                FreeAgreement = Long(row, "free_aggrement") is > 0,
                PriceMultiplier = Decimal(row, "price_multiplier") ?? 1,
                RefereeScore = (byte)(Long(row, "ref_score") ?? 0),
                CoreReferee = Cut(Empty(Text(row, "referee")), 10),
                Score = (int)(Long(row, "score") ?? 0),
                TeacherScore = (byte)(Long(row, "teacher_score") ?? 0),
                Thumbnail = Cut(Empty(Text(row, "thumb_pic")), 100),
                ViewCount = (int)(Long(row, "views") ?? 0),
                DownloadCount = (int)(Long(row, "downloads") ?? 0),
                PaidCount = (int)(Long(row, "payed_num") ?? 0),
                CreationDate = Date(row, "subdate", timeZone) ?? now,
                LastModifyDate = Date(row, "up_date", timeZone),
                SyncDate = now,
            };
        }

        /// <summary>The paper's own files (question, answer, Word) from a <c>tests</c> row. Internal for the tests.</summary>
        internal static List<PastPaperFile> MainFiles(IReadOnlyDictionary<string, object?> row, long paperId)
        {
            List<PastPaperFile> files = [];
            void Add(PastPaperFileKind kind, string pathColumn, string? extensionColumn, string? pagesColumn)
            {
                var path = FilePath(Text(row, pathColumn));
                if (path is null)
                {
                    return;
                }

                files.Add(new()
                {
                    PastPaperId = paperId,
                    Kind = kind,
                    Path = path,
                    Extension = Extension(extensionColumn is null ? null : Text(row, extensionColumn), path),
                    PageCount = pagesColumn is null ? null : (short?)Positive(Long(row, pagesColumn)),
                });
            }

            Add(PastPaperFileKind.Question, "q_file", "q_file_ext", "q_file_pages");
            Add(PastPaperFileKind.Answer, "a_file", "a_file_ext", "a_file_pages");
            Add(PastPaperFileKind.QuestionWord, "q_file_word", null, null);
            return files;
        }

        /// <summary>An extra file from a <c>tests_extra_files</c> row; null when it has no file. Internal for the tests.</summary>
        internal static PastPaperFile? ToExtraFile(IReadOnlyDictionary<string, object?> row, TimeZoneInfo timeZone)
        {
            var path = FilePath(Text(row, "file"));
            return path is null
                ? null
                : new()
                {
                    PastPaperId = Long(row, "test_id") ?? 0,
                    Kind = PastPaperFileKind.Extra,
                    Path = path,
                    Extension = Extension(null, path),
                    Length = (int?)Positive(Long(row, "length")),
                    CoreTypeId = (int?)Positive(Long(row, "type")),
                    CoreExtraFileId = Long(row, "id"),
                    CreationDate = Date(row, "subdate", timeZone),
                };
        }

        private async Task<ResultData<bool>> ImportPapersAsync(PastPaperImportResultDto result, Dictionary<long, long> users, TimeZoneInfo timeZone)
        {
            long afterId = 0;
            while (true)
            {
                var batch = await reader.Value.ReadBatchAsync(PapersTable, afterId, BatchSize);
                if (batch.OperationResult is not OperationResult.Succeeded || batch.Data is null)
                {
                    return new(batch.OperationResult) { Errors = batch.Errors };
                }

                if (batch.Data.Count == 0)
                {
                    return new(OperationResult.Succeeded) { Data = true };
                }

                var now = DateTimeOffset.UtcNow;
                var rows = batch.Data.Select(t => (Paper: ToPaper(t, users, timeZone, now), Row: t)).ToList();
                var ids = rows.Select(t => t.Paper.Id).ToList();

                var uow = UnitOfWorkProvider.Value.CreateUnitOfWork();
                var repository = uow.GetRepository<PastPaper>();
                var existing = await repository
                    .GetManyQueryable(t => ids.Contains(t.Id), includes: t => t.Include(p => p.Files), tracking: true)
                    .ToDictionaryAsync(t => t.Id);

                foreach (var (paper, row) in rows)
                {
                    var files = MainFiles(row, paper.Id);
                    if (existing.TryGetValue(paper.Id, out var current))
                    {
                        CopyInto(paper, current);
                        SyncMainFiles(current, files, result);
                        result.PapersUpdated++;
                    }
                    else
                    {
                        paper.Files = files;
                        repository.Add(paper);
                        result.PapersAdded++;
                        result.FilesAdded += files.Count;
                    }

                    if (paper.OwnerUserId is null)
                    {
                        result.PapersWithoutUser++;
                    }
                }

                _ = await uow.SaveChangesAsync();
                result.PapersRead += rows.Count;
                afterId = ids.Max();
            }
        }

        private async Task<ResultData<bool>> ImportExtraFilesAsync(PastPaperImportResultDto result, TimeZoneInfo timeZone)
        {
            HashSet<long> seen = [];
            long afterId = 0;
            while (true)
            {
                var batch = await reader.Value.ReadBatchAsync(ExtraFilesTable, afterId, BatchSize);
                if (batch.OperationResult is not OperationResult.Succeeded || batch.Data is null)
                {
                    return new(batch.OperationResult) { Errors = batch.Errors };
                }

                if (batch.Data.Count == 0)
                {
                    break;
                }

                afterId = batch.Data.Max(t => Long(t, "id") ?? 0);
                result.ExtraFilesRead += batch.Data.Count;
                var files = batch.Data.Select(t => ToExtraFile(t, timeZone)).OfType<PastPaperFile>().ToList();
                foreach (var file in files)
                {
                    _ = seen.Add(file.CoreExtraFileId!.Value);
                }

                var paperIds = files.Select(t => t.PastPaperId).Distinct().ToList();
                var extraIds = files.Select(t => t.CoreExtraFileId).ToList();
                var uow = UnitOfWorkProvider.Value.CreateUnitOfWork();
                var knownPapers = (await uow.GetRepository<PastPaper>().GetManyQueryable(t => paperIds.Contains(t.Id)).Select(t => t.Id).ToListAsync()).ToHashSet();
                var repository = uow.GetRepository<PastPaperFile>();
                var existing = await repository.GetManyQueryable(t => extraIds.Contains(t.CoreExtraFileId), tracking: true)
                    .ToDictionaryAsync(t => t.CoreExtraFileId!.Value);

                foreach (var file in files)
                {
                    if (!knownPapers.Contains(file.PastPaperId))
                    {
                        result.ExtraFilesWithoutPaper++;
                        continue;
                    }

                    if (!existing.TryGetValue(file.CoreExtraFileId!.Value, out var current))
                    {
                        repository.Add(file);
                        result.FilesAdded++;
                    }
                    else if (CopyFileInto(file, current))
                    {
                        result.FilesUpdated++;
                    }
                }

                _ = await uow.SaveChangesAsync();
            }

            // An extra file the owner removed in gama-api is gone from its table: remove its copy too.
            var removeUow = UnitOfWorkProvider.Value.CreateUnitOfWork();
            var fileRepository = removeUow.GetRepository<PastPaperFile>();
            var stale = (await fileRepository.GetManyQueryable(t => t.CoreExtraFileId != null, tracking: true).ToListAsync())
                .Where(t => !seen.Contains(t.CoreExtraFileId!.Value)).ToList();
            foreach (var file in stale)
            {
                fileRepository.Remove(file);
            }

            result.FilesRemoved += stale.Count;
            _ = await removeUow.SaveChangesAsync();
            return new(OperationResult.Succeeded) { Data = true };
        }

        private static void SyncMainFiles(PastPaper paper, List<PastPaperFile> files, PastPaperImportResultDto result)
        {
            paper.Files ??= [];
            foreach (var kind in new[] { PastPaperFileKind.Question, PastPaperFileKind.Answer, PastPaperFileKind.QuestionWord })
            {
                var wanted = files.Find(t => t.Kind == kind);
                var current = paper.Files.FirstOrDefault(t => t.Kind == kind);
                if (wanted is null && current is not null)
                {
                    _ = paper.Files.Remove(current);
                    result.FilesRemoved++;
                }
                else if (wanted is not null && current is null)
                {
                    paper.Files.Add(wanted);
                    result.FilesAdded++;
                }
                else if (wanted is not null && current is not null && CopyFileInto(wanted, current))
                {
                    result.FilesUpdated++;
                }
            }
        }

        private static bool CopyFileInto(PastPaperFile from, PastPaperFile to)
        {
            if (string.Equals(to.Path, from.Path, StringComparison.Ordinal) && string.Equals(to.Extension, from.Extension, StringComparison.Ordinal)
                && to.PageCount == from.PageCount && to.Length == from.Length && to.CoreTypeId == from.CoreTypeId
                && to.PastPaperId == from.PastPaperId && to.CreationDate == from.CreationDate)
            {
                return false;
            }

            to.PastPaperId = from.PastPaperId;
            to.Path = from.Path;
            to.Extension = from.Extension;
            to.PageCount = from.PageCount;
            to.Length = from.Length;
            to.CoreTypeId = from.CoreTypeId;
            to.CreationDate = from.CreationDate;
            return true;
        }

        private static void CopyInto(PastPaper from, PastPaper to)
        {
            to.CoreOwnerId = from.CoreOwnerId;
            to.OwnerUserId = from.OwnerUserId ?? to.OwnerUserId;
            to.CoreTeacherId = from.CoreTeacherId;
            to.Title = from.Title;
            to.Description = from.Description;
            to.Status = from.Status;
            to.ReviewNote = from.ReviewNote;
            to.IsDeleted = from.IsDeleted;
            to.CoreBoardId = from.CoreBoardId;
            to.CoreGradeId = from.CoreGradeId;
            to.CoreSubjectId = from.CoreSubjectId;
            to.CoreCourseId = from.CoreCourseId;
            to.CoreTopicIds = from.CoreTopicIds;
            to.CorePaperTypeId = from.CorePaperTypeId;
            to.Variant = from.Variant;
            to.Code = from.Code;
            to.Level = from.Level;
            to.AnswerType = from.AnswerType;
            to.HoldingLevel = from.HoldingLevel;
            to.State = from.State;
            to.CoreAreaId = from.CoreAreaId;
            to.CoreSchoolId = from.CoreSchoolId;
            to.EduYear = from.EduYear;
            to.EduMonth = from.EduMonth;
            to.EduSession = from.EduSession;
            to.CoreExamIds = from.CoreExamIds;
            to.IsPaper = from.IsPaper;
            to.FreeAgreement = from.FreeAgreement;
            to.PriceMultiplier = from.PriceMultiplier;
            to.RefereeScore = from.RefereeScore;
            to.CoreReferee = from.CoreReferee;
            to.Score = from.Score;
            to.TeacherScore = from.TeacherScore;
            to.Thumbnail = from.Thumbnail;
            to.ViewCount = from.ViewCount;
            to.DownloadCount = from.DownloadCount;
            to.PaidCount = from.PaidCount;
            to.CreationDate = from.CreationDate;
            to.LastModifyDate = from.LastModifyDate;
            to.SyncDate = from.SyncDate;
        }

        private static string? FilePath(string? value)
        {
            var path = value?.Trim().TrimStart('/');
            return string.IsNullOrEmpty(path) || NoFile.Contains(path) ? null : Cut(path, 200);
        }

        private static string? Extension(string? recorded, string path)
        {
            var extension = Empty(recorded)?.TrimStart('.') ?? System.IO.Path.GetExtension(path).TrimStart('.');
            return string.IsNullOrEmpty(extension) ? null : Cut(extension.ToLowerInvariant(), 10);
        }

        private static long? Long(IReadOnlyDictionary<string, object?> row, string column) =>
            row.TryGetValue(column, out var value) && value is not null
                && long.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture), NumberStyles.Integer, CultureInfo.InvariantCulture, out var number)
                ? number
                : null;

        private static decimal? Decimal(IReadOnlyDictionary<string, object?> row, string column) =>
            row.TryGetValue(column, out var value) && value is not null
                && decimal.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture), NumberStyles.Float, CultureInfo.InvariantCulture, out var number)
                ? Math.Round(number, 4)
                : null;

        private static long? Positive(long? value) => value is > 0 ? value : null;

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

        /// <summary>gama-api user id → this backend's user id, for every user linked by <c>CoreId</c>.</summary>
        private async Task<Dictionary<long, long>> CoreUserMapAsync()
        {
            var uow = UnitOfWorkProvider.Value.CreateUnitOfWork();
            var pairs = await uow.GetRepository<ApplicationUser>().GetManyQueryable(t => t.CoreId != null)
                .Select(t => new { CoreId = t.CoreId!.Value, t.Id }).ToListAsync();

            // CoreId is not unique in the schema; if two users ever share one, the first account keeps the papers.
            return pairs.GroupBy(t => t.CoreId).ToDictionary(t => t.Key, t => t.Min(u => u.Id));
        }
    }
}
