namespace GamaEdtech.Application.Service
{
    using System.Diagnostics.CodeAnalysis;
    using System.Globalization;
    using System.Linq;

    using GamaEdtech.Application.Interface;
    using GamaEdtech.Common.Core;
    using GamaEdtech.Common.Data;
    using GamaEdtech.Common.DataAccess.UnitOfWork;
    using GamaEdtech.Common.Security;
    using GamaEdtech.Common.Service;
    using GamaEdtech.Data.Dto.ExamAdmin;
    using GamaEdtech.Data.Dto.Taxonomy;
    using GamaEdtech.Domain.Entity;
    using GamaEdtech.Domain.Entity.Identity;
    using GamaEdtech.Domain.Enumeration;

    using Microsoft.AspNetCore.Http;
    using Microsoft.EntityFrameworkCore;
    using Microsoft.Extensions.Logging;

    using static GamaEdtech.Common.Core.Constants;

    public sealed class ExamAdminService(Lazy<IUnitOfWorkProvider> unitOfWorkProvider, Lazy<IHttpContextAccessor> httpContextAccessor,
        Lazy<ILogger<ExamAdminService>> logger, Lazy<IMigrationRouter> migrationRouter, Lazy<IFileService> fileService)
        : ServiceBase<ExamAdminService>(unitOfWorkProvider, httpContextAccessor, logger), IExamAdminService
    {
        private const int MaxPageSize = 100;
        private const int ExcerptLength = 200;
        private const int SaveAttempts = 3;

        /// <summary>gama-api's status of an exam an admin confirmed.</summary>
        private const byte ConfirmedStatus = 1;

        // gama-api's exam statuses an admin may set, and a question's.
        private static readonly byte[] ExamStatuses = [0, 1, 2, 3, 5, 6, 7];
        private static readonly byte[] QuestionStatuses = [0, 1, 2];
        private static readonly byte[] QuestionTypes = [1, 2, 4, 8, 16, 32];

        // A question's separate image files (gama-api's file, file_a..file_d, answer_full_file), by the place the admin form names.
        private static readonly Dictionary<string, Action<ExamQuestion, string?>> ImageSlots = new(StringComparer.OrdinalIgnoreCase)
        {
            ["question"] = (question, file) => question.QuestionFile = file,
            ["a"] = (question, file) => question.FileA = file,
            ["b"] = (question, file) => question.FileB = file,
            ["c"] = (question, file) => question.FileC = file,
            ["d"] = (question, file) => question.FileD = file,
            ["answerFull"] = (question, file) => question.AnswerFullFile = file,
        };

        public async Task<ResultData<ExamAdminListDto>> GetExamsAsync([NotNull] ExamAdminListRequestDto requestDto)
        {
            try
            {
                var uow = UnitOfWorkProvider.Value.CreateUnitOfWork();
                var query = uow.GetRepository<Exam>().GetManyQueryable(t => requestDto.IncludeDeleted || !t.IsDeleted);
                query = requestDto.Status is byte status ? query.Where(t => t.Status == status) : query;
                query = requestDto.BoardId is int board ? query.Where(t => t.CoreBoardId == board) : query;
                query = requestDto.GradeId is int grade ? query.Where(t => t.CoreGradeId == grade) : query;
                query = requestDto.SubjectId is int subject ? query.Where(t => t.CoreSubjectId == subject) : query;
                query = requestDto.ExamTypeId is int examType ? query.Where(t => t.CoreExamTypeId == examType) : query;
                query = requestDto.OwnerUserId is long owner ? query.Where(t => t.OwnerUserId == owner) : query;
                query = requestDto.Combination is byte combination ? query.Where(t => t.QuestionsCombination == combination) : query;
                query = requestDto.Year is int year ? query.Where(t => t.EduYear == year) : query;
                if (requestDto.Open is bool open)
                {
                    // gama-api's examOpen: no dates (any time), or now between its start and end.
                    var now = DateTimeOffset.UtcNow;
                    query = open
                        ? query.Where(t => (t.StartDate == null || t.StartDate <= now) && (t.EndDate == null || t.EndDate >= now))
                        : query.Where(t => (t.StartDate != null && t.StartDate > now) || (t.EndDate != null && t.EndDate < now));
                }

                if (!string.IsNullOrWhiteSpace(requestDto.Text))
                {
                    var text = requestDto.Text.Trim();
                    query = long.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var id)
                        ? query.Where(t => t.Id == id || t.Title!.Contains(text))
                        : query.Where(t => t.Title!.Contains(text) || t.Code == text);
                }

                var size = Math.Clamp(requestDto.PageSize, 1, MaxPageSize);
                var total = await query.CountAsync();
                var page = await query.OrderByDescending(t => t.LastModifyDate).ThenByDescending(t => t.Id)
                    .Skip((Math.Max(requestDto.Page, 1) - 1) * size).Take(size).ToListAsync();
                var names = await NamesAsync(uow, page, []);
                return new(OperationResult.Succeeded) { Data = new() { Total = total, Items = [.. page.Select(t => FillExam(new ExamAdminItemDto(), t, names))] } };
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return new(OperationResult.Failed) { Errors = [new() { Message = exc.Message }] };
            }
        }

        public async Task<ResultData<ExamParticipationListDto>> GetParticipationsAsync([NotNull] ExamParticipationListRequestDto requestDto)
        {
            try
            {
                var uow = UnitOfWorkProvider.Value.CreateUnitOfWork();
                var query = uow.GetRepository<ExamAttempt>().GetManyQueryable();
                query = requestDto.ExamId is long examId ? query.Where(t => t.ExamId == examId) : query;
                query = requestDto.UserId is long userId ? query.Where(t => t.UserId == userId) : query;
                query = requestDto.Status is byte status ? query.Where(t => t.Status == status) : query;
                query = requestDto.From is DateTimeOffset from ? query.Where(t => t.CreationDate >= from) : query;
                query = requestDto.ToExclusive is DateTimeOffset to ? query.Where(t => t.CreationDate < to) : query;
                if (!string.IsNullOrWhiteSpace(requestDto.Text))
                {
                    var text = requestDto.Text.Trim();
                    query = query.Where(t => t.User != null && (t.User.Email!.Contains(text) || t.User.UserName!.Contains(text)
                        || t.User.FirstName!.Contains(text) || t.User.LastName!.Contains(text)));
                }

                var total = await query.CountAsync();
                var finished = await query.CountAsync(t => t.Status == 1);

                // A participant is a user here, or a gama-api user not linked yet.
                var participants = await query.Select(t => t.UserId != null ? t.UserId.Value : -t.CoreUserId).Distinct().CountAsync();

                var size = Math.Clamp(requestDto.PageSize, 1, MaxPageSize);
                var page = await query.OrderByDescending(t => t.CreationDate).ThenByDescending(t => t.Id)
                    .Skip((Math.Max(requestDto.Page, 1) - 1) * size).Take(size)
                    .Select(t => new ExamParticipationItemDto
                    {
                        Id = t.Id,
                        ExamId = t.ExamId,
                        UserId = t.UserId,
                        CoreUserId = t.CoreUserId,
                        UserName = t.User == null ? null : ((t.User.FirstName ?? string.Empty) + " " + (t.User.LastName ?? string.Empty)).Trim(),
                        Email = t.User == null ? null : t.User.Email,
                        Status = t.Status,
                        ResultScore = t.ResultScore,
                        SubmitSeconds = t.SubmitSeconds,
                        StartDate = t.CreationDate,
                    })
                    .ToListAsync();

                List<long> examIds = [.. page.Select(t => t.ExamId).Distinct()];
                var titles = await uow.GetRepository<Exam>().GetManyQueryable(t => examIds.Contains(t.Id))
                    .Select(t => new { t.Id, t.Title }).ToDictionaryAsync(t => t.Id, t => t.Title);
                foreach (var item in page)
                {
                    item.ExamTitle = titles.GetValueOrDefault(item.ExamId);
                    item.UserName = string.IsNullOrEmpty(item.UserName) ? item.Email : item.UserName;
                }

                return new(OperationResult.Succeeded) { Data = new() { Total = total, Finished = finished, Participants = participants, Items = page } };
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return new(OperationResult.Failed) { Errors = [new() { Message = exc.Message }] };
            }
        }

        public async Task<ResultData<ExamAdminDto>> GetExamAsync(long id)
        {
            try
            {
                var uow = UnitOfWorkProvider.Value.CreateUnitOfWork();
                var exam = await uow.GetRepository<Exam>().GetManyQueryable(t => t.Id == id).FirstOrDefaultAsync();
                if (exam is null)
                {
                    return NotFound<ExamAdminDto>();
                }

                List<long> ids = [.. ExamCopyService.QuestionIdsOf(exam.QuestionIds)];
                var questions = await uow.GetRepository<ExamQuestion>().GetManyQueryable(t => ids.Contains(t.Id)).ToDictionaryAsync(t => t.Id);
                var names = await NamesAsync(uow, [exam], [.. questions.Values]);
                var data = FillExam(new ExamAdminDto(), exam, names);
                data.CoreTopicIds = exam.CoreTopicIds;
                data.Level = exam.Level;
                data.ScoreType = exam.ScoreType;
                data.EduYear = exam.EduYear;
                data.EduMonth = exam.EduMonth;
                data.StartDate = exam.StartDate;
                data.EndDate = exam.EndDate;
                data.Questions = [.. ids.Where(questions.ContainsKey).Select(t => FillQuestion(new ExamQuestionAdminItemDto(), questions[t], names))];
                return new(OperationResult.Succeeded) { Data = data };
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return new(OperationResult.Failed) { Errors = [new() { Message = exc.Message }] };
            }
        }

        public async Task<ResultData<bool>> UpdateExamAsync(long id, [NotNull] ExamEditDto requestDto)
        {
            try
            {
                if (NotNative<bool>() is { } refused)
                {
                    return refused;
                }

                if (string.IsNullOrWhiteSpace(requestDto.Title) || requestDto.Title.Trim().Length > 500)
                {
                    return Invalid<bool>("title", "The title is required, at most 500 characters.");
                }

                if (requestDto.ScoreType is not (null or 1 or 2) || requestDto.DurationMinutes is < 0 or > 1440 || requestDto.EduMonth is > 12)
                {
                    return Invalid<bool>("wrongValue", "A setting is out of range.");
                }

                var uow = UnitOfWorkProvider.Value.CreateUnitOfWork();
                var exam = await uow.GetRepository<Exam>().GetManyQueryable(t => t.Id == id, tracking: true).FirstOrDefaultAsync();
                if (exam is null)
                {
                    return NotFound<bool>();
                }

                if (!await TypesExistAsync(uow, requestDto.BoardId, requestDto.GradeId, requestDto.SubjectId, requestDto.CoreTopicIds))
                {
                    return Invalid<bool>("wrongType", "A board, grade, subject or topic does not exist.");
                }

                exam.Title = requestDto.Title.Trim();
                exam.CoreBoardId = Positive(requestDto.BoardId);
                exam.CoreGradeId = Positive(requestDto.GradeId);
                exam.CoreSubjectId = Positive(requestDto.SubjectId);
                exam.CoreTopicIds = string.IsNullOrWhiteSpace(requestDto.CoreTopicIds) ? null : requestDto.CoreTopicIds.Trim();
                exam.CoreExamTypeId = Positive(requestDto.ExamTypeId);
                exam.Level = requestDto.Level;
                exam.DurationMinutes = requestDto.DurationMinutes;
                exam.ScoreType = requestDto.ScoreType ?? exam.ScoreType;
                exam.EduYear = requestDto.EduYear;
                exam.EduMonth = requestDto.EduMonth is > 0 ? requestDto.EduMonth : null;
                exam.StartDate = requestDto.StartDate;
                exam.EndDate = requestDto.EndDate;
                Touch(exam);
                _ = await uow.SaveChangesAsync();
                return new(OperationResult.Succeeded) { Data = true };
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return new(OperationResult.Failed) { Errors = [new() { Message = exc.Message }] };
            }
        }

        public async Task<ResultData<bool>> SetExamStatusAsync(long id, byte status)
        {
            if (!ExamStatuses.Contains(status))
            {
                return Invalid<bool>("status", "Unknown exam status.");
            }

            var changed = await ChangeExamAsync(id, exam => exam.Status = status);
            return changed.OperationResult is OperationResult.Succeeded && status == ConfirmedStatus
                ? await PublishOwnQuestionsAsync(id)
                : changed;
        }

        public Task<ResultData<bool>> DeleteExamAsync(long id) => ChangeExamAsync(id, exam => exam.IsDeleted = true);

        public async Task<ResultData<bool>> SetExamQuestionsAsync(long id, [NotNull] IReadOnlyList<long> questionIds)
        {
            try
            {
                if (NotNative<bool>() is { } refused)
                {
                    return refused;
                }

                List<long> ids = [.. questionIds.Distinct()];
                var uow = UnitOfWorkProvider.Value.CreateUnitOfWork();
                if (await uow.GetRepository<ExamQuestion>().GetManyQueryable(t => ids.Contains(t.Id)).CountAsync() != ids.Count)
                {
                    return Invalid<bool>("wrongQuestion", "A question does not exist.");
                }

                var joined = string.Join(',', ids);
                return joined.Length > 5000
                    ? Invalid<bool>("tooManyQuestions", "Too many questions for one exam.")
                    : await ChangeExamAsync(id, exam =>
                    {
                        exam.QuestionIds = joined;
                        exam.QuestionCount = ids.Count;
                    });
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return new(OperationResult.Failed) { Errors = [new() { Message = exc.Message }] };
            }
        }

        public async Task<ResultData<ExamQuestionAdminListDto>> GetQuestionsAsync([NotNull] ExamQuestionAdminListRequestDto requestDto)
        {
            try
            {
                var uow = UnitOfWorkProvider.Value.CreateUnitOfWork();
                var query = uow.GetRepository<ExamQuestion>().GetManyQueryable(t => true);
                query = requestDto.Status is byte status ? query.Where(t => t.Status == status) : query;
                query = requestDto.BoardId is int board ? query.Where(t => t.CoreBoardId == board) : query;
                query = requestDto.SubjectId is int subject ? query.Where(t => t.CoreSubjectId == subject) : query;
                query = requestDto.Type is byte type ? query.Where(t => t.Type == type) : query;
                query = requestDto.OwnerUserId is long owner ? query.Where(t => t.OwnerUserId == owner) : query;
                if (requestDto.TopicId is int topic)
                {
                    query = query.Where(TopicMatching.TopicIn<ExamQuestion>(nameof(ExamQuestion.CoreTopicIds), [topic.ToString(CultureInfo.InvariantCulture)]));
                }

                if (!string.IsNullOrWhiteSpace(requestDto.Text))
                {
                    var text = requestDto.Text.Trim();
                    query = long.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var id)
                        ? query.Where(t => t.Id == id || t.Question!.Contains(text))
                        : query.Where(t => t.Question!.Contains(text) || t.Code == text);
                }

                var size = Math.Clamp(requestDto.PageSize, 1, MaxPageSize);
                var total = await query.CountAsync();
                var page = await query.OrderByDescending(t => t.LastModifyDate).ThenByDescending(t => t.Id)
                    .Skip((Math.Max(requestDto.Page, 1) - 1) * size).Take(size).ToListAsync();
                var names = await NamesAsync(uow, [], page);
                return new(OperationResult.Succeeded) { Data = new() { Total = total, Items = [.. page.Select(t => FillQuestion(new ExamQuestionAdminItemDto(), t, names))] } };
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return new(OperationResult.Failed) { Errors = [new() { Message = exc.Message }] };
            }
        }

        public async Task<ResultData<ExamQuestionAdminDto>> GetQuestionAsync(long id)
        {
            try
            {
                var uow = UnitOfWorkProvider.Value.CreateUnitOfWork();
                var question = await uow.GetRepository<ExamQuestion>().GetManyQueryable(t => t.Id == id).FirstOrDefaultAsync();
                if (question is null)
                {
                    return NotFound<ExamQuestionAdminDto>();
                }

                var names = await NamesAsync(uow, [], [question]);
                var data = FillQuestion(new ExamQuestionAdminDto(), question, names);
                data.BoardId = question.CoreBoardId;
                data.GradeId = question.CoreGradeId;
                data.CoreTopicIds = question.CoreTopicIds;
                data.Level = question.Level;
                data.TypeCode = question.Type;
                data.Question = question.Question;
                data.AnswerA = question.AnswerA;
                data.AnswerB = question.AnswerB;
                data.AnswerC = question.AnswerC;
                data.AnswerD = question.AnswerD;
                data.AnswerFull = question.AnswerFull;
                data.QuestionImage = ImageUrl(question.QuestionFile);
                data.AnswerAImage = ImageUrl(question.FileA);
                data.AnswerBImage = ImageUrl(question.FileB);
                data.AnswerCImage = ImageUrl(question.FileC);
                data.AnswerDImage = ImageUrl(question.FileD);
                data.AnswerFullImage = ImageUrl(question.AnswerFullFile);
                data.Resource = question.Resource;
                return new(OperationResult.Succeeded) { Data = data };
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return new(OperationResult.Failed) { Errors = [new() { Message = exc.Message }] };
            }
        }

        public async Task<ResultData<long>> CreateQuestionAsync(long userId, [NotNull] ExamQuestionEditDto requestDto)
        {
            try
            {
                if (NotNative<long>() is { } refused)
                {
                    return refused;
                }

                var uow = UnitOfWorkProvider.Value.CreateUnitOfWork();
                if (await CheckQuestionAsync(uow, requestDto) is { } invalid)
                {
                    return new(OperationResult.NotValid) { Errors = invalid.Errors };
                }

                var now = DateTimeOffset.UtcNow;
                var coreId = await uow.GetRepository<ApplicationUser>().GetManyQueryable(t => t.Id == userId).Select(t => t.CoreId).FirstOrDefaultAsync();
                ExamQuestion question = new()
                {
                    CoreOwnerId = coreId ?? 0,
                    OwnerUserId = userId,
                    Code = NewCode(),
                    Status = requestDto.Status ?? 0,
                    CreationDate = now,
                    LastModifyDate = now,
                    SyncDate = now,
                };
                Apply(question, requestDto);
                return new(OperationResult.Succeeded) { Data = await SaveNewAsync(question) };
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return new(OperationResult.Failed) { Errors = [new() { Message = exc.Message }] };
            }
        }

        public async Task<ResultData<bool>> UpdateQuestionAsync(long id, [NotNull] ExamQuestionEditDto requestDto)
        {
            try
            {
                if (NotNative<bool>() is { } refused)
                {
                    return refused;
                }

                var uow = UnitOfWorkProvider.Value.CreateUnitOfWork();
                var question = await uow.GetRepository<ExamQuestion>().GetManyQueryable(t => t.Id == id, tracking: true).FirstOrDefaultAsync();
                if (question is null)
                {
                    return NotFound<bool>();
                }

                if (await CheckQuestionAsync(uow, requestDto) is { } invalid)
                {
                    return new(OperationResult.NotValid) { Errors = invalid.Errors };
                }

                Apply(question, requestDto);
                question.Status = requestDto.Status ?? question.Status;
                question.LastModifyDate = DateTimeOffset.UtcNow;
                question.SyncDate = question.LastModifyDate.Value;
                _ = await uow.SaveChangesAsync();
                return new(OperationResult.Succeeded) { Data = true };
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return new(OperationResult.Failed) { Errors = [new() { Message = exc.Message }] };
            }
        }

        public Task<ResultData<bool>> SetQuestionStatusAsync(long id, byte status) =>
            !QuestionStatuses.Contains(status)
                ? Task.FromResult(Invalid<bool>("status", "Unknown question status."))
                : ChangeQuestionAsync(id, question => question.Status = status);

        public async Task<ResultData<string>> SetQuestionImageAsync(long id, string slot, [NotNull] byte[] content)
        {
            try
            {
                if (NotNative<string>() is { } refused)
                {
                    return refused;
                }

                if (!ImageSlots.TryGetValue(slot ?? string.Empty, out var setImage))
                {
                    return Invalid<string>("slot", "Unknown image place.");
                }

                if (content.LongLength is 0 or > IEditorImageService.MaxBytes)
                {
                    return Invalid<string>("file", "The image must be at most 5 MB.");
                }

                // The kind comes from the bytes, never from the name; re-encoding drops the metadata (as for editor images).
                var kind = EditorImageService.Kind(content);
                if (kind is null)
                {
                    return Invalid<string>("file", "Only JPG, PNG, GIF or WebP images can be used.");
                }

                var (image, extension, contentType) = kind == "gif" ? (content, ".gif", "image/gif") : EditorImageService.Reencoded(content, kind);
                if (image is null)
                {
                    return Invalid<string>("file", "The image could not be read.");
                }

                var uow = UnitOfWorkProvider.Value.CreateUnitOfWork();
                var question = await uow.GetRepository<ExamQuestion>().GetManyQueryable(t => t.Id == id, tracking: true).FirstOrDefaultAsync();
                if (question is null)
                {
                    return NotFound<string>();
                }

                using MemoryStream stream = new(image);
                FormFile file = new(stream, 0, image.LongLength, "file", "image" + extension) { Headers = new HeaderDictionary(), ContentType = contentType };
                var stored = await fileService.Value.CreateFileAsync(new() { File = file, ContainerType = ContainerType.ExamImage });
                if (stored.OperationResult is not OperationResult.Succeeded || string.IsNullOrEmpty(stored.Data))
                {
                    return new(OperationResult.Failed) { Errors = stored.Errors ?? [new() { Message = "The image could not be stored." }] };
                }

                // The previous file is not deleted: gama-api's clones share image names, and so do their copies here.
                setImage(question, stored.Data);
                question.LastModifyDate = DateTimeOffset.UtcNow;
                question.SyncDate = question.LastModifyDate.Value;
                _ = await uow.SaveChangesAsync();
                return new(OperationResult.Succeeded) { Data = ImageUrl(stored.Data) };
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return new(OperationResult.Failed) { Errors = [new() { Message = exc.Message }] };
            }
        }

        public Task<ResultData<bool>> RemoveQuestionImageAsync(long id, string slot) =>
            ImageSlots.TryGetValue(slot ?? string.Empty, out var setImage)
                ? ChangeQuestionAsync(id, question => setImage(question, null))
                : Task.FromResult(Invalid<bool>("slot", "Unknown image place."));

        public async Task<ResultData<bool>> DeleteQuestionAsync(long id)
        {
            try
            {
                if (NotNative<bool>() is { } refused)
                {
                    return refused;
                }

                var uow = UnitOfWorkProvider.Value.CreateUnitOfWork();
                var key = id.ToString(CultureInfo.InvariantCulture);
                // A question an exam still uses is not deleted: its results and the exam would lose it.
                if (await uow.GetRepository<Exam>().GetManyQueryable(t => t.QuestionIds == key || t.QuestionIds!.StartsWith(key + ",")
                    || t.QuestionIds.EndsWith("," + key) || t.QuestionIds.Contains("," + key + ",")).AnyAsync())
                {
                    return Invalid<bool>("usedByExam", "An exam uses this question; take it out of the exam first.");
                }

                var repository = uow.GetRepository<ExamQuestion>();
                var question = await repository.GetManyQueryable(t => t.Id == id, tracking: true).FirstOrDefaultAsync();
                if (question is null)
                {
                    return NotFound<bool>();
                }

                repository.Remove(question);
                _ = await uow.SaveChangesAsync();
                return new(OperationResult.Succeeded) { Data = true };
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return new(OperationResult.Failed) { Errors = [new() { Message = exc.Message }] };
            }
        }

        /// <summary>
        /// Confirming an exam publishes the questions its owner wrote for it (status 0 waiting or 2 personal use, to 1), so they
        /// get their public pages. On gama-api a teacher's questions stayed "personal use" for good and a confirmed exam showed
        /// samples that answered 404. Questions of other authors in the exam are left alone.
        /// </summary>
        private async Task<ResultData<bool>> PublishOwnQuestionsAsync(long examId)
        {
            try
            {
                var uow = UnitOfWorkProvider.Value.CreateUnitOfWork();
                var exam = await uow.GetRepository<Exam>().GetManyQueryable(t => t.Id == examId)
                    .Select(t => new { t.OwnerUserId, t.QuestionIds }).FirstOrDefaultAsync();
                if (exam?.OwnerUserId is not long owner)
                {
                    return new(OperationResult.Succeeded) { Data = true };
                }

                List<long> ids = [.. ExamCopyService.QuestionIdsOf(exam.QuestionIds)];
                var now = DateTimeOffset.UtcNow;
                _ = await uow.GetRepository<ExamQuestion>()
                    .GetManyQueryable(t => ids.Contains(t.Id) && t.OwnerUserId == owner && (t.Status == 0 || t.Status == 2))
                    .ExecuteUpdateAsync(t => t.SetProperty(q => q.Status, (byte)1).SetProperty(q => q.LastModifyDate, now).SetProperty(q => q.SyncDate, now));
                return new(OperationResult.Succeeded) { Data = true };
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return new(OperationResult.Failed) { Errors = [new() { Message = exc.Message }] };
            }
        }

        private async Task<ResultData<bool>> ChangeExamAsync(long id, Action<Exam> change)
        {
            try
            {
                if (NotNative<bool>() is { } refused)
                {
                    return refused;
                }

                var uow = UnitOfWorkProvider.Value.CreateUnitOfWork();
                var exam = await uow.GetRepository<Exam>().GetManyQueryable(t => t.Id == id, tracking: true).FirstOrDefaultAsync();
                if (exam is null)
                {
                    return NotFound<bool>();
                }

                change(exam);
                Touch(exam);
                _ = await uow.SaveChangesAsync();
                return new(OperationResult.Succeeded) { Data = true };
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return new(OperationResult.Failed) { Errors = [new() { Message = exc.Message }] };
            }
        }

        private async Task<ResultData<bool>> ChangeQuestionAsync(long id, Action<ExamQuestion> change)
        {
            try
            {
                if (NotNative<bool>() is { } refused)
                {
                    return refused;
                }

                var uow = UnitOfWorkProvider.Value.CreateUnitOfWork();
                var question = await uow.GetRepository<ExamQuestion>().GetManyQueryable(t => t.Id == id, tracking: true).FirstOrDefaultAsync();
                if (question is null)
                {
                    return NotFound<bool>();
                }

                change(question);
                question.LastModifyDate = DateTimeOffset.UtcNow;
                question.SyncDate = question.LastModifyDate.Value;
                _ = await uow.SaveChangesAsync();
                return new(OperationResult.Succeeded) { Data = true };
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return new(OperationResult.Failed) { Errors = [new() { Message = exc.Message }] };
            }
        }

        /// <summary>gama-api's required parts of a question: the text, a known type and its subject; a choice question needs its
        /// options and a correct answer among them; the types must exist.</summary>
        private static async Task<ResultData<bool>?> CheckQuestionAsync(IUnitOfWork uow, ExamQuestionEditDto edit)
        {
            if (string.IsNullOrWhiteSpace(TextOf(edit.Question)) && !(edit.Question ?? string.Empty).Contains("<img", StringComparison.OrdinalIgnoreCase))
            {
                return Invalid<bool>("question", "The question is required.");
            }

            if (edit.Type is not byte type || !QuestionTypes.Contains(type))
            {
                return Invalid<bool>("type", "Unknown question type.");
            }

            if (edit.SubjectId is not > 0)
            {
                return Invalid<bool>("subject", "The subject is required.");
            }

            if (edit.Status is byte status && !QuestionStatuses.Contains(status))
            {
                return Invalid<bool>("status", "Unknown question status.");
            }

            // Four-choice and two-choice questions need their options and the right one among them.
            var options = type switch { 1 => 4, 4 => 2, _ => 0 };
            if (options > 0)
            {
                string?[] given = [edit.AnswerA, edit.AnswerB, edit.AnswerC, edit.AnswerD];
                if (given.Take(options).Any(t => string.IsNullOrWhiteSpace(TextOf(t)) && !(t ?? string.Empty).Contains("<img", StringComparison.OrdinalIgnoreCase))
                    || edit.TrueAnswer is not byte answer || answer < 1 || answer > options)
                {
                    return Invalid<bool>("answers", "Every option and the correct one are required.");
                }
            }

            return await TypesExistAsync(uow, edit.BoardId, edit.GradeId, edit.SubjectId, edit.CoreTopicIds)
                ? null
                : Invalid<bool>("wrongType", "A board, grade, subject or topic does not exist.");
        }

        private static void Apply(ExamQuestion question, ExamQuestionEditDto edit)
        {
            question.CoreBoardId = Positive(edit.BoardId);
            question.CoreGradeId = Positive(edit.GradeId);
            question.CoreSubjectId = Positive(edit.SubjectId);
            question.CoreTopicIds = string.IsNullOrWhiteSpace(edit.CoreTopicIds) ? null : edit.CoreTopicIds.Trim();
            question.Level = edit.Level;
            question.Type = edit.Type;
            question.Question = edit.Question.SanitizeHtml();
            question.AnswerA = edit.AnswerA.SanitizeHtml();
            question.AnswerB = edit.AnswerB.SanitizeHtml();
            question.AnswerC = edit.AnswerC.SanitizeHtml();
            question.AnswerD = edit.AnswerD.SanitizeHtml();
            question.AnswerFull = edit.AnswerFull.SanitizeHtml();
            question.TrueAnswer = edit.TrueAnswer;
            question.Resource = string.IsNullOrWhiteSpace(edit.Resource) ? null : edit.Resource.Trim()[..Math.Min(edit.Resource.Trim().Length, 200)];
        }

        private static async Task<bool> TypesExistAsync(IUnitOfWork uow, int? board, int? grade, int? subject, string? topics)
        {
            List<(int Id, short Kind)> wanted = [];
            if (board is > 0)
            {
                wanted.Add((board.Value, LegacyTaxonomyKinds.Board));
            }

            if (grade is > 0)
            {
                wanted.Add((grade.Value, LegacyTaxonomyKinds.Grade));
            }

            if (subject is > 0)
            {
                wanted.Add((subject.Value, LegacyTaxonomyKinds.Subject));
            }

            foreach (var topic in (topics ?? string.Empty).Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (!int.TryParse(topic, NumberStyles.None, CultureInfo.InvariantCulture, out var id))
                {
                    return false;
                }

                wanted.Add((id, LegacyTaxonomyKinds.Topic));
            }

            List<int> ids = [.. wanted.Select(t => t.Id)];
            var kinds = await uow.GetRepository<LegacyTaxonomyItem, int>().GetManyQueryable(t => ids.Contains(t.Id) && !t.IsDeleted)
                .Select(t => new { t.Id, t.Kind }).ToDictionaryAsync(t => t.Id, t => t.Kind);
            return wanted.All(t => kinds.TryGetValue(t.Id, out var kind) && kind == t.Kind);
        }

        private sealed record Names(IReadOnlyDictionary<int, string?> Types, IReadOnlyDictionary<long, string> Owners);

        private static async Task<Names> NamesAsync(IUnitOfWork uow, IReadOnlyCollection<Exam> exams, IReadOnlyCollection<ExamQuestion> questions)
        {
            HashSet<int> ids = [];
            foreach (var exam in exams)
            {
                ids.UnionWith(new int?[] { exam.CoreBoardId, exam.CoreGradeId, exam.CoreSubjectId, exam.CoreExamTypeId }.OfType<int>());
            }

            foreach (var question in questions)
            {
                ids.UnionWith(new int?[] { question.CoreSubjectId, FirstTopic(question.CoreTopicIds) }.OfType<int>());
            }

            List<int> typeIds = [.. ids];
            var types = await uow.GetRepository<LegacyTaxonomyItem, int>().GetManyQueryable(t => typeIds.Contains(t.Id))
                .Select(t => new { t.Id, t.Title }).ToDictionaryAsync(t => t.Id, t => t.Title);
            List<long> ownerIds = [.. exams.Select(t => t.OwnerUserId).Concat(questions.Select(t => t.OwnerUserId)).OfType<long>().Distinct()];
            var owners = await uow.GetRepository<ApplicationUser>().GetManyQueryable(t => ownerIds.Contains(t.Id))
                .Select(t => new { t.Id, t.FirstName, t.LastName, t.UserName })
                .ToDictionaryAsync(t => t.Id, t => $"{t.FirstName} {t.LastName}".Trim() is { Length: > 0 } name ? name : t.UserName ?? string.Empty);
            return new(types, owners);
        }

        private static T FillExam<T>(T data, Exam exam, Names names)
            where T : ExamAdminItemDto
        {
            data.Id = exam.Id;
            data.Code = exam.Code;
            data.Title = exam.Title;
            data.Status = exam.Status;
            data.Type = exam.Type;
            data.IsDeleted = exam.IsDeleted;
            data.BoardId = exam.CoreBoardId;
            data.BoardTitle = Title(exam.CoreBoardId);
            data.GradeId = exam.CoreGradeId;
            data.GradeTitle = Title(exam.CoreGradeId);
            data.SubjectId = exam.CoreSubjectId;
            data.SubjectTitle = Title(exam.CoreSubjectId);
            data.ExamTypeId = exam.CoreExamTypeId;
            data.ExamTypeTitle = Title(exam.CoreExamTypeId);
            data.QuestionCount = exam.QuestionCount;
            data.DurationMinutes = exam.DurationMinutes;
            data.OwnerUserId = exam.OwnerUserId;
            data.OwnerName = exam.OwnerUserId is long owner ? names.Owners.GetValueOrDefault(owner) : null;
            data.ParticipantCount = exam.ParticipantCount;
            data.ViewCount = exam.ViewCount;
            data.CreationDate = exam.CreationDate;
            data.LastModifyDate = exam.LastModifyDate;
            return data;

            string? Title(int? id) => id is int value ? names.Types.GetValueOrDefault(value) : null;
        }

        private static T FillQuestion<T>(T data, ExamQuestion question, Names names)
            where T : ExamQuestionAdminItemDto
        {
            data.Id = question.Id;
            data.Code = question.Code;
            data.Excerpt = ExamQuestionText.Excerpt(question.Question, ExcerptLength);
            data.Type = ExamQuestionText.TypeTitle(question.Type);
            data.TrueAnswer = question.TrueAnswer;
            data.Status = question.Status;
            data.SubjectId = question.CoreSubjectId;
            data.SubjectTitle = question.CoreSubjectId is int subject ? names.Types.GetValueOrDefault(subject) : null;
            data.TopicTitle = FirstTopic(question.CoreTopicIds) is int topic ? names.Types.GetValueOrDefault(topic)?.Replace("**", string.Empty, StringComparison.Ordinal).Trim() : null;
            data.OwnerUserId = question.OwnerUserId;
            data.OwnerName = question.OwnerUserId is long owner ? names.Owners.GetValueOrDefault(owner) : null;
            data.UsedPublish = question.UsedPublish;
            data.CreationDate = question.CreationDate;
            data.LastModifyDate = question.LastModifyDate;
            return data;
        }

        /// <summary>Saves a new question under the next id after the highest one (gama-api's ids are kept), again if taken meanwhile.</summary>
        private async Task<long> SaveNewAsync(ExamQuestion question)
        {
            var attempt = 1;
            while (true)
            {
                var uow = UnitOfWorkProvider.Value.CreateUnitOfWork();
                var repository = uow.GetRepository<ExamQuestion>();
                question.Id = (await repository.GetManyQueryable(t => true).MaxAsync(t => (long?)t.Id) ?? 0) + 1;
                repository.Add(question);
                try
                {
                    _ = await uow.SaveChangesAsync();
                    return question.Id;
                }
                catch (DbUpdateException exc) when (attempt < SaveAttempts)
                {
                    Logger.Value.LogWarning(exc, "Question id {Id} was taken meanwhile; trying the next one", question.Id);
                    attempt++;
                }
            }
        }

        // gama-api's short public code of a question (6 letters and digits).
        private static string NewCode()
        {
            const string alphabet = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
            return string.Create(6, alphabet, (span, letters) =>
            {
                for (var i = 0; i < span.Length; i++)
                {
                    span[i] = letters[System.Security.Cryptography.RandomNumberGenerator.GetInt32(letters.Length)];
                }
            });
        }

        private static void Touch(Exam exam)
        {
            exam.LastModifyDate = DateTimeOffset.UtcNow;
            exam.SyncDate = exam.LastModifyDate.Value;
        }

        private string? ImageUrl(string? file) => string.IsNullOrWhiteSpace(file) || file == "0"
            ? null
            : fileService.Value.GetStaticFileUrl(new() { FileId = file.Trim(), ContainerType = ContainerType.ExamImage });

        private static string TextOf(string? html) => ExamQuestionText.PlainText(html);

        private static int? FirstTopic(string? topics) =>
            (topics ?? string.Empty).Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(t => int.TryParse(t, NumberStyles.None, CultureInfo.InvariantCulture, out var id) ? id : (int?)null).FirstOrDefault(t => t is > 0);

        private static int? Positive(int? value) => value is > 0 ? value : null;

        private ResultData<T>? NotNative<T>() =>
            migrationRouter.Value.GetWriteRoute(MigrationDomain.Exams) == MigrationRoute.Native
                ? null
                : new(OperationResult.NotValid)
                {
                    Errors = [new() { Message = "Exams are edited on gama-api until Migration:WriteRoutes:Exams is Native.", Reference = "notNative" }],
                };

        private static ResultData<T> Invalid<T>(string reference, string message) => new(OperationResult.NotValid) { Errors = [new() { Message = message, Reference = reference }] };

        private static ResultData<T> NotFound<T>() => new(OperationResult.NotFound) { Errors = [new() { Message = "No such exam or question.", Reference = "notFound" }] };
    }
}
