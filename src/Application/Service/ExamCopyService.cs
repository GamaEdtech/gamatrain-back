namespace GamaEdtech.Application.Service
{
    using System.Globalization;
    using System.Linq;

    using GamaEdtech.Application.Interface;
    using GamaEdtech.Common.Caching;
    using GamaEdtech.Common.Core;
    using GamaEdtech.Common.Data;
    using GamaEdtech.Common.DataAccess.Entities;
    using GamaEdtech.Common.DataAccess.UnitOfWork;
    using GamaEdtech.Common.Service;
    using GamaEdtech.Data.Dto.ExamCopy;
    using GamaEdtech.Domain.Entity;
    using GamaEdtech.Domain.Entity.Identity;
    using GamaEdtech.Domain.Enumeration;
    using GamaEdtech.Infrastructure.Interface;

    using Microsoft.AspNetCore.Http;
    using Microsoft.EntityFrameworkCore;
    using Microsoft.Extensions.Configuration;
    using Microsoft.Extensions.Logging;

    using static GamaEdtech.Common.Core.Constants;

    public sealed class ExamCopyService(Lazy<IUnitOfWorkProvider> unitOfWorkProvider, Lazy<IHttpContextAccessor> httpContextAccessor,
        Lazy<ILogger<ExamCopyService>> logger, Lazy<ILegacyDatabaseReader> reader, Lazy<IConfiguration> configuration,
        Lazy<IMigrationRouter> migrationRouter, Lazy<ICacheProvider> cacheProvider)
        : ServiceBase<ExamCopyService>(unitOfWorkProvider, httpContextAccessor, logger), IExamCopyService
    {
        private const int StampBatchSize = 20_000;
        private const int RowBatchSize = 500;
        private const string LockKey = "ExamCopy:lock";

        // Only the columns copied; internal flags (elastic_sync, check_bit, files_move...) stay on gama-api.
        private static readonly string[] ExamColumns =
        [
            "id", "clone_id", "user_", "referee", "user_type", "section", "base", "course", "lesson", "topics", "type", "azmoon_type",
            "holding_level", "state", "area", "school", "level", "title", "tests", "tests_num", "tests_combination", "start_date",
            "end_date", "azmoon_time", "score_type", "code", "edu_year", "edu_month", "file_original", "file_pdf", "page_break_points",
            "thumb_pic", "participants_num", "status", "msg", "review", "metadata", "views", "downloads", "bit_delete", "subdate", "up_date",
        ];

        private static readonly string[] QuestionColumns =
        [
            "id", "user_", "code", "code_order", "parent", "section", "base", "course", "lesson", "direction", "topics", "level", "type",
            "enhanced_title", "question", "q_file", "answer_a", "answer_b", "answer_c", "answer_d", "answer_full", "answer_view_type",
            "a_file", "b_file", "c_file", "d_file", "answer_full_file", "true_answer", "resource", "used_confirm", "used_publish",
            "answers_stat", "status", "msg", "teacher_score", "subdate", "up_date",
        ];

        private static readonly string[] AttemptColumns =
            ["id", "user_", "azmoon", "azmoon_code", "submit_time", "result_score", "status", "state", "area", "school", "transfer", "subdate"];

        private static readonly string[] AnswerSheetColumns = ["id", "user_", "azmoon", "start_id", "tests", "subdate"];

        public async Task<ResultData<ExamCopyResultDto>> CopyAsync()
        {
            try
            {
                if (!reader.Value.IsConfigured)
                {
                    return new(OperationResult.NotValid) { Errors = [new() { Message = "gama-api's database is not configured (Migration:LegacyDatabase:ConnectionString)." }] };
                }

                // Once exams are written here, the copy is over: it would put gama-api's older rows back over the changes.
                if (migrationRouter.Value.GetWriteRoute(MigrationDomain.Exams) == MigrationRoute.Native)
                {
                    return new(OperationResult.NotValid) { Errors = [new() { Message = "Exams are written here (Migration:WriteRoutes:Exams is Native); the copy from gama-api no longer runs." }] };
                }

                // Every 15 minutes: a run still going (a large first copy) is never doubled.
                await using var held = await cacheProvider.Value.LockAsync(LockKey, TimeSpan.FromHours(1), TimeSpan.Zero);
                if (held is null)
                {
                    return new(OperationResult.NotValid) { Errors = [new() { Message = "An exam copy is already running." }] };
                }

                ExamCopyResultDto result = new();
                var users = await CoreUserMapAsync();
                var timeZone = LegacyTimeZone();
                var now = DateTimeOffset.UtcNow;

                // A test server copies a sample: the newest MaxExams exams, the questions they use, and their attempts. Production
                // leaves it unset and copies everything.
                var maxExams = configuration.Value.GetValue<int?>("Migration:LegacyDatabase:MaxExams") is int max and > 0 ? max : (int?)null;
                var exams = await MirrorChangedAsync("azmoons", ExamColumns, result.Exams, row => ToExam(row, users, timeZone, now), CopyInto,
                    ids => maxExams is int n ? [.. ids.OrderByDescending(t => t).Take(n)] : ids);
                if (exams.Errors is not null)
                {
                    return new(OperationResult.Failed) { Data = result, Errors = exams.Errors };
                }

                HashSet<long> examIds = [];
                HashSet<long> questionIds = [];
                if (maxExams is not null)
                {
                    var uow = UnitOfWorkProvider.Value.CreateUnitOfWork();
                    foreach (var exam in await uow.GetRepository<Exam>().GetManyQueryable(t => true).Select(t => new { t.Id, t.QuestionIds }).ToListAsync())
                    {
                        _ = examIds.Add(exam.Id);
                        questionIds.UnionWith(QuestionIdsOf(exam.QuestionIds));
                    }
                }

                var questions = await MirrorChangedAsync("azmoon_tests", QuestionColumns, result.Questions, row => ToQuestion(row, users, timeZone, now), CopyInto,
                    ids => maxExams is null ? ids : [.. ids.Where(questionIds.Contains)]);
                if (questions.Errors is not null)
                {
                    return new(OperationResult.Failed) { Data = result, Errors = questions.Errors };
                }

                // Once results are written here (WriteRoutes:Exams.Games Native), the attempts and answer sheets are ours: never
                // copied over or removed again.
                if (migrationRouter.Value.GetWriteRoute(MigrationDomain.Exams, MigrationFeatures.Games) == MigrationRoute.Native)
                {
                    Logger.Value.LogWarning("Exam copy: exams {Exams}; questions {Questions}; results are written here, not copied", result.Exams, result.Questions);
                    return new(OperationResult.Succeeded) { Data = result };
                }

                bool KeepExam(long id) => maxExams is null || examIds.Contains(id);
                var attempts = await MirrorAllAsync("azmoon_data", AttemptColumns, result.Attempts, row => ToAttempt(row, users, timeZone, now), CopyInto, KeepExam);
                if (attempts.Errors is not null)
                {
                    return new(OperationResult.Failed) { Data = result, Errors = attempts.Errors };
                }

                var sheets = await MirrorAllAsync("azmoon_results", AnswerSheetColumns, result.AnswerSheets, row => ToAnswerSheet(row, users, timeZone, now), CopyInto, KeepExam);
                if (sheets.Errors is not null)
                {
                    return new(OperationResult.Failed) { Data = result, Errors = sheets.Errors };
                }

                Logger.Value.LogWarning("Exam copy: exams {Exams}; questions {Questions}; attempts {Attempts}; answer sheets {Sheets}",
                    result.Exams, result.Questions, result.Attempts, result.AnswerSheets);
                return new(OperationResult.Succeeded) { Data = result };
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return new(OperationResult.Failed) { Errors = [new() { Message = exc.Message }] };
            }
        }

        /// <summary>The question ids of an exam's comma-separated <c>tests</c>, in order. Internal for the tests and readers.</summary>
        internal static IEnumerable<long> QuestionIdsOf(string? questionIds) =>
            (questionIds ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(t => long.TryParse(t, NumberStyles.None, CultureInfo.InvariantCulture, out var id) ? id : 0).Where(t => t > 0);

        /// <summary>An exam from an <c>azmoons</c> row; null without an id. Internal for the tests.</summary>
        internal static Exam? ToExam(IReadOnlyDictionary<string, object?> row, IReadOnlyDictionary<long, long> users, TimeZoneInfo timeZone, DateTimeOffset now)
        {
            if (Long(row, "id") is not long id)
            {
                return null;
            }

            var owner = Long(row, "user_") ?? 0;
            var created = Date(row, "subdate", timeZone) ?? now;
            return new()
            {
                Id = id,
                CloneId = Long(row, "clone_id") is long clone and > 0 ? clone : null,
                CoreOwnerId = owner,
                OwnerUserId = users.TryGetValue(owner, out var user) ? user : null,
                CoreReferee = Cut(Empty(Text(row, "referee")), 10),
                UserType = Byte(row, "user_type"),
                CoreBoardId = Id(row, "section"),
                CoreGradeId = Id(row, "base"),
                CoreCourseId = Id(row, "course"),
                CoreSubjectId = Id(row, "lesson"),
                CoreTopicIds = Cut(Empty(Text(row, "topics")), 200),
                Type = Byte(row, "type") ?? 0,
                CoreExamTypeId = Id(row, "azmoon_type"),
                HoldingLevel = Byte(row, "holding_level"),
                State = Cut(Empty(Text(row, "state")), 50),
                Area = Id(row, "area"),
                School = Id(row, "school"),
                Level = Byte(row, "level"),
                Title = Cut(Text(row, "title")?.Trim() ?? string.Empty, 500),
                QuestionIds = Cut(Empty(Text(row, "tests")), 5000),
                QuestionCount = (int)Math.Clamp(Long(row, "tests_num") ?? 0, 0, int.MaxValue),
                QuestionsCombination = Byte(row, "tests_combination"),
                StartDate = Date(row, "start_date", timeZone),
                EndDate = Date(row, "end_date", timeZone),
                DurationMinutes = (int?)Long(row, "azmoon_time"),
                ScoreType = Byte(row, "score_type"),
                Code = Cut(Empty(Text(row, "code")), 10),
                EduYear = (int?)Long(row, "edu_year") is int year and not 0 ? year : null,
                EduMonth = Byte(row, "edu_month") is byte month and > 0 ? month : null,
                FileOriginal = Cut(Empty(Text(row, "file_original")), 100),
                FilePdf = Cut(Empty(Text(row, "file_pdf")), 100),
                PageBreakPoints = Cut(Empty(Text(row, "page_break_points")), 50),
                Thumbnail = Cut(Empty(Text(row, "thumb_pic")), 200),
                ParticipantCount = (int)Math.Clamp(Long(row, "participants_num") ?? 0, 0, int.MaxValue),
                Status = Byte(row, "status") ?? 0,
                Message = Cut(Empty(Text(row, "msg")), 100),
                Review = Byte(row, "review"),
                Metadata = Cut(Empty(Text(row, "metadata")), 1000),
                ViewCount = (int)Math.Clamp(Long(row, "views") ?? 0, 0, int.MaxValue),
                DownloadCount = (int)Math.Clamp(Long(row, "downloads") ?? 0, 0, int.MaxValue),
                IsDeleted = (Long(row, "bit_delete") ?? 0) != 0,
                CreationDate = created,
                LastModifyDate = Date(row, "up_date", timeZone) ?? created,
                SyncDate = now,
            };
        }

        /// <summary>A question from an <c>azmoon_tests</c> row; null without an id. Internal for the tests.</summary>
        internal static ExamQuestion? ToQuestion(IReadOnlyDictionary<string, object?> row, IReadOnlyDictionary<long, long> users, TimeZoneInfo timeZone, DateTimeOffset now)
        {
            if (Long(row, "id") is not long id)
            {
                return null;
            }

            var owner = Long(row, "user_") ?? 0;
            var created = Date(row, "subdate", timeZone) ?? now;
            return new()
            {
                Id = id,
                CoreOwnerId = owner,
                OwnerUserId = users.TryGetValue(owner, out var user) ? user : null,
                Code = Cut(Empty(Text(row, "code")), 10),
                CodeOrder = Cut(Empty(Text(row, "code_order")), 10),
                Parent = Cut(Empty(Text(row, "parent")), 10),
                CoreBoardId = Id(row, "section"),
                CoreGradeId = Id(row, "base"),
                CoreCourseId = Id(row, "course"),
                CoreSubjectId = Id(row, "lesson"),
                Direction = Cut(Empty(Text(row, "direction")), 5),
                CoreTopicIds = Cut(Empty(Text(row, "topics")), 100),
                Level = Byte(row, "level"),
                Type = Byte(row, "type"),
                EnhancedTitle = Cut(Empty(Text(row, "enhanced_title")), 500),
                Question = Text(row, "question"),
                QuestionFile = Cut(Empty(Text(row, "q_file")), 100),
                AnswerA = Text(row, "answer_a"),
                AnswerB = Text(row, "answer_b"),
                AnswerC = Text(row, "answer_c"),
                AnswerD = Text(row, "answer_d"),
                AnswerFull = Text(row, "answer_full"),
                AnswerViewType = Byte(row, "answer_view_type"),
                FileA = Cut(Empty(Text(row, "a_file")), 200),
                FileB = Cut(Empty(Text(row, "b_file")), 200),
                FileC = Cut(Empty(Text(row, "c_file")), 200),
                FileD = Cut(Empty(Text(row, "d_file")), 200),
                AnswerFullFile = Cut(Empty(Text(row, "answer_full_file")), 200),
                TrueAnswer = Byte(row, "true_answer"),
                Resource = Cut(Empty(Text(row, "resource")), 200),
                UsedConfirm = (int)Math.Clamp(Long(row, "used_confirm") ?? 0, int.MinValue, int.MaxValue),
                UsedPublish = (int)Math.Clamp(Long(row, "used_publish") ?? 0, int.MinValue, int.MaxValue),
                AnswersStat = Cut(Empty(Text(row, "answers_stat")), 50),
                Status = Byte(row, "status") ?? 0,
                Message = Cut(Empty(Text(row, "msg")), 100),
                TeacherScore = Byte(row, "teacher_score"),
                CreationDate = created,
                LastModifyDate = Date(row, "up_date", timeZone) ?? created,
                SyncDate = now,
            };
        }

        /// <summary>An attempt from an <c>azmoon_data</c> row; null without an id or exam. Internal for the tests.</summary>
        internal static ExamAttempt? ToAttempt(IReadOnlyDictionary<string, object?> row, IReadOnlyDictionary<long, long> users, TimeZoneInfo timeZone, DateTimeOffset now)
        {
            if (Long(row, "id") is not long id || Long(row, "azmoon") is not long exam)
            {
                return null;
            }

            var user = Long(row, "user_") ?? 0;
            return new()
            {
                Id = id,
                CoreUserId = user,
                UserId = users.TryGetValue(user, out var linked) ? linked : null,
                ExamId = exam,
                ExamCode = Cut(Empty(Text(row, "azmoon_code")), 10),
                SubmitSeconds = (int?)Long(row, "submit_time"),
                ResultScore = Cut(Empty(Text(row, "result_score")), 4),
                Status = Byte(row, "status") ?? 0,
                State = Byte(row, "state"),
                Area = Id(row, "area"),
                School = Id(row, "school"),
                Transfer = (Long(row, "transfer") ?? 0) != 0,
                CreationDate = Date(row, "subdate", timeZone) ?? now,
                SyncDate = now,
            };
        }

        /// <summary>An answer sheet from an <c>azmoon_results</c> row; null without an id or exam. Internal for the tests.</summary>
        internal static ExamAnswerSheet? ToAnswerSheet(IReadOnlyDictionary<string, object?> row, IReadOnlyDictionary<long, long> users, TimeZoneInfo timeZone, DateTimeOffset now)
        {
            if (Long(row, "id") is not long id || Long(row, "azmoon") is not long exam)
            {
                return null;
            }

            var user = Long(row, "user_") ?? 0;
            return new()
            {
                Id = id,
                CoreUserId = user,
                UserId = users.TryGetValue(user, out var linked) ? linked : null,
                ExamId = exam,
                AttemptId = Long(row, "start_id") is long start and > 0 ? start : null,
                Answers = Cut(Empty(Text(row, "tests")), 4000),
                CreationDate = Date(row, "subdate", timeZone) ?? now,
                SyncDate = now,
            };
        }

        /// <summary>A table with an <c>up_date</c>: reads every row's id and change date, then only the new or changed rows by id;
        /// removes the rows gama-api no longer has.</summary>
        private async Task<ResultData<bool>> MirrorChangedAsync<T>(string table, string[] columns, ExamCopyTableResultDto result,
            Func<IReadOnlyDictionary<string, object?>, T?> map, Func<T, T, bool> copyInto, Func<IReadOnlyCollection<long>, IReadOnlyCollection<long>> keep)
            where T : class, IEntity<T, long>, IExamCopyStampedRow
        {
            Dictionary<long, (DateTimeOffset? Updated, DateTimeOffset? Created)> stamps = [];
            var timeZone = LegacyTimeZone();
            long afterId = 0;
            while (true)
            {
                var batch = await reader.Value.ReadBatchAsync(table, ["id", "subdate", "up_date"], afterId, StampBatchSize);
                if (batch.Data is null)
                {
                    return new(batch.OperationResult) { Errors = batch.Errors ?? [new() { Message = $"Reading gama-api's {table} failed." }] };
                }

                if (batch.Data.Count == 0)
                {
                    break;
                }

                foreach (var row in batch.Data)
                {
                    if (Long(row, "id") is long id)
                    {
                        stamps[id] = (Date(row, "up_date", timeZone), Date(row, "subdate", timeZone));
                    }
                }

                afterId = batch.Data.Max(t => Long(t, "id") ?? 0);
            }

            HashSet<long> kept = [.. keep(stamps.Keys)];
            stamps = stamps.Where(t => kept.Contains(t.Key)).ToDictionary(t => t.Key, t => t.Value);
            result.Total = stamps.Count;
            var uow = UnitOfWorkProvider.Value.CreateUnitOfWork();
            var ours = await uow.GetRepository<T, long>().GetManyQueryable(t => true).Select(t => new { t.Id, t.LastModifyDate }).ToDictionaryAsync(t => t.Id, t => t.LastModifyDate);
            List<long> changed = [.. stamps.Where(t => !ours.TryGetValue(t.Key, out var stamp) || stamp != (t.Value.Updated ?? t.Value.Created)).Select(t => t.Key)];
            foreach (var chunk in changed.Chunk(RowBatchSize))
            {
                var rows = await reader.Value.ReadByIdsAsync(table, columns, chunk);
                if (rows.Data is null)
                {
                    return new(rows.OperationResult) { Errors = rows.Errors ?? [new() { Message = $"Reading gama-api's {table} failed." }] };
                }

                await UpsertAsync([.. rows.Data.Select(map).OfType<T>()], result, copyInto);
            }

            await RemoveAsync<T>([.. ours.Keys.Where(t => !stamps.ContainsKey(t))], result);
            return new(OperationResult.Succeeded) { Data = true };
        }

        /// <summary>A small table without a change date: reads it all and writes what differs; removes the rows gama-api no longer has.</summary>
        private async Task<ResultData<bool>> MirrorAllAsync<T>(string table, string[] columns, ExamCopyTableResultDto result,
            Func<IReadOnlyDictionary<string, object?>, T?> map, Func<T, T, bool> copyInto, Func<long, bool> keepExam)
            where T : class, IEntity<T, long>, IExamCopyRow
        {
            List<T> all = [];
            long afterId = 0;
            while (true)
            {
                var batch = await reader.Value.ReadBatchAsync(table, columns, afterId, RowBatchSize * 4);
                if (batch.Data is null)
                {
                    return new(batch.OperationResult) { Errors = batch.Errors ?? [new() { Message = $"Reading gama-api's {table} failed." }] };
                }

                if (batch.Data.Count == 0)
                {
                    break;
                }

                all.AddRange(batch.Data.Select(map).OfType<T>().Where(t => keepExam(t.ExamId)));
                afterId = batch.Data.Max(t => Long(t, "id") ?? 0);
            }

            result.Total = all.Count;
            foreach (var chunk in all.Chunk(RowBatchSize))
            {
                await UpsertAsync([.. chunk], result, copyInto);
            }

            HashSet<long> ids = [.. all.Select(t => t.Id)];
            var uow = UnitOfWorkProvider.Value.CreateUnitOfWork();
            var ours = await uow.GetRepository<T, long>().GetManyQueryable(t => true).Select(t => t.Id).ToListAsync();
            await RemoveAsync<T>([.. ours.Where(t => !ids.Contains(t))], result);
            return new(OperationResult.Succeeded) { Data = true };
        }

        private async Task UpsertAsync<T>(List<T> rows, ExamCopyTableResultDto result, Func<T, T, bool> copyInto)
            where T : class, IEntity<T, long>
        {
            List<long> ids = [.. rows.Select(t => t.Id)];
            var uow = UnitOfWorkProvider.Value.CreateUnitOfWork();
            var repository = uow.GetRepository<T, long>();
            var existing = await repository.GetManyQueryable(t => ids.Contains(t.Id), tracking: true).ToDictionaryAsync(t => t.Id);
            foreach (var row in rows)
            {
                if (!existing.TryGetValue(row.Id, out var current))
                {
                    repository.Add(row);
                    result.Added++;
                }
                else if (copyInto(row, current))
                {
                    result.Updated++;
                }
            }

            _ = await uow.SaveChangesAsync();
        }

        private async Task RemoveAsync<T>(List<long> ids, ExamCopyTableResultDto result)
            where T : class, IEntity<T, long>
        {
            foreach (var chunk in ids.Chunk(RowBatchSize))
            {
                var uow = UnitOfWorkProvider.Value.CreateUnitOfWork();
                var repository = uow.GetRepository<T, long>();
                foreach (var row in await repository.GetManyQueryable(t => chunk.Contains(t.Id), tracking: true).ToListAsync())
                {
                    repository.Remove(row);
                    result.Removed++;
                }

                _ = await uow.SaveChangesAsync();
            }
        }

        private static bool CopyInto(Exam from, Exam to) => Assign(from, to, from.LastModifyDate != to.LastModifyDate || from.Title != to.Title || from.QuestionIds != to.QuestionIds
            || from.Status != to.Status || from.IsDeleted != to.IsDeleted || from.OwnerUserId != to.OwnerUserId || from.ViewCount != to.ViewCount
            || from.ParticipantCount != to.ParticipantCount || from.DownloadCount != to.DownloadCount);

        private static bool CopyInto(ExamQuestion from, ExamQuestion to) => Assign(from, to, from.LastModifyDate != to.LastModifyDate || from.Status != to.Status
            || from.TrueAnswer != to.TrueAnswer || from.OwnerUserId != to.OwnerUserId || from.UsedConfirm != to.UsedConfirm || from.UsedPublish != to.UsedPublish);

        private static bool CopyInto(ExamAttempt from, ExamAttempt to) => Assign(from, to, from.Status != to.Status || from.ResultScore != to.ResultScore
            || from.SubmitSeconds != to.SubmitSeconds || from.UserId != to.UserId || from.ExamId != to.ExamId);

        private static bool CopyInto(ExamAnswerSheet from, ExamAnswerSheet to) => Assign(from, to, from.Answers != to.Answers || from.UserId != to.UserId
            || from.AttemptId != to.AttemptId || from.ExamId != to.ExamId);

        /// <summary>Copies every column of <paramref name="from"/> onto the tracked <paramref name="to"/> when anything differs.</summary>
        private static bool Assign<T>(T from, T to, bool changed)
            where T : class
        {
            if (changed)
            {
                foreach (var property in typeof(T).GetProperties().Where(t => t.CanWrite && t.Name != "Id"
                    && (t.PropertyType.IsValueType || t.PropertyType == typeof(string))))
                {
                    property.SetValue(to, property.GetValue(from));
                }
            }

            return changed;
        }

        private static long? Long(IReadOnlyDictionary<string, object?> row, string column) =>
            row.TryGetValue(column, out var value) && value is not null
                && long.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture), NumberStyles.Integer, CultureInfo.InvariantCulture, out var number)
                ? number
                : null;

        // A type id: gama-api writes 0 for none.
        private static int? Id(IReadOnlyDictionary<string, object?> row, string column) => Long(row, column) is long id and > 0 and <= int.MaxValue ? (int)id : null;

        private static byte? Byte(IReadOnlyDictionary<string, object?> row, string column) => Long(row, column) is long value and >= 0 and <= 255 ? (byte)value : null;

        private static string? Text(IReadOnlyDictionary<string, object?> row, string column) =>
            row.TryGetValue(column, out var value) && value is not null ? Convert.ToString(value, CultureInfo.InvariantCulture) : null;

        private static string? Empty(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

        private static string? Cut(string? value, int length) => value is { Length: > 0 } && value.Length > length ? value[..length] : value;

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
            return pairs.GroupBy(t => t.CoreId).ToDictionary(t => t.Key, t => t.Min(u => u.Id));
        }
    }
}
