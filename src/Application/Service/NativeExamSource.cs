namespace GamaEdtech.Application.Service
{
    using System.Diagnostics.CodeAnalysis;
    using System.Globalization;
    using System.Linq;
    using System.Text.Json.Nodes;

    using GamaEdtech.Application.Interface;
    using GamaEdtech.Common.Core;
    using GamaEdtech.Common.Data;
    using GamaEdtech.Common.DataAccess.UnitOfWork;
    using GamaEdtech.Common.Service;
    using GamaEdtech.Data.Dto.ExamImport;
    using GamaEdtech.Data.Dto.Game;
    using GamaEdtech.Data.Dto.Provider.Core;
    using GamaEdtech.Domain.Entity;
    using GamaEdtech.Domain.Entity.Identity;
    using GamaEdtech.Domain.Enumeration;
    using GamaEdtech.Infrastructure.Interface;

    using Microsoft.AspNetCore.Http;
    using Microsoft.AspNetCore.WebUtilities;
    using Microsoft.EntityFrameworkCore;
    using Microsoft.Extensions.Logging;

    using static GamaEdtech.Common.Core.Constants;

    /// <summary>
    /// Exams from this backend's copy (<c>Exams</c>, <c>ExamQuestions</c>, <c>ExamAttempts</c>, the types copy, our users), answering
    /// the website's exam reads as gama-api does: the exam page, a question's page, a random practice question, the search's exam
    /// tab, and what the export and the exam page's samples read. Used only where <c>Migration:Routes:Exams.*</c> says so. The
    /// result, start/submit and the exam builder's (MCP, test maker) calls are not native yet and answer <c>notNative</c>.
    /// gama-api's Jalali dates are not sent: they mean nothing on this site. See docs/business/exams-migration.md.
    /// </summary>
    public sealed partial class NativeExamSource(Lazy<IUnitOfWorkProvider> unitOfWorkProvider, Lazy<IHttpContextAccessor> httpContextAccessor,
        Lazy<ILogger<NativeExamSource>> logger, Lazy<IFileService> fileService)
        : ServiceBase<NativeExamSource>(unitOfWorkProvider, httpContextAccessor, logger), IExamSource
    {
        // gama-api's statuses an exam is public in: 1 published, 5 cloned, 7 (an older published kind).
        private static readonly byte[] PublicStatuses = [1, 5, 7];

        // gama-api's exam types whose answers are a choice (normalAnswerTypes); any other is an "enhanced" (written) exam.
        private static readonly int[] NormalAnswerTypes = [1, 4, 5, 8, 9, 12, 13];

        // gama-api's participation price is 0 up to this many questions (staticValues freeExamTestsNum), then half price.
        private const int FreeExamQuestions = 500;
        private const int DefaultPerPage = 10;
        private const int MaxPerPage = 50;
        private const int MaxPerPageMore = 1000;

        public MigrationRoute ProviderType => MigrationRoute.Native;

        public async Task<ResultData<JsonObject>> GetExamDetailsAsync([NotNull] ExamInformationRequestDto requestDto)
        {
            try
            {
                var uow = UnitOfWorkProvider.Value.CreateUnitOfWork();
                var exam = await uow.GetRepository<Exam>().GetManyQueryable(t => t.Id == requestDto.ExamId && !t.IsDeleted).FirstOrDefaultAsync();
                var owner = exam is not null && requestDto.UserId is long caller && exam.OwnerUserId == caller;
                if (exam is null || (!PublicStatuses.Contains(exam.Status) && !owner))
                {
                    return Failed<JsonObject>("noResult");
                }

                // gama-api counts a view per fetch of a page that isn't the owner's; a shadow comparison writes nothing.
                if (!owner && !MigrationRouter.InShadowCall && await CountViewAsync(uow, exam.Id))
                {
                    exam.ViewCount++;
                }

                var context = await LoadContextAsync(uow, [exam]);
                var data = Detail(exam, context);
                var now = DateTimeOffset.UtcNow;

                // gama-api's participation window: an exam without dates is open any time ("freetime"); one with dates opens at the
                // start and closes at the end, and a day after the end it is open again (gama-api clears the dates then).
                var timed = exam.StartDate is not null && !(exam.EndDate is { } end && now > end.AddDays(1));
                var started = !timed || exam.StartDate <= now;
                var finished = !timed || exam.EndDate <= now;
                var freetime = !timed;
                if (!timed)
                {
                    data["start_date"] = null;
                    data["end_date"] = null;
                }

                var attempt = requestDto.UserId is long userId
                    ? await uow.GetRepository<ExamAttempt>().GetManyQueryable(t => t.ExamId == exam.Id && t.UserId == userId)
                        .OrderByDescending(t => t.Id).Select(t => new { t.Id, t.Status }).FirstOrDefaultAsync()
                    : null;

                data["uid"] = requestDto.UserId is long uid ? Invariant(uid) : No();
                data["participable"] = PublicStatuses.Contains(exam.Status) && (!timed || (started && !(exam.EndDate <= now)));
                data["examUserData"] = attempt is null ? No() : new JsonObject { ["id"] = Invariant(attempt.Id), ["status"] = Invariant(attempt.Status) };
                data["owner"] = owner;
                data["admin"] = false;
                data["referee"] = false;
                data["teacher"] = false;
                data["referee_access"] = false;
                data["original"] = false;
                data["pdf"] = !string.IsNullOrEmpty(exam.FilePdf);
                data["fileSize"] = 0;
                data["price"] = Price(exam);
                data["clonedItem"] = exam.Status == 5;
                data["freetime"] = freetime;
                data["started"] = started;
                data["finished"] = finished;
                data["isAfterPartyTime"] = finished;
                data["participateTime"] = freetime || (started && !finished);
                if (!owner)
                {
                    data["file_original"] = null;
                }

                return new(OperationResult.Succeeded) { Data = data };
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return new(OperationResult.Failed) { Errors = [new() { Message = exc.Message }] };
            }
        }

        public async Task<ResultData<CoreExamTestResponse>> GetPublicExamTestAsync(long testId)
        {
            try
            {
                var uow = UnitOfWorkProvider.Value.CreateUnitOfWork();
                var question = await uow.GetRepository<ExamQuestion>().GetManyQueryable(t => t.Id == testId && t.Status == 1).FirstOrDefaultAsync();
                return question is null ? Failed<CoreExamTestResponse>("permissionDenied") : new(OperationResult.Succeeded)
                {
                    Data = new()
                    {
                        Id = Invariant(question.Id),
                        Code = question.Code,
                        Question = ExamQuestionText.SplitTitle(question.Question).Question,
                        Type = ExamQuestionText.TypeTitle(question.Type),
                        OptionA = question.AnswerA,
                        OptionB = question.AnswerB,
                        OptionC = question.AnswerC,
                        OptionD = question.AnswerD,
                        QuestionFile = ImageUrl(question.QuestionFile),
                        OptionAFile = ImageUrl(question.FileA),
                        OptionBFile = ImageUrl(question.FileB),
                        OptionCFile = ImageUrl(question.FileC),
                        OptionDFile = ImageUrl(question.FileD),
                        TrueAnswer = Invariant(question.TrueAnswer),
                        AnswerFull = question.AnswerFull,
                        AnswerFullFile = ImageUrl(question.AnswerFullFile),
                        AnswerViewType = Invariant(question.AnswerViewType),
                        TestImageAnswers = !string.IsNullOrEmpty(question.FileA),
                        Paywall = new() { Locked = false },
                    },
                };
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return new(OperationResult.Failed) { Errors = [new() { Message = exc.Message }] };
            }
        }

        public async Task<ResultData<JsonObject>> GetExamTestPageAsync([NotNull] string identity, bool full)
        {
            try
            {
                var uow = UnitOfWorkProvider.Value.CreateUnitOfWork();
                var questions = uow.GetRepository<ExamQuestion>().GetManyQueryable(t => t.Status == 1);
                var text = identity.Trim();
                var question = long.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var id)
                    ? await questions.FirstOrDefaultAsync(t => t.Id == id || t.Code == text)
                    : await questions.FirstOrDefaultAsync(t => t.Code == text);
                if (question is null)
                {
                    return Failed<JsonObject>("permissionDenied");
                }

                var context = await LoadContextAsync(uow, [], [question]);
                var data = QuestionPage(question, context);
                if (full)
                {
                    data["tutorial_id"] = await FirstTutorialAsync(uow, FirstTopic(question.CoreTopicIds)) is long tutorial ? Invariant(tutorial) : No();
                }

                return new(OperationResult.Succeeded) { Data = data };
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return new(OperationResult.Failed) { Errors = [new() { Message = exc.Message }] };
            }
        }

        public async Task<ResultData<JsonObject>> GetRandomExamTestAsync([NotNull] string query)
        {
            try
            {
                var search = Parse(query);
                var uow = UnitOfWorkProvider.Value.CreateUnitOfWork();
                var questions = uow.GetRepository<ExamQuestion>().GetManyQueryable(t => t.Status == 1 && t.Type == 1 && t.Code != null);
                if (Number(search, "lesson") is long lesson)
                {
                    questions = questions.Where(t => t.CoreSubjectId == lesson);
                }

                // gama-api tries the topic first and falls back to the whole subject.
                var picked = Number(search, "topic") is long topic
                    ? await PickAsync(questions.Where(TopicMatching.TopicIn<ExamQuestion>(nameof(ExamQuestion.CoreTopicIds), [topic.ToString(CultureInfo.InvariantCulture)])))
                    : null;
                picked ??= await PickAsync(questions);
                return picked is null ? Failed<JsonObject>("noResult") : new(OperationResult.Succeeded)
                {
                    Data = new()
                    {
                        ["code"] = picked.Code,
                        ["testImgAnswers"] = !string.IsNullOrEmpty(picked.FileA),
                        // No practice limit while one is not decided (2026-11-17): never locked.
                        ["paywall"] = Paywall(),
                    },
                };
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return new(OperationResult.Failed) { Errors = [new() { Message = exc.Message }] };
            }

            static async Task<ExamQuestion?> PickAsync(IQueryable<ExamQuestion> source)
            {
                var count = await source.CountAsync();
                return count == 0 ? null : await source.OrderBy(t => t.Id).Skip(System.Security.Cryptography.RandomNumberGenerator.GetInt32(count)).FirstOrDefaultAsync();
            }
        }

        public async Task<ResultData<JsonObject>> SearchExamsAsync([NotNull] string query)
        {
            try
            {
                var search = Parse(query);
                var perPageLimit = search.ContainsKey("ineedmore") ? MaxPerPageMore : MaxPerPage;
                var perPage = Number(search, "perpage") is long p and > 0 ? (int)Math.Min(p, perPageLimit) : DefaultPerPage;
                var page = Number(search, "page") is long p2 and > 0 ? p2 : 1;

                var uow = UnitOfWorkProvider.Value.CreateUnitOfWork();
                var exams = await FilterAsync(uow, search);
                var total = await exams.CountAsync();
                // gama-api's English search always sorts by the exam's year and month, then the latest change.
                var list = await exams.OrderByDescending(t => t.EduYear).ThenByDescending(t => t.EduMonth).ThenByDescending(t => t.LastModifyDate).ThenByDescending(t => t.Id)
                    .Skip((int)((page - 1) * perPage)).Take(perPage).ToListAsync();
                var context = await LoadContextAsync(uow, list);
                return new(OperationResult.Succeeded)
                {
                    Data = new() { ["num"] = Invariant(total), ["list"] = new JsonArray([.. list.Select(t => (JsonNode)SearchItem(t, context))]) },
                };
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return new(OperationResult.Failed) { Errors = [new() { Message = exc.Message }] };
            }
        }

        public async Task<ResultData<ExamInformationResponseDto>> GetExamInformationAsync([NotNull] ExamInformationRequestDto requestDto)
        {
            try
            {
                var uow = UnitOfWorkProvider.Value.CreateUnitOfWork();
                var exam = await uow.GetRepository<Exam>().GetManyQueryable(t => t.Id == requestDto.ExamId && !t.IsDeleted && PublicStatuses.Contains(t.Status)).FirstOrDefaultAsync();
                if (exam is null)
                {
                    return Failed<ExamInformationResponseDto>("noResult");
                }

                List<long> ids = [.. ExamCopyService.QuestionIdsOf(exam.QuestionIds)];
                var questions = await uow.GetRepository<ExamQuestion>().GetManyQueryable(t => ids.Contains(t.Id)).ToDictionaryAsync(t => t.Id);
                if (ids.Any(t => !questions.ContainsKey(t)))
                {
                    Logger.Value.LogError("Exam {ExamId}: {Missing} of {Count} questions are not in the copy", exam.Id, ids.Count(t => !questions.ContainsKey(t)), ids.Count);
                    return Failed<ExamInformationResponseDto>("GeneralError");
                }

                var context = await LoadContextAsync(uow, [exam], [.. questions.Values]);
                var owner = exam.OwnerUserId is long ownerId ? context.Owners.GetValueOrDefault(ownerId) : null;
                return new(OperationResult.Succeeded)
                {
                    Data = new()
                    {
                        Exam = new()
                        {
                            Title = exam.Title,
                            TestsCount = exam.QuestionCount,
                            StartDate = Written(exam.StartDate),
                            EndDate = Written(exam.EndDate),
                            ExamTime = Invariant(exam.DurationMinutes),
                            ExamType = Invariant(exam.CoreExamTypeId),
                            Type = Invariant(exam.Type),
                            Level = exam.Level switch
                            {
                                1 => "Easy",
                                2 => "Medium",
                                3 => "Hard",
                                null or 0 => null,
                                _ => Invariant(exam.Level),
                            },
                            Author = string.Join(' ', new[] { owner?.FirstName, owner?.LastName }.Where(t => !string.IsNullOrWhiteSpace(t))),
                            AuthorCoreId = exam.CoreOwnerId,
                            Topics = [.. TopicIds(exam.CoreTopicIds).Select(t => context.Items.GetValueOrDefault(t)).OfType<LegacyTaxonomyItem>()
                                .OrderBy(t => t.ListOrder).Select(t => (t.Title ?? string.Empty).Replace("**", string.Empty, StringComparison.Ordinal).Trim())
                                .Where(t => t.Length > 0)],
                        },
                        Tests = [.. ids.Select(t => questions[t]).Select(t => new ExamInformationResponseDto.TestDto
                        {
                            Question = ExamQuestionText.SplitTitle(t.Question).Question,
                            QuestionFile = ImageUrl(t.QuestionFile),
                            OptionA = t.AnswerA,
                            OptionAFile = ImageUrl(t.FileA),
                            OptionB = t.AnswerB,
                            OptionBFile = ImageUrl(t.FileB),
                            OptionC = t.AnswerC,
                            OptionCFile = ImageUrl(t.FileC),
                            OptionD = t.AnswerD,
                            OptionDFile = ImageUrl(t.FileD),
                            CorrectOption = t.TrueAnswer switch
                            {
                                1 => 'A',
                                2 => 'B',
                                3 => 'C',
                                4 => 'D',
                                _ => null,
                            },
                            AnswerHtml = string.IsNullOrWhiteSpace(t.AnswerFull) ? null : t.AnswerFull,
                            AnswerFile = ImageUrl(t.AnswerFullFile),
                            QuestionType = ExamQuestionText.TypeTitle(t.Type),
                            AnswerViewType = Invariant(t.AnswerViewType),
                            TestImageAnswers = !string.IsNullOrEmpty(t.FileA),
                        })],
                    },
                };
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return new(OperationResult.Failed) { Errors = [new() { Message = exc.Message }] };
            }
        }

        public async Task<ResultData<int>> GetExamQuestionCountAsync([NotNull] ExamInformationRequestDto requestDto)
        {
            try
            {
                var uow = UnitOfWorkProvider.Value.CreateUnitOfWork();
                var questionIds = await uow.GetRepository<Exam>().GetManyQueryable(t => t.Id == requestDto.ExamId && !t.IsDeleted && PublicStatuses.Contains(t.Status))
                    .Select(t => t.QuestionIds).FirstOrDefaultAsync();
                return questionIds is null && !await uow.GetRepository<Exam>().AnyAsync(t => t.Id == requestDto.ExamId && !t.IsDeleted && PublicStatuses.Contains(t.Status))
                    ? Failed<int>("noResult")
                    : new(OperationResult.Succeeded) { Data = ExamCopyService.QuestionIdsOf(questionIds).Count() };
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return new(OperationResult.Failed) { Errors = [new() { Message = exc.Message }] };
            }
        }

        // Not native yet: the exam builder's calls with a gama-api token (the switch), and gama-api's older result summary (unused).
        public Task<ResultData<ExamResultResponseDto>> GetExamResultAsync([NotNull] ExamResultRequestDto requestDto) => NotNativeAsync<ExamResultResponseDto>();

        public Task<ResultData<long?>> GetCurrentExamIdAsync([NotNull] ExamImportRequestDto requestDto) => NotNativeAsync<long?>();

        public Task<ResultData<ExamImportDraftDto>> GetExamAsync([NotNull] ExamImportRequestDto requestDto) => NotNativeAsync<ExamImportDraftDto>();

        public Task<ResultData<ListDataSource<ExamImportDraftDto>>> GetExamsAsync([NotNull] ExamImportExamsRequestDto requestDto) => NotNativeAsync<ListDataSource<ExamImportDraftDto>>();

        public Task<ResultData<IEnumerable<ExamImportDraftQuestionDto>>> GetExamQuestionsAsync([NotNull] ExamImportRequestDto requestDto) => NotNativeAsync<IEnumerable<ExamImportDraftQuestionDto>>();

        public Task<ResultData<IEnumerable<long>>> GetExamTestIdsAsync([NotNull] ExamImportRequestDto requestDto) => NotNativeAsync<IEnumerable<long>>();

        public Task<ResultData<string>> UploadFileAsync([NotNull] ExamImportUploadRequestDto requestDto) => NotNativeAsync<string>();

        public Task<ResultData<long>> SaveExamTestAsync([NotNull] SaveExamTestRequestDto requestDto) => NotNativeAsync<long>();

        public Task<ResultData<Common.Data.Void>> DeleteExamTestAsync([NotNull] ExamImportRequestDto requestDto) => NotNativeAsync<Common.Data.Void>();

        public Task<ResultData<long>> SaveExamAsync([NotNull] SaveExamRequestDto requestDto) => NotNativeAsync<long>();

        public Task<ResultData<Common.Data.Void>> SetExamTestsAsync([NotNull] SetExamTestsRequestDto requestDto) => NotNativeAsync<Common.Data.Void>();

        public Task<ResultData<Common.Data.Void>> PublishExamAsync([NotNull] ExamImportRequestDto requestDto) => NotNativeAsync<Common.Data.Void>();

        public Task<ResultData<Common.Data.Void>> DeleteExamAsync([NotNull] ExamImportRequestDto requestDto) => NotNativeAsync<Common.Data.Void>();

        /// <summary>gama-api's participation and PDF prices (<c>price</c>): participation is free up to 500 questions, then half a
        /// unit each; the PDF a unit per question; both rounded to the nearest 10 (gama-api's English <c>standardPrice</c>).</summary>
        internal static JsonObject Price(Exam exam)
        {
            var participation = PublicStatuses[..2].Contains(exam.Status) && exam.QuestionCount > FreeExamQuestions
                ? RoundTen(exam.QuestionCount * 0.5m)
                : 0;
            return new()
            {
                ["participation"] = new JsonObject { ["price"] = participation },
                ["pdf"] = new JsonObject { ["price"] = RoundTen(exam.QuestionCount) },
            };

            static long RoundTen(decimal value) => (long)Math.Round(value / 10, MidpointRounding.AwayFromZero) * 10;
        }

        // gama-api sends false for a missing value in these fields.
        private static JsonValue No() => JsonValue.Create(false);

        private static JsonObject Paywall() => new() { ["locked"] = false, ["limit"] = false, ["usage"] = false, ["remained"] = 0 };

        /// <summary>The exam page's fields (gama-api's <c>exams/{id}</c> with <c>outputBuilder('detail')</c>), before the caller's own.</summary>
        private JsonObject Detail(Exam exam, Context context)
        {
            var data = new JsonObject
            {
                ["id"] = Invariant(exam.Id),
                ["clone_id"] = Invariant(exam.CloneId ?? 0),
                ["user_"] = Invariant(exam.CoreOwnerId),
                ["referee"] = false,
                ["user_type"] = Invariant(exam.UserType),
                ["section"] = Invariant(exam.CoreBoardId),
                ["base"] = Invariant(exam.CoreGradeId),
                ["course"] = Invariant(exam.CoreCourseId),
                ["lesson"] = Invariant(exam.CoreSubjectId),
                ["topics"] = exam.CoreTopicIds ?? string.Empty,
                ["type"] = Invariant(exam.Type),
                ["azmoon_type"] = Invariant(exam.CoreExamTypeId),
                ["holding_level"] = Invariant(exam.HoldingLevel),
                ["state"] = exam.State,
                ["area"] = Invariant(exam.Area),
                ["school"] = Invariant(exam.School),
                ["level"] = Invariant(exam.Level ?? 0),
                ["title"] = exam.Title,
                ["tests"] = new JsonArray([.. ExamCopyService.QuestionIdsOf(exam.QuestionIds).Select(t => (JsonNode)Invariant(t)!)]),
                ["tests_num"] = Invariant(exam.QuestionCount),
                ["tests_combination"] = Invariant(exam.QuestionsCombination),
                ["start_date"] = Written(exam.StartDate),
                ["end_date"] = Written(exam.EndDate),
                ["azmoon_time"] = Invariant(exam.DurationMinutes),
                ["code"] = exam.Code,
                ["edu_year"] = Invariant(exam.EduYear),
                ["edu_month"] = Invariant(exam.EduMonth),
                ["file_original"] = exam.FileOriginal,
                ["page_break_points"] = exam.PageBreakPoints,
                ["participants_num"] = Invariant(exam.ParticipantCount),
                ["status"] = Invariant(exam.Status),
                ["msg"] = exam.Message,
                ["review"] = Invariant(exam.Review ?? 0),
                ["metadata"] = Metadata(exam.Metadata),
                ["views"] = Invariant(exam.ViewCount),
                ["downloads"] = Invariant(exam.DownloadCount),
                ["subdate"] = PastPaperOutput.Written(exam.CreationDate),
                ["up_date"] = Written(exam.LastModifyDate),
                ["user_id"] = Invariant(exam.CoreOwnerId),
                ["bookmark"] = false,
            };
            AddOwner(data, exam, context);
            AddTitles(data, exam, context);
            data["participable"] = false;
            data["isEnhanced"] = !NormalAnswerTypes.Contains(exam.Type);
            data["title_url"] = PastPaperOutput.TitleUrl(exam.Title);
            data["negative_point"] = exam.ScoreType == 2;
            data["edu_month_title"] = PastPaperOutput.MonthTitle(exam.EduMonth);
            data["thumb_pic_url"] = ThumbnailUrl(exam.Thumbnail);

            // gama-api merges the metadata's keys (e.g. the paper it was built from) into the exam.
            if (data["metadata"] is JsonObject metadata)
            {
                foreach (var (key, value) in metadata)
                {
                    if (!data.ContainsKey(key))
                    {
                        data[key] = value?.DeepClone();
                    }
                }
            }

            return data;
        }

        /// <summary>An exam in the search's exam tab (gama-api's search item).</summary>
        private JsonObject SearchItem(Exam exam, Context context)
        {
            var data = new JsonObject
            {
                ["id"] = Invariant(exam.Id),
                ["code"] = exam.Code,
                ["user_id"] = Invariant(exam.CoreOwnerId),
                ["section"] = Invariant(exam.CoreBoardId),
                ["course"] = Invariant(exam.CoreCourseId),
                ["base"] = Invariant(exam.CoreGradeId),
                ["lesson"] = Invariant(exam.CoreSubjectId),
                ["azmoon_type"] = Invariant(exam.CoreExamTypeId),
                ["topics"] = exam.CoreTopicIds ?? string.Empty,
                ["level"] = Invariant(exam.Level ?? 0),
                ["title"] = exam.Title,
                ["tests_num"] = Invariant(exam.QuestionCount),
                ["start_date"] = Written(exam.StartDate),
                ["end_date"] = Written(exam.EndDate),
                ["azmoon_time"] = Invariant(exam.DurationMinutes),
                ["participants_num"] = Invariant(exam.ParticipantCount),
                ["status"] = Invariant(exam.Status),
                ["subdate"] = PastPaperOutput.Written(exam.CreationDate),
                ["up_date"] = Written(exam.LastModifyDate),
            };
            AddOwner(data, exam, context);
            AddTitles(data, exam, context);
            data["title_url"] = PastPaperOutput.TitleUrl(exam.Title);
            data["identity"] = exam.OwnerUserId is long id && context.Owners.GetValueOrDefault(id)?.Handle is { Length: > 0 } handle ? $"@{handle}" : null;
            return data;
        }

        /// <summary>A question's public page (gama-api's <c>examTests/{code}</c> with <c>outputBuilder('detail')</c>).</summary>
        private JsonObject QuestionPage(ExamQuestion question, Context context)
        {
            var (title, text) = ExamQuestionText.SplitTitle(question.Question);
            var topic = FirstTopic(question.CoreTopicIds);
            var owner = question.OwnerUserId is long ownerId ? context.Owners.GetValueOrDefault(ownerId) : null;
            var data = new JsonObject
            {
                ["id"] = Invariant(question.Id),
                ["user_"] = Invariant(question.CoreOwnerId),
                ["code"] = question.Code,
                ["section"] = Invariant(question.CoreBoardId ?? 0),
                ["base"] = Invariant(question.CoreGradeId ?? 0),
                ["course"] = Invariant(question.CoreCourseId ?? 0),
                ["lesson"] = Invariant(question.CoreSubjectId ?? 0),
                ["direction"] = question.Direction,
                ["topic"] = Invariant(topic),
                ["level"] = Invariant(question.Level ?? 0),
                ["type"] = ExamQuestionText.TypeTitle(question.Type),
                ["enhanced_title"] = question.EnhancedTitle,
                ["question"] = text,
                ["q_file"] = ImageUrl(question.QuestionFile) ?? string.Empty,
                ["answer_a"] = question.AnswerA,
                ["answer_b"] = question.AnswerB,
                ["answer_c"] = question.AnswerC,
                ["answer_d"] = question.AnswerD,
                ["answer_full"] = question.AnswerFull ?? string.Empty,
                ["answer_view_type"] = Invariant(question.AnswerViewType),
                ["a_file"] = ImageUrl(question.FileA) ?? string.Empty,
                ["b_file"] = ImageUrl(question.FileB) ?? string.Empty,
                ["c_file"] = ImageUrl(question.FileC) ?? string.Empty,
                ["d_file"] = ImageUrl(question.FileD) ?? string.Empty,
                ["answer_full_file"] = ImageUrl(question.AnswerFullFile) ?? string.Empty,
                ["true_answer"] = Invariant(question.TrueAnswer),
                ["resource"] = question.Resource,
                ["answers_stat"] = Metadata(question.AnswersStat),
                ["subdate"] = PastPaperOutput.Written(question.CreationDate),
                ["up_date"] = question.LastModifyDate is { } changed && changed != question.CreationDate ? PastPaperOutput.Written(changed) : null,
                ["hasVideo"] = false,
                ["first_name"] = owner?.FirstName,
                ["last_name"] = owner?.LastName,
                ["avatar"] = fileService.Value.GetStaticFileUrl(new() { FileId = owner?.AvatarId, ContainerType = ContainerType.User }),
                ["owner"] = false,
            };
            string? Title(int? id) => id is > 0 && context.Items.TryGetValue(id.Value, out var item) ? item.Title : null;
            data["section_title"] = Title(question.CoreBoardId);
            data["base_title"] = Title(question.CoreGradeId);
            data["lesson_title"] = Title(question.CoreSubjectId);
            data["topic_title"] = Title(topic)?.Replace("**", string.Empty, StringComparison.Ordinal).Trim();
            data["title"] = title;
            data["testImgAnswers"] = !string.IsNullOrEmpty(question.FileA);
            var plain = ExamQuestionText.PlainText(text);
            var shortText = plain.Length > 160 ? plain[..160].TrimEnd() + "…" : plain;
            var urlText = plain.Length > 100 ? plain[..100] : plain;
            var enhanced = !string.IsNullOrWhiteSpace(question.EnhancedTitle);
            data["pageTitle"] = enhanced ? question.EnhancedTitle : $"{shortText} | {data["lesson_title"]} {data["base_title"]}";
            data["pageDescription"] = enhanced ? question.EnhancedTitle : plain;
            data["title_url"] = PastPaperOutput.TitleUrl(enhanced ? question.EnhancedTitle : urlText);
            data["paywall"] = Paywall();
            return data;
        }

        /// <summary>gama-api's search filters for exams: published, not deleted; a topic (with its linked topics), else the subject
        /// or the board, course and grade (each with the topics imported into it); the title, exam type, kind, date status and a
        /// question it holds.</summary>
        private static async Task<IQueryable<Exam>> FilterAsync(IUnitOfWork uow, Dictionary<string, string?> search)
        {
            var query = uow.GetRepository<Exam>().GetManyQueryable(t => t.Status == 1 && !t.IsDeleted);
            if (search.GetValueOrDefault("title") is { Length: > 0 } title)
            {
                var text = title.Trim();
                query = query.Where(t => t.Title!.Contains(text));
            }

            if ((Number(search, "exam_type") ?? Number(search, "azmoon_type")) is long examType)
            {
                query = examType == -1 ? query.Where(t => t.CoreExamTypeId == null) : query.Where(t => t.CoreExamTypeId == examType);
            }

            if (Number(search, "examOpen") is long open)
            {
                query = open == 1 ? query.Where(t => t.Type == 1) : query.Where(t => t.Type != 1);
            }

            if (Number(search, "level") is long level)
            {
                query = query.Where(t => t.Level == level);
            }

            if (Number(search, "test_id") is long testId)
            {
                var key = testId.ToString(CultureInfo.InvariantCulture);
                query = query.Where(t => t.QuestionIds == key || t.QuestionIds!.StartsWith(key + ",") || t.QuestionIds.EndsWith("," + key) || t.QuestionIds.Contains("," + key + ","));
            }

            var now = DateTimeOffset.UtcNow;
            query = Number(search, "date_status") switch
            {
                1 => query.Where(t => t.StartDate == null || (t.StartDate <= now && t.EndDate > now)),
                2 => query.Where(t => t.StartDate != null && t.StartDate > now),
                3 => query.Where(t => t.StartDate != null && t.EndDate < now),
                _ => query,
            };

            if (Number(search, "topic") is long topicId)
            {
                List<long> topics = [topicId, .. await TopicMatching.LinkedAsync(uow, topicId)];
                return query.Where(TopicMatching.TopicIn<Exam>(nameof(Exam.CoreTopicIds), [.. topics.Select(t => t.ToString(CultureInfo.InvariantCulture))]));
            }

            var filterId = Number(search, "lesson") ?? Number(search, "base") ?? Number(search, "section");
            var imported = filterId is long id ? await TopicMatching.ImportedTopicsAsync(uow, id) : [];
            var own = query;
            if (Number(search, "lesson") is long lesson)
            {
                own = own.Where(t => t.CoreSubjectId == lesson);
            }
            else
            {
                if (Number(search, "section") is long section)
                {
                    own = own.Where(t => t.CoreBoardId == section);
                }

                if (Number(search, "course") is long course)
                {
                    own = own.Where(t => t.CoreCourseId == course);
                }

                if (Number(search, "base") is long grade)
                {
                    own = own.Where(t => t.CoreGradeId == grade);
                }
            }

            return imported.Count == 0
                ? own
                : own.Union(query.Where(TopicMatching.TopicIn<Exam>(nameof(Exam.CoreTopicIds), [.. imported.Select(t => t.ToString(CultureInfo.InvariantCulture))])));
        }

        private void AddOwner(JsonObject data, Exam exam, Context context)
        {
            var owner = exam.OwnerUserId is long id ? context.Owners.GetValueOrDefault(id) : null;
            string? sex = null;
            if (owner?.Gender == GenderType.Male)
            {
                sex = "1";
            }
            else if (owner?.Gender == GenderType.Female)
            {
                sex = "2";
            }

            data["username"] = "0";
            data["first_name"] = owner?.FirstName;
            data["last_name"] = owner?.LastName;
            data["sex"] = sex;
            // Our user's avatar; none means null, and the website shows its own default picture.
            data["avatar"] = fileService.Value.GetStaticFileUrl(new() { FileId = owner?.AvatarId, ContainerType = ContainerType.User });
        }

        private void AddTitles(JsonObject data, Exam exam, Context context)
        {
            string? Title(int? id) => id is > 0 && context.Items.TryGetValue(id.Value, out var item) ? item.Title : null;
            data["section_title"] = Title(exam.CoreBoardId);
            data["base_title"] = Title(exam.CoreGradeId);
            data["course_title"] = Title(exam.CoreCourseId);
            data["lesson_title"] = Title(exam.CoreSubjectId);
            data["lesson_pic"] = exam.CoreSubjectId is int subject && context.Items.TryGetValue(subject, out var item) && !string.IsNullOrWhiteSpace(item.Picture)
                ? fileService.Value.GetStaticFileUrl(new() { FileId = item.Picture, ContainerType = ContainerType.SubjectPicture })
                : null;
            data["azmoon_type_title"] = Title(exam.CoreExamTypeId);
        }

        internal sealed record Context(IReadOnlyDictionary<int, LegacyTaxonomyItem> Items, IReadOnlyDictionary<long, OwnerInfo> Owners);

        internal sealed record OwnerInfo(string? FirstName, string? LastName, string? AvatarId, GenderType? Gender, string? Handle);

        private static async Task<Context> LoadContextAsync(IUnitOfWork uow, IReadOnlyCollection<Exam> exams, IReadOnlyCollection<ExamQuestion>? questions = null)
        {
            HashSet<int> ids = [];
            foreach (var exam in exams)
            {
                ids.UnionWith(new int?[] { exam.CoreBoardId, exam.CoreGradeId, exam.CoreCourseId, exam.CoreSubjectId, exam.CoreExamTypeId }.OfType<int>());
                ids.UnionWith(TopicIds(exam.CoreTopicIds));
            }

            foreach (var question in questions ?? [])
            {
                ids.UnionWith(new int?[] { question.CoreBoardId, question.CoreGradeId, question.CoreSubjectId, FirstTopic(question.CoreTopicIds) }.OfType<int>());
            }

            List<int> itemIds = [.. ids];
            var items = await uow.GetRepository<LegacyTaxonomyItem, int>().GetManyQueryable(t => itemIds.Contains(t.Id)).ToDictionaryAsync(t => t.Id);

            List<long> ownerIds = [.. exams.Select(t => t.OwnerUserId).Concat((questions ?? []).Select(t => t.OwnerUserId)).OfType<long>().Distinct()];
            var owners = await uow.GetRepository<ApplicationUser>().GetManyQueryable(t => ownerIds.Contains(t.Id))
                .Select(t => new { t.Id, t.FirstName, t.LastName, t.AvatarId, t.Gender, t.Handle })
                .ToDictionaryAsync(t => t.Id, t => new OwnerInfo(t.FirstName, t.LastName, t.AvatarId, t.Gender, t.Handle));
            return new(items, owners);
        }

        /// <summary>The first published tutorial of a topic by its order (gama-api's <c>tutorial_id</c> on a full question page).</summary>
        private static async Task<long?> FirstTutorialAsync(IUnitOfWork uow, int? topic) => topic is int id
            ? await uow.GetRepository<Tutorial>().GetManyQueryable(t => t.Status == TutorialStatus.Confirmed)
                .Where(TopicMatching.TopicIn<Tutorial>(nameof(Tutorial.CoreTopicIds), [id.ToString(CultureInfo.InvariantCulture)]))
                .OrderBy(t => t.TopicOrder).ThenBy(t => t.Id).Select(t => (long?)t.Id).FirstOrDefaultAsync()
            : null;

        private static async Task<bool> CountViewAsync(IUnitOfWork uow, long id)
        {
            try
            {
                _ = await uow.GetRepository<Exam>().GetManyQueryable(t => t.Id == id).ExecuteUpdateAsync(t => t.SetProperty(p => p.ViewCount, p => p.ViewCount + 1));
                return true;
            }
            catch (InvalidOperationException)
            {
                // The test database can't run a bulk update; a missed view never fails the page.
                return false;
            }
        }

        private string? ImageUrl(string? file) => string.IsNullOrWhiteSpace(file) || file == "0"
            ? null
            : fileService.Value.GetStaticFileUrl(new() { FileId = file.Trim(), ContainerType = ContainerType.ExamImage });

        private string? ThumbnailUrl(string? thumbnail) => string.IsNullOrWhiteSpace(thumbnail)
            ? null
            : fileService.Value.GetStaticFileUrl(new() { FileId = thumbnail.Replace("uploads/thumbs/", string.Empty, StringComparison.Ordinal), ContainerType = ContainerType.Thumbnail });

        private static JsonNode? Metadata(string? json)
        {
            try
            {
                return string.IsNullOrWhiteSpace(json) ? null : JsonNode.Parse(json);
            }
            catch (System.Text.Json.JsonException)
            {
                return json;
            }
        }

        private static IEnumerable<int> TopicIds(string? topics) =>
            (topics ?? string.Empty).Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(t => int.TryParse(t, NumberStyles.None, CultureInfo.InvariantCulture, out var id) ? id : 0).Where(t => t > 0);

        private static int? FirstTopic(string? topics) => TopicIds(topics).Select(t => (int?)t).FirstOrDefault();

        private static Dictionary<string, string?> Parse(string query) =>
            QueryHelpers.ParseQuery(query.StartsWith('?') ? query : "?" + query).ToDictionary(t => t.Key, t => (string?)t.Value.ToString(), StringComparer.Ordinal);

        private static long? Number(Dictionary<string, string?> search, string key) =>
            search.TryGetValue(key, out var value) && long.TryParse(value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var number) && number != 0
                ? number
                : null;

        private static string? Written(DateTimeOffset? date) => date is { } value ? PastPaperOutput.Written(value) : null;

        private static string? Invariant(long? value) => value?.ToString(CultureInfo.InvariantCulture);

        private static ResultData<T> Failed<T>(string error) => new(OperationResult.Failed) { Errors = [new() { Message = error, Reference = error }] };

        private static Task<ResultData<T>> NotNativeAsync<T>() => Task.FromResult(new ResultData<T>(OperationResult.NotValid)
        {
            Errors = [new() { Message = "Not served from this backend's copy yet: keep this call's route on Legacy.", Reference = "notNative" }],
        });
    }
}
