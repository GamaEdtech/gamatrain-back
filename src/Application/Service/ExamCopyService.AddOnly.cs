namespace GamaEdtech.Application.Service
{
    using System.Linq;

    using GamaEdtech.Common.Core;
    using GamaEdtech.Common.Data;
    using GamaEdtech.Data.Dto.ExamCopy;
    using GamaEdtech.Domain.Entity;
    using GamaEdtech.Domain.Enumeration;

    using Microsoft.AspNetCore.Http;
    using Microsoft.EntityFrameworkCore;
    using Microsoft.Extensions.Configuration;
    using Microsoft.Extensions.Logging;

    using static GamaEdtech.Common.Core.Constants;

    /// <summary>
    /// The copy after the switch (<c>Migration:WriteRoutes:Exams</c> <c>Native</c>): exams are written here, so gama-api's
    /// older rows must never come back over them. Only rows gama-api created after the last full copy are brought, once: they
    /// get a new id here (gama-api keeps handing out ids that this backend may already have used) and keep gama-api's in
    /// <c>LegacyId</c>, whose unique index stops a row from coming twice. An exam's question list is mapped through those ids;
    /// their images are fetched from gama-api into the <c>ExamImage</c> container. Later edits on gama-api to a row already
    /// here are not brought. Attempts and answers are written here. See docs/business/exams-migration.md, "After the switch".
    /// </summary>
    public sealed partial class ExamCopyService
    {
        private const int MaxImageBytes = 10 * 1024 * 1024;
        private const string AddOnlyKey = "Migration:LegacyDatabase:AddOnlyAfter";

        private async Task<ResultData<ExamCopyResultDto>> AddNewAsync()
        {
            // The highest gama-api ids of the last full copy, set at the switch: everything up to them is here with gama-api's id.
            if (configuration.Value.GetValue<long?>($"{AddOnlyKey}:ExamId") is not long examsAfter
                || configuration.Value.GetValue<long?>($"{AddOnlyKey}:QuestionId") is not long questionsAfter)
            {
                return new(OperationResult.NotValid)
                {
                    Errors = [new() { Message = $"Exams are written here; set {AddOnlyKey}:ExamId and :QuestionId (gama-api's highest ids at the last full copy) to bring only newer rows." }],
                };
            }

            await using var held = await cacheProvider.Value.LockAsync(LockKey, TimeSpan.FromHours(1), TimeSpan.Zero);
            if (held is null)
            {
                return new(OperationResult.NotValid) { Errors = [new() { Message = "An exam copy is already running." }] };
            }

            ExamCopyResultDto result = new();
            var users = await CoreUserMapAsync();
            var timeZone = LegacyTimeZone();

            var questions = await NewRowsAsync<ExamQuestion>("azmoon_tests", QuestionColumns, questionsAfter, async (row, uow) =>
            {
                var question = ToQuestion(row, users, timeZone, DateTimeOffset.UtcNow);
                if (question is null)
                {
                    return null;
                }

                question.LegacyId = question.Id;
                question.Code = await FreeCodeAsync(uow.GetRepository<ExamQuestion>().GetManyQueryable(t => true).Select(t => t.Code), question.Code);
                question.QuestionFile = await CopyImageAsync(question.QuestionFile);
                question.FileA = await CopyImageAsync(question.FileA);
                question.FileB = await CopyImageAsync(question.FileB);
                question.FileC = await CopyImageAsync(question.FileC);
                question.FileD = await CopyImageAsync(question.FileD);
                question.AnswerFullFile = await CopyImageAsync(question.AnswerFullFile);
                return question;
            });
            if (questions.Errors is not null)
            {
                return new(OperationResult.Failed) { Data = result, Errors = questions.Errors };
            }

            result.Questions.Added = questions.Data;

            // gama-api question id → the id here, for the ones brought after the switch; older ones kept gama-api's id.
            var mapped = await UnitOfWorkProvider.Value.CreateUnitOfWork().GetRepository<ExamQuestion>()
                .GetManyQueryable(t => t.LegacyId != null).ToDictionaryAsync(t => t.LegacyId!.Value, t => t.Id);
            var exams = await NewRowsAsync<Exam>("azmoons", ExamColumns, examsAfter, async (row, uow) =>
            {
                var exam = ToExam(row, users, timeZone, DateTimeOffset.UtcNow);
                if (exam is null)
                {
                    return null;
                }

                exam.LegacyId = exam.Id;
                exam.Code = await FreeCodeAsync(uow.GetRepository<Exam>().GetManyQueryable(t => true).Select(t => t.Code), exam.Code);
                List<long> ids = [.. QuestionIdsOf(exam.QuestionIds)
                    .Select(t => t <= questionsAfter ? t : mapped.GetValueOrDefault(t))
                    .Where(t => t > 0)];
                exam.QuestionIds = ids.Count == 0 ? null : string.Join(',', ids);
                exam.QuestionCount = ids.Count;
                return exam;
            });
            if (exams.Errors is not null)
            {
                return new(OperationResult.Failed) { Data = result, Errors = exams.Errors };
            }

            result.Exams.Added = exams.Data;
            Logger.Value.LogWarning("Exam copy (add-only): new exams {Exams}; new questions {Questions}", result.Exams.Added, result.Questions.Added);
            return new(OperationResult.Succeeded) { Data = result };
        }

        /// <summary>
        /// Brings gama-api's rows above <paramref name="afterId"/> that aren't here yet (by <c>LegacyId</c>), each with the next
        /// free id here. Returns how many were added.
        /// </summary>
        private async Task<ResultData<int>> NewRowsAsync<T>(string table, string[] columns, long afterId,
            Func<IReadOnlyDictionary<string, object?>, Common.DataAccess.UnitOfWork.IUnitOfWork, Task<T?>> map)
            where T : class, Common.DataAccess.Entities.IEntity<T, long>
        {
            var existing = (await UnitOfWorkProvider.Value.CreateUnitOfWork().GetRepository<T>().GetManyQueryable(t => true)
                .Select(t => EF.Property<long?>(t, "LegacyId")).Where(t => t != null).ToListAsync()).Select(t => t!.Value).ToHashSet();
            var added = 0;
            var after = afterId;
            while (true)
            {
                var batch = await reader.Value.ReadBatchAsync(table, columns, after, RowBatchSize);
                if (batch.OperationResult is not OperationResult.Succeeded)
                {
                    return new(OperationResult.Failed) { Errors = batch.Errors };
                }

                var rows = batch.Data ?? [];
                foreach (var row in rows)
                {
                    if (Long(row, "id") is not long legacyId || existing.Contains(legacyId))
                    {
                        continue;
                    }

                    var uow = UnitOfWorkProvider.Value.CreateUnitOfWork();
                    if (await map(row, uow) is not { } item)
                    {
                        continue;
                    }

                    var repository = uow.GetRepository<T>();
                    item.Id = (await repository.GetManyQueryable(t => true).MaxAsync(t => (long?)t.Id) ?? 0) + 1;
                    repository.Add(item);
                    try
                    {
                        _ = await uow.SaveChangesAsync();
                        _ = existing.Add(legacyId);
                        added++;
                    }
                    catch (DbUpdateException exc)
                    {
                        // The id was taken meanwhile (a native write) or the row came in another run: the next run tries again.
                        Logger.Value.LogWarning(exc, "Exam copy (add-only): {Table} row {Id} not added this run", table, legacyId);
                    }
                }

                if (rows.Count < RowBatchSize)
                {
                    return new(OperationResult.Succeeded) { Data = added };
                }

                after = rows.Max(t => Long(t, "id") ?? after);
            }
        }

        /// <summary>A gama-api image name, fetched from its uploads into the <c>ExamImage</c> container; the stored id, or null
        /// when it can't be fetched (logged, the question is kept without it).</summary>
        private async Task<string?> CopyImageAsync(string? name)
        {
            if (string.IsNullOrWhiteSpace(name) || name == "0")
            {
                return null;
            }

            try
            {
                var core = configuration.Value.GetValue<string>("Core:Url");
                if (!name.StartsWith("http", StringComparison.OrdinalIgnoreCase) && string.IsNullOrEmpty(core))
                {
                    return null;
                }

                var source = name.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                    ? new Uri(name)
                    : new Uri(new Uri(core!), $"uploads/azmoonImages/{name.TrimStart('/')}");
                var content = await webDownloadProvider.Value.DownloadAsync(source, MaxImageBytes, CancellationToken.None);
                if (content.OperationResult is not OperationResult.Succeeded || content.Data is not { Length: > 0 } bytes)
                {
                    Logger.Value.LogWarning("Exam copy (add-only): image {Name} not fetched", name);
                    return null;
                }

                // Only image extensions are stored (the CDN serves by extension), whatever gama-api's name says.
                using MemoryStream stream = new(bytes);
                var (extension, contentType) = Path.GetExtension(source.AbsolutePath).ToUpperInvariant() switch
                {
                    ".PNG" => (".png", "image/png"),
                    ".GIF" => (".gif", "image/gif"),
                    ".WEBP" => (".webp", "image/webp"),
                    _ => (".jpg", "image/jpeg"),
                };
                FormFile file = new(stream, 0, bytes.LongLength, "file", $"image{extension}")
                {
                    Headers = new HeaderDictionary(),
                    ContentType = contentType,
                };
                var stored = await fileService.Value.CreateFileAsync(new() { File = file, ContainerType = ContainerType.ExamImage });
                return stored.OperationResult is OperationResult.Succeeded ? stored.Data : null;
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return null;
            }
        }

        /// <summary>gama-api's code when no row here has it, else a new one (codes are public links).</summary>
        private static async Task<string> FreeCodeAsync(IQueryable<string?> codes, string? code)
        {
            if (!string.IsNullOrEmpty(code) && !await codes.AnyAsync(t => t == code))
            {
                return code;
            }

            const string alphabet = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
            while (true)
            {
                var candidate = string.Create(6, alphabet, (span, letters) =>
                {
                    for (var i = 0; i < span.Length; i++)
                    {
                        span[i] = letters[System.Security.Cryptography.RandomNumberGenerator.GetInt32(letters.Length)];
                    }
                });
                if (!await codes.AnyAsync(t => t == candidate))
                {
                    return candidate;
                }
            }
        }

    }
}
