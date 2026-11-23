namespace GamaEdtech.Application.Service
{
    using System.Diagnostics.CodeAnalysis;
    using System.Globalization;
    using System.Linq;
    using System.Text.Json;
    using System.Text.Json.Nodes;

    using GamaEdtech.Common.Core;
    using GamaEdtech.Common.Data;
    using GamaEdtech.Common.DataAccess.UnitOfWork;
    using GamaEdtech.Common.Security;
    using GamaEdtech.Data.Dto.ExamImport;
    using GamaEdtech.Domain.Entity;
    using GamaEdtech.Domain.Entity.Identity;
    using GamaEdtech.Domain.Enumeration;

    using Microsoft.AspNetCore.Http;
    using Microsoft.EntityFrameworkCore;

    using static GamaEdtech.Common.Core.Constants;

    using Void = Common.Data.Void;

    /// <summary>
    /// Exam building for the MCP (the teacher's draft exam, its questions and images, publishing), on this backend's tables
    /// once <c>Migration:WriteRoutes:Exams</c> is <c>Native</c>. The caller is <c>UserId</c> on each request (the MCP's own
    /// signed-in user); every change needs the caller to own the exam or question. A draft is status 6, as on gama-api; the
    /// owner publishing it makes it 7 (public), and an admin confirming it (1) publishes the owner's own questions in it
    /// (<c>ExamAdminService.SetExamStatusAsync</c>). See docs/business/exams-migration.md, "Native exam building".
    /// </summary>
    public sealed partial class NativeExamSource
    {
        /// <summary>gama-api's status of an unpublished draft exam.</summary>
        private const byte DraftStatus = 6;

        /// <summary>gama-api's status of an exam its owner published (public, not yet confirmed).</summary>
        private const byte OwnerPublishedStatus = 7;

        /// <summary>gama-api's <c>type</c> of an online exam.</summary>
        private const byte OnlineExamType = 1;

        private const int MaxTitleLength = 200;
        private const int MaxQuestionIdsLength = 5000;
        private const int MaxImageBytes = 5 * 1024 * 1024;

        private static readonly Dictionary<string, byte> QuestionTypes = new(StringComparer.Ordinal)
        {
            ["fourchoice"] = 1,
            ["descriptive"] = 2,
            ["twochoice"] = 4,
            ["tf"] = 8,
            ["blank"] = 16,
            ["shortanswer"] = 32,
        };

        private static readonly string[] ImageTypes = ["image/png", "image/jpeg", "image/gif", "image/webp"];

        public async Task<ResultData<long?>> GetCurrentExamIdAsync([NotNull] ExamImportRequestDto requestDto)
        {
            try
            {
                if (requestDto.UserId <= 0)
                {
                    return SignedOut<long?>();
                }

                var uow = UnitOfWorkProvider.Value.CreateUnitOfWork();
                var id = await uow.GetRepository<Exam>()
                    .GetManyQueryable(t => t.OwnerUserId == requestDto.UserId && t.Status == DraftStatus && !t.IsDeleted)
                    .OrderByDescending(t => t.Id).Select(t => (long?)t.Id).FirstOrDefaultAsync();
                return new(OperationResult.Succeeded) { Data = id };
            }
            catch (Exception exc)
            {
                return Error<long?>(exc);
            }
        }

        public async Task<ResultData<ExamImportDraftDto>> GetExamAsync([NotNull] ExamImportRequestDto requestDto)
        {
            try
            {
                var uow = UnitOfWorkProvider.Value.CreateUnitOfWork();
                var exam = await uow.GetRepository<Exam>().GetManyQueryable(t => t.Id == requestDto.Id && !t.IsDeleted).FirstOrDefaultAsync();
                if (exam is null || (exam.OwnerUserId != requestDto.UserId && !PublicStatuses.Contains(exam.Status)))
                {
                    return Failed<ExamImportDraftDto>("noResult");
                }

                var context = await LoadContextAsync(uow, [exam]);
                return new(OperationResult.Succeeded) { Data = Draft(exam, context, requestDto.UserId) };
            }
            catch (Exception exc)
            {
                return Error<ExamImportDraftDto>(exc);
            }
        }

        public async Task<ResultData<ListDataSource<ExamImportDraftDto>>> GetExamsAsync([NotNull] ExamImportExamsRequestDto requestDto)
        {
            try
            {
                if (requestDto.UserId <= 0)
                {
                    return SignedOut<ListDataSource<ExamImportDraftDto>>();
                }

                var uow = UnitOfWorkProvider.Value.CreateUnitOfWork();
                var statuses = requestDto.Statuses.Select(t => (byte)t).ToArray();
                var query = uow.GetRepository<Exam>().GetManyQueryable(t => t.OwnerUserId == requestDto.UserId && !t.IsDeleted);
                if (statuses.Length > 0)
                {
                    query = query.Where(t => statuses.Contains(t.Status));
                }

                var size = Math.Clamp(requestDto.PageSize, 1, MaxPerPage);
                var total = await query.CountAsync();
                var exams = await query.OrderByDescending(t => t.Id).Skip((Math.Max(requestDto.Page, 1) - 1) * size).Take(size).ToListAsync();
                var context = await LoadContextAsync(uow, exams);
                return new(OperationResult.Succeeded)
                {
                    Data = new() { List = [.. exams.Select(t => Draft(t, context, requestDto.UserId))], TotalRecordsCount = total },
                };
            }
            catch (Exception exc)
            {
                return Error<ListDataSource<ExamImportDraftDto>>(exc);
            }
        }

        public async Task<ResultData<IEnumerable<ExamImportDraftQuestionDto>>> GetExamQuestionsAsync([NotNull] ExamImportRequestDto requestDto)
        {
            try
            {
                var uow = UnitOfWorkProvider.Value.CreateUnitOfWork();
                if (await OwnExamAsync(uow, requestDto.Id, requestDto.UserId, tracking: false) is not { } exam)
                {
                    return Failed<IEnumerable<ExamImportDraftQuestionDto>>("accessDenied");
                }

                List<long> ids = [.. ExamCopyService.QuestionIdsOf(exam.QuestionIds)];
                var questions = await uow.GetRepository<ExamQuestion>().GetManyQueryable(t => ids.Contains(t.Id)).ToDictionaryAsync(t => t.Id);
                return new(OperationResult.Succeeded)
                {
                    Data = [.. ids.Where(questions.ContainsKey).Select(t => DraftQuestion(questions[t], requestDto.UserId))],
                };
            }
            catch (Exception exc)
            {
                return Error<IEnumerable<ExamImportDraftQuestionDto>>(exc);
            }
        }

        public async Task<ResultData<IEnumerable<long>>> GetExamTestIdsAsync([NotNull] ExamImportRequestDto requestDto)
        {
            try
            {
                var uow = UnitOfWorkProvider.Value.CreateUnitOfWork();
                return await OwnExamAsync(uow, requestDto.Id, requestDto.UserId, tracking: false) is { } exam
                    ? new(OperationResult.Succeeded) { Data = [.. ExamCopyService.QuestionIdsOf(exam.QuestionIds)] }
                    : Failed<IEnumerable<long>>("accessDenied");
            }
            catch (Exception exc)
            {
                return Error<IEnumerable<long>>(exc);
            }
        }

        public async Task<ResultData<string>> UploadFileAsync([NotNull] ExamImportUploadRequestDto requestDto)
        {
            try
            {
                if (requestDto.UserId <= 0)
                {
                    return SignedOut<string>();
                }

                if (requestDto.Content.Length is 0 or > MaxImageBytes || !ImageTypes.Contains(requestDto.ContentType, StringComparer.OrdinalIgnoreCase))
                {
                    return Failed<string>("wrongFile");
                }

                // Stored for good in the questions' public image container; the id is what the question's image fields hold. The
                // extension comes from the checked type, never the given name, so a file can't be served as anything but an image.
                var contentType = requestDto.ContentType.ToLowerInvariant();
                using MemoryStream stream = new(requestDto.Content);
                FormFile file = new(stream, 0, requestDto.Content.LongLength, "file", $"figure{ImageExtension(contentType)}")
                {
                    Headers = new HeaderDictionary(),
                    ContentType = contentType,
                };
                var stored = await fileService.Value.CreateFileAsync(new() { File = file, ContainerType = ContainerType.ExamImage });
                return stored.OperationResult is OperationResult.Succeeded && !string.IsNullOrEmpty(stored.Data)
                    ? new(OperationResult.Succeeded) { Data = stored.Data }
                    : new(OperationResult.Failed) { Errors = stored.Errors ?? [new() { Message = "The image was not stored." }] };
            }
            catch (Exception exc)
            {
                return Error<string>(exc);
            }
        }

        public async Task<ResultData<long>> SaveExamTestAsync([NotNull] SaveExamTestRequestDto requestDto)
        {
            try
            {
                if (requestDto.UserId <= 0)
                {
                    return SignedOut<long>();
                }

                if (!QuestionTypes.TryGetValue(requestDto.Type, out var type))
                {
                    return Failed<long>("wrongType");
                }

                if (requestDto.SubjectId is not > 0)
                {
                    return Failed<long>("requiredField");
                }

                var uow = UnitOfWorkProvider.Value.CreateUnitOfWork();
                var now = DateTimeOffset.UtcNow;
                ExamQuestion question;
                if (requestDto.Id is long id)
                {
                    var found = await uow.GetRepository<ExamQuestion>().GetManyQueryable(t => t.Id == id, tracking: true).FirstOrDefaultAsync();
                    if (found is null || found.OwnerUserId != requestDto.UserId)
                    {
                        return Failed<long>("accessDenied");
                    }

                    question = found;
                }
                else
                {
                    var coreId = await uow.GetRepository<ApplicationUser>().GetManyQueryable(t => t.Id == requestDto.UserId).Select(t => t.CoreId).FirstOrDefaultAsync();
                    question = new()
                    {
                        CoreOwnerId = coreId ?? 0,
                        OwnerUserId = requestDto.UserId,
                        Code = await NewCodeAsync(uow.GetRepository<ExamQuestion>().GetManyQueryable(t => true).Select(t => t.Code)),
                        Direction = "ltr",

                        // Waiting: an admin confirming the exam publishes it (docs/business/exams-migration.md).
                        Status = 0,
                        CreationDate = now,
                    };
                }

                question.CoreBoardId = Positive(requestDto.BoardId);
                question.CoreGradeId = Positive(requestDto.GradeId);
                question.CoreCourseId = Positive(requestDto.CourseId);
                question.CoreSubjectId = requestDto.SubjectId;
                question.CoreTopicIds = requestDto.TopicId is > 0 ? requestDto.TopicId.Value.ToString(CultureInfo.InvariantCulture) : null;
                question.Level = requestDto.Level is > 0 and <= byte.MaxValue ? (byte)requestDto.Level : null;
                question.Type = type;
                question.Question = requestDto.QuestionHtml.SanitizeHtml();
                question.AnswerFull = requestDto.AnswerHtml.SanitizeHtml();
                question.Resource = Cut(requestDto.Resource?.Trim(), 250);
                question.TrueAnswer = requestDto.CorrectOption is >= 1 and <= 4 ? (byte)requestDto.CorrectOption : null;
                question.AnswerViewType = requestDto.ImageOptions ? (byte)1 : (byte)0;
                string?[] options = [.. Enumerable.Range(0, 4).Select(i => i < requestDto.OptionsHtml.Count ? requestDto.OptionsHtml[i].SanitizeHtml() : null)];
                (question.AnswerA, question.AnswerB, question.AnswerC, question.AnswerD) = (options[0], options[1], options[2], options[3]);

                // An image left out keeps the question's image.
                question.QuestionFile = requestDto.QuestionFile ?? question.QuestionFile;
                question.AnswerFullFile = requestDto.AnswerFile ?? question.AnswerFullFile;
                if (requestDto.ImageOptions)
                {
                    string? File(int i, string? current) => i < requestDto.OptionFiles.Count && !string.IsNullOrEmpty(requestDto.OptionFiles[i]) ? requestDto.OptionFiles[i] : current;
                    (question.FileA, question.FileB, question.FileC, question.FileD) = (File(0, question.FileA), File(1, question.FileB), File(2, question.FileC), File(3, question.FileD));
                }

                question.LastModifyDate = now;
                question.SyncDate = now;
                if (requestDto.Id is long existing)
                {
                    _ = await uow.SaveChangesAsync();
                    return new(OperationResult.Succeeded) { Data = existing };
                }

                return new(OperationResult.Succeeded) { Data = await SaveNewAsync(question, t => t.GetRepository<ExamQuestion>()) };
            }
            catch (Exception exc)
            {
                return Error<long>(exc);
            }
        }

        public async Task<ResultData<Void>> DeleteExamTestAsync([NotNull] ExamImportRequestDto requestDto)
        {
            try
            {
                var uow = UnitOfWorkProvider.Value.CreateUnitOfWork();
                var question = await uow.GetRepository<ExamQuestion>().GetManyQueryable(t => t.Id == requestDto.Id, tracking: true).FirstOrDefaultAsync();
                if (question is null || question.OwnerUserId != requestDto.UserId)
                {
                    return Failed<Void>("accessDenied");
                }

                // A question in someone's published exam, or with answers, stays: removing it would change their results.
                var id = requestDto.Id.ToString(CultureInfo.InvariantCulture);
                var inUse = await uow.GetRepository<Exam>().AnyAsync(t => !t.IsDeleted && t.Status != DraftStatus && ("," + t.QuestionIds + ",").Contains("," + id + ","));
                if (inUse || question.AnswersStat is { Length: > 0 })
                {
                    return Failed<Void>("inUse");
                }

                uow.GetRepository<ExamQuestion>().Remove(question);
                _ = await uow.SaveChangesAsync();
                return new(OperationResult.Succeeded) { Data = new() };
            }
            catch (Exception exc)
            {
                return Error<Void>(exc);
            }
        }

        public async Task<ResultData<long>> SaveExamAsync([NotNull] SaveExamRequestDto requestDto)
        {
            try
            {
                if (requestDto.UserId <= 0)
                {
                    return SignedOut<long>();
                }

                var title = SafeTitle(requestDto.Title);
                if (title.Length == 0 || requestDto.SubjectId <= 0 || requestDto.DurationMinutes is < 0 or > 1440)
                {
                    return Failed<long>("requiredField");
                }

                var uow = UnitOfWorkProvider.Value.CreateUnitOfWork();
                var now = DateTimeOffset.UtcNow;
                Exam exam;
                if (requestDto.ExamId is long id)
                {
                    if (await OwnExamAsync(uow, id, requestDto.UserId, tracking: true) is not { } found)
                    {
                        return Failed<long>("accessDenied");
                    }

                    exam = found;
                }
                else
                {
                    // A teacher builds one draft at a time, as on gama-api (alreadyInProgress).
                    if (await uow.GetRepository<Exam>().AnyAsync(t => t.OwnerUserId == requestDto.UserId && t.Status == DraftStatus && !t.IsDeleted))
                    {
                        return Failed<long>("alreadyInProgress");
                    }

                    var user = await uow.GetRepository<ApplicationUser>().GetManyQueryable(t => t.Id == requestDto.UserId)
                        .Select(t => new { t.CoreId, t.Group }).FirstOrDefaultAsync();
                    exam = new()
                    {
                        CoreOwnerId = user?.CoreId ?? 0,
                        OwnerUserId = requestDto.UserId,
                        UserType = user?.Group is > 0 and <= byte.MaxValue ? (byte)user.Group : null,
                        Type = OnlineExamType,
                        Status = DraftStatus,
                        Code = await NewCodeAsync(uow.GetRepository<Exam>().GetManyQueryable(t => true).Select(t => t.Code)),
                        QuestionIds = string.Empty,
                        CreationDate = now,
                    };
                }

                exam.Title = title;
                exam.CoreBoardId = Positive(requestDto.BoardId);
                exam.CoreGradeId = Positive(requestDto.GradeId);
                exam.CoreCourseId = Positive(requestDto.CourseId);
                exam.CoreSubjectId = requestDto.SubjectId;
                exam.CoreExamTypeId = Positive(requestDto.PaperId);
                exam.DurationMinutes = requestDto.DurationMinutes;
                exam.ScoreType = requestDto.NegativeMarking ? (byte)2 : (byte)1;
                exam.Level = requestDto.Level is > 0 and <= byte.MaxValue ? (byte)requestDto.Level : null;
                exam.EduYear = requestDto.Year is > 0 ? requestDto.Year : null;
                exam.EduMonth = requestDto.SessionMonth is > 0 and <= 12 ? (byte)requestDto.SessionMonth : null;
                exam.Metadata = WithPaper(exam.Metadata, requestDto.PastPaperId);
                exam.LastModifyDate = now;
                exam.SyncDate = now;

                long examId;
                if (requestDto.ExamId is long existing)
                {
                    _ = await uow.SaveChangesAsync();
                    examId = existing;
                }
                else
                {
                    examId = await SaveNewAsync(exam, t => t.GetRepository<Exam>());
                }

                if (requestDto.PastPaperId is long paperId and > 0)
                {
                    await LinkPaperAsync(paperId, examId);
                }

                return new(OperationResult.Succeeded) { Data = examId };
            }
            catch (Exception exc)
            {
                return Error<long>(exc);
            }
        }

        public async Task<ResultData<Void>> SetExamTestsAsync([NotNull] SetExamTestsRequestDto requestDto)
        {
            try
            {
                var uow = UnitOfWorkProvider.Value.CreateUnitOfWork();
                if (await OwnExamAsync(uow, requestDto.ExamId, requestDto.UserId, tracking: true) is not { } exam)
                {
                    return Failed<Void>("accessDenied");
                }

                List<long> ids = [.. requestDto.TestIds.Distinct()];
                if (await uow.GetRepository<ExamQuestion>().GetManyQueryable(t => ids.Contains(t.Id)).CountAsync() != ids.Count)
                {
                    return Failed<Void>("wrongQuestion");
                }

                var joined = string.Join(',', ids);
                if (joined.Length > MaxQuestionIdsLength)
                {
                    return Failed<Void>("tooManyQuestions");
                }

                exam.QuestionIds = joined;
                exam.QuestionCount = ids.Count;
                exam.LastModifyDate = DateTimeOffset.UtcNow;
                exam.SyncDate = exam.LastModifyDate.Value;
                _ = await uow.SaveChangesAsync();
                return new(OperationResult.Succeeded) { Data = new() };
            }
            catch (Exception exc)
            {
                return Error<Void>(exc);
            }
        }

        public async Task<ResultData<Void>> PublishExamAsync([NotNull] ExamImportRequestDto requestDto)
        {
            try
            {
                var uow = UnitOfWorkProvider.Value.CreateUnitOfWork();
                if (await OwnExamAsync(uow, requestDto.Id, requestDto.UserId, tracking: true) is not { } exam)
                {
                    return Failed<Void>("accessDenied");
                }

                var count = ExamCopyService.QuestionIdsOf(exam.QuestionIds).Count();
                if (count == 0)
                {
                    return Failed<Void>("minimumTests");
                }

                // The owner publishes (7, public at once); an admin confirming it (1) publishes its questions too.
                if (exam.Status == DraftStatus)
                {
                    exam.Status = OwnerPublishedStatus;
                }

                exam.QuestionCount = count;
                exam.LastModifyDate = DateTimeOffset.UtcNow;
                exam.SyncDate = exam.LastModifyDate.Value;
                _ = await uow.SaveChangesAsync();
                return new(OperationResult.Succeeded) { Data = new() };
            }
            catch (Exception exc)
            {
                return Error<Void>(exc);
            }
        }

        public async Task<ResultData<Void>> DeleteExamAsync([NotNull] ExamImportRequestDto requestDto)
        {
            try
            {
                var uow = UnitOfWorkProvider.Value.CreateUnitOfWork();
                if (await OwnExamAsync(uow, requestDto.Id, requestDto.UserId, tracking: true) is not { } exam)
                {
                    return Failed<Void>("accessDenied");
                }

                exam.IsDeleted = true;
                exam.LastModifyDate = DateTimeOffset.UtcNow;
                exam.SyncDate = exam.LastModifyDate.Value;
                _ = await uow.SaveChangesAsync();
                return new(OperationResult.Succeeded) { Data = new() };
            }
            catch (Exception exc)
            {
                return Error<Void>(exc);
            }
        }

        /// <summary>The exam, when it exists, isn't deleted and the caller owns it.</summary>
        private static Task<Exam?> OwnExamAsync(IUnitOfWork uow, long id, long userId, bool tracking) => userId <= 0
            ? Task.FromResult<Exam?>(null)
            : uow.GetRepository<Exam>().GetManyQueryable(t => t.Id == id && t.OwnerUserId == userId && !t.IsDeleted, tracking: tracking).FirstOrDefaultAsync();

        private static ExamImportDraftDto Draft(Exam exam, Context context, long userId)
        {
            string? Title(int? id) => id is int key && context.Items.TryGetValue(key, out var item) ? item.Title?.Trim() : null;
            return new()
            {
                Id = exam.Id,
                Title = exam.Title,
                BoardId = exam.CoreBoardId,
                Board = Title(exam.CoreBoardId),
                GradeId = exam.CoreGradeId,
                Grade = Title(exam.CoreGradeId),
                CourseId = exam.CoreCourseId,
                SubjectId = exam.CoreSubjectId,
                Subject = Title(exam.CoreSubjectId),
                PaperId = exam.CoreExamTypeId,
                Paper = Title(exam.CoreExamTypeId),
                DurationMinutes = exam.DurationMinutes,
                Year = exam.EduYear is > 0 ? exam.EduYear : null,
                SessionMonth = exam.EduMonth is > 0 ? exam.EduMonth : null,
                Level = exam.Level is > 0 ? exam.Level : null,
                NegativeMarking = exam.ScoreType == 2,
                PastPaperId = PaperOf(exam.Metadata),
                QuestionIds = [.. ExamCopyService.QuestionIdsOf(exam.QuestionIds)],
                Owner = exam.OwnerUserId == userId,
                Status = exam.Status,
            };
        }

        private ExamImportDraftQuestionDto DraftQuestion(ExamQuestion question, long userId)
        {
            Uri? Image(string? file) => ImageUrl(file) is { } url && Uri.TryCreate(url, UriKind.Absolute, out var uri) ? uri : null;
            return new()
            {
                Id = question.Id,
                Type = ExamQuestionText.TypeTitle(question.Type),
                Html = question.Question,
                Image = Image(question.QuestionFile),
                Options = [question.AnswerA, question.AnswerB, question.AnswerC, question.AnswerD],
                OptionImages = [Image(question.FileA), Image(question.FileB), Image(question.FileC), Image(question.FileD)],
                Correct = question.TrueAnswer ?? 0,
                AnswerHtml = question.AnswerFull,
                AnswerImage = Image(question.AnswerFullFile),
                Owner = question.OwnerUserId == userId,
                Pending = question.Status == 0,
            };
        }

        /// <summary>Adds the exam to the paper's exams (gama-api's <c>tests.exam_id</c>, a JSON list), once.</summary>
        private async Task LinkPaperAsync(long paperId, long examId)
        {
            var uow = UnitOfWorkProvider.Value.CreateUnitOfWork();
            var paper = await uow.GetRepository<PastPaper>().GetManyQueryable(t => t.Id == paperId, tracking: true).FirstOrDefaultAsync();
            if (paper is null)
            {
                return;
            }

            List<string> ids = [];
            try
            {
                if (!string.IsNullOrWhiteSpace(paper.CoreExamIds) && JsonNode.Parse(paper.CoreExamIds) is JsonArray list)
                {
                    ids.AddRange(list.Select(t => t?.ToString()).OfType<string>());
                }
            }
            catch (JsonException)
            {
                // A value that isn't a JSON list is replaced.
            }

            var id = examId.ToString(CultureInfo.InvariantCulture);
            if (ids.Contains(id))
            {
                return;
            }

            ids.Add(id);
            var json = new JsonArray([.. ids.Select(t => JsonValue.Create(t))]).ToJsonString();
            if (json.Length <= 300)
            {
                paper.CoreExamIds = json;
                _ = await uow.SaveChangesAsync();
            }
        }

        /// <summary>The exam's metadata with <c>paperID</c> set (or removed), as gama-api keeps the paper it was built from.</summary>
        private static string? WithPaper(string? metadata, long? paperId)
        {
            JsonObject data;
            try
            {
                data = Metadata(metadata) as JsonObject ?? [];
            }
            catch (JsonException)
            {
                data = [];
            }

            if (paperId is > 0)
            {
                data["paperID"] = paperId.Value;
            }
            else
            {
                _ = data.Remove("paperID");
            }

            var json = data.Count == 0 ? null : data.ToJsonString();
            return json is { Length: > 1000 } ? metadata : json;
        }

        private static long? PaperOf(string? metadata)
        {
            try
            {
                return Metadata(metadata) is JsonObject data && Long(data["paperID"]) is long id and > 0 ? id : null;
            }
            catch (JsonException)
            {
                return null;
            }
        }

        /// <summary>gama-api's short public code (6 letters and digits), one no row has.</summary>
        private static async Task<string> NewCodeAsync(IQueryable<string?> codes)
        {
            const string alphabet = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
            while (true)
            {
                var code = string.Create(6, alphabet, (span, letters) =>
                {
                    for (var i = 0; i < span.Length; i++)
                    {
                        span[i] = letters[System.Security.Cryptography.RandomNumberGenerator.GetInt32(letters.Length)];
                    }
                });
                if (!await codes.AnyAsync(t => t == code))
                {
                    return code;
                }
            }
        }

        /// <summary>gama-api's title rules: no slash, single spaces, at most 200 characters.</summary>
        private static string SafeTitle(string? title)
        {
            var safe = string.Join(' ', (title ?? string.Empty).Replace('/', '-').Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
            return safe.Length > MaxTitleLength ? safe[..MaxTitleLength] : safe;
        }

        private static string? Cut(string? value, int length) => string.IsNullOrEmpty(value) ? null : value[..Math.Min(length, value.Length)];

        private static int? Positive(int? value) => value is > 0 ? value : null;

        private static string ImageExtension(string contentType) => contentType switch
        {
            "image/png" => ".png",
            "image/gif" => ".gif",
            "image/webp" => ".webp",
            _ => ".jpg",
        };

        private static ResultData<T> SignedOut<T>() => Failed<T>("signInExpired");

        private ResultData<T> Error<T>(Exception exc)
        {
            Logger.Value.LogException(exc);
            return new(OperationResult.Failed) { Errors = [new() { Message = exc.Message }] };
        }
    }
}
