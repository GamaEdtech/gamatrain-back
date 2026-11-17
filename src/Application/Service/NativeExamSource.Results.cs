namespace GamaEdtech.Application.Service
{
    using System.Diagnostics.CodeAnalysis;
    using System.Globalization;
    using System.Linq;
    using System.Text.Json.Nodes;

    using GamaEdtech.Common.Core;
    using GamaEdtech.Common.Data;
    using GamaEdtech.Common.DataAccess.UnitOfWork;
    using GamaEdtech.Data.Dto.Exam;
    using GamaEdtech.Data.Dto.Game;
    using GamaEdtech.Data.Dto.Provider.Core;
    using GamaEdtech.Domain.Entity;
    using GamaEdtech.Domain.Entity.Identity;
    using GamaEdtech.Domain.Enumeration;

    using Microsoft.EntityFrameworkCore;
    using Microsoft.Extensions.Logging;

    using static GamaEdtech.Common.Core.Constants;

    /// <summary>
    /// The native exam taking and results (step 3 of docs/business/exams-migration.md), as gama-api's <c>Exam_lib</c> does them:
    /// start (or resume) an attempt, submit and grade it, the result page, a user's results and an exam's participants. New
    /// attempts and answer sheets get ids above gama-api's. Used once <c>Migration:Routes</c>/<c>WriteRoutes:Exams.Games</c> (and
    /// <c>Exams.ExamAnalysis</c> for the analysis) are <c>Native</c>; the exam copy then leaves attempts and answer sheets alone.
    /// </summary>
    public sealed partial class NativeExamSource
    {
        private const int SaveAttempts = 3;
        private const int DefaultResultsPerPage = 20;
        private const int MaxParticipantsPerPage = 100;

        public async Task<ResultData<JsonObject>> StartExamAsync([NotNull] ExamStartRequestDto requestDto)
        {
            try
            {
                if (requestDto.UserId is not long userId)
                {
                    return Failed<JsonObject>("accessDenied");
                }

                var uow = UnitOfWorkProvider.Value.CreateUnitOfWork();
                var exam = await uow.GetRepository<Exam>().GetManyQueryable(t => t.Id == requestDto.ExamId && !t.IsDeleted && PublicStatuses.Contains(t.Status)).FirstOrDefaultAsync();
                if (exam is null || string.IsNullOrEmpty(exam.QuestionIds))
                {
                    return Failed<JsonObject>("noResult");
                }

                var now = DateTimeOffset.UtcNow;
                if (ParticipationRefusal(exam, now, ending: false) is { } refusal)
                {
                    return Failed<JsonObject>(refusal);
                }

                var attempts = uow.GetRepository<ExamAttempt>();
                var attempt = await attempts.GetManyQueryable(t => t.ExamId == exam.Id && t.UserId == userId, tracking: true).OrderByDescending(t => t.Id).FirstOrDefaultAsync();
                if (attempt?.Status == 1)
                {
                    return new(OperationResult.Failed) { Errors = [new() { Message = "alreadyParticipated", Reference = "alreadyParticipated", Value = attempt.Id }] };
                }

                if (attempt is null)
                {
                    var coreId = await uow.GetRepository<ApplicationUser>().GetManyQueryable(t => t.Id == userId).Select(t => t.CoreId).FirstOrDefaultAsync();
                    attempt = new()
                    {
                        CoreUserId = coreId ?? 0,
                        UserId = userId,
                        ExamId = exam.Id,
                        ExamCode = exam.Code,
                        Status = 0,
                        Transfer = true,
                        CreationDate = now,
                        SyncDate = now,
                    };
                    attempt.Id = await SaveNewAsync(attempt, uow => uow.GetRepository<ExamAttempt>());
                }

                // gama-api's time left: the exam's minutes since the attempt began, and no later than the exam's end.
                var remained = ((exam.DurationMinutes ?? 0) * 60) - (long)(now - attempt.CreationDate).TotalSeconds;
                if (exam.EndDate is { } end)
                {
                    remained = Math.Min(remained, (long)(end - now).TotalSeconds);
                }

                List<long> ids = [.. ExamCopyService.QuestionIdsOf(exam.QuestionIds)];
                var questions = await uow.GetRepository<ExamQuestion>().GetManyQueryable(t => ids.Contains(t.Id)).ToDictionaryAsync(t => t.Id);
                var context = await LoadContextAsync(uow, [exam], [.. questions.Values]);
                return new(OperationResult.Succeeded)
                {
                    Data = new()
                    {
                        ["startID"] = Invariant(attempt.Id),
                        ["admin"] = false,
                        ["referee"] = false,
                        ["exam"] = TakingExam(exam),
                        ["remainedSeconds"] = remained,
                        // In exam order, without their answers.
                        ["tests"] = new JsonArray([.. ids.Where(questions.ContainsKey).Select(t => (JsonNode)TakingQuestion(questions[t], context))]),
                    },
                };
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return new(OperationResult.Failed) { Errors = [new() { Message = exc.Message }] };
            }
        }

        public async Task<ResultData<long?>> EndExamAsync([NotNull] ExamEndRequestDto requestDto)
        {
            try
            {
                if (requestDto.UserId is not long userId)
                {
                    return Failed<long?>("accessDenied");
                }

                if (Long(requestDto.Body["startID"]) is not long startId || requestDto.Body["answers"] is not JsonObject answers)
                {
                    return Failed<long?>("requiredField");
                }

                var uow = UnitOfWorkProvider.Value.CreateUnitOfWork();
                var exam = await uow.GetRepository<Exam>().GetManyQueryable(t => t.Id == requestDto.ExamId && !t.IsDeleted && PublicStatuses.Contains(t.Status), tracking: true).FirstOrDefaultAsync();
                if (exam is null || string.IsNullOrEmpty(exam.QuestionIds))
                {
                    return Failed<long?>("noResult");
                }

                if (ParticipationRefusal(exam, DateTimeOffset.UtcNow, ending: true) is { } refusal)
                {
                    return Failed<long?>(refusal);
                }

                var attempt = await uow.GetRepository<ExamAttempt>().GetManyQueryable(t => t.Id == startId && t.ExamId == exam.Id, tracking: true).FirstOrDefaultAsync();
                if (attempt is null)
                {
                    return Failed<long?>("wrongRequest");
                }

                if (attempt.UserId != userId)
                {
                    return Failed<long?>("accessDenied");
                }

                if (attempt.Status == 1 || await uow.GetRepository<ExamAnswerSheet>().AnyAsync(t => t.AttemptId == startId))
                {
                    // A submitted exam answers with its result, as gama-api's refusal does.
                    return new(OperationResult.Failed) { Data = startId, Errors = [new() { Message = "alreadyParticipated", Reference = "alreadyParticipated" }] };
                }

                List<long> ids = [.. ExamCopyService.QuestionIdsOf(exam.QuestionIds)];
                var questions = await uow.GetRepository<ExamQuestion>().GetManyQueryable(t => ids.Contains(t.Id), tracking: true).ToDictionaryAsync(t => t.Id);
                var chosen = ids.ToDictionary(t => t, t => Choice(answers[t.ToString(CultureInfo.InvariantCulture)]));
                var now = DateTimeOffset.UtcNow;

                // gama-api's normal exams keep "question:choice" for every question in exam order (no answer: empty) and grade
                // it now; an exam of written answers keeps the answers given and is scored by its teacher (0 until then).
                var enhanced = !NormalAnswerTypes.Contains(exam.Type);
                var sheet = enhanced
                    ? answers.ToJsonString()
                    : string.Join(',', ids.Select(t => $"{t}:{(chosen[t] is int c ? c.ToString(CultureInfo.InvariantCulture) : string.Empty)}"));
                var score = enhanced ? 0 : Score(exam, ids.Where(questions.ContainsKey).Select(t => (questions[t].TrueAnswer, chosen[t])));

                _ = await SaveNewAsync(new ExamAnswerSheet
                {
                    CoreUserId = attempt.CoreUserId,
                    UserId = userId,
                    ExamId = exam.Id,
                    AttemptId = attempt.Id,
                    Answers = sheet,
                    CreationDate = now,
                    SyncDate = now,
                }, uow => uow.GetRepository<ExamAnswerSheet>());

                attempt.Status = 1;
                attempt.SubmitSeconds = (int)Math.Clamp((now - attempt.CreationDate).TotalSeconds, 0, int.MaxValue);
                attempt.ResultScore = PastPaperOutput.Number(score);
                attempt.SyncDate = now;
                exam.ParticipantCount++;
                if (!enhanced)
                {
                    foreach (var id in ids.Where(questions.ContainsKey))
                    {
                        questions[id].AnswersStat = AnswersStat(questions[id].AnswersStat, chosen[id]);
                    }
                }

                _ = await uow.SaveChangesAsync();
                return new(OperationResult.Succeeded) { Data = attempt.Id };
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return new(OperationResult.Failed) { Errors = [new() { Message = exc.Message }] };
            }
        }

        public async Task<ResultData<JsonObject>> GetExamResultPageAsync([NotNull] ExamResultRequestDto requestDto)
        {
            try
            {
                var uow = UnitOfWorkProvider.Value.CreateUnitOfWork();
                var attempt = await uow.GetRepository<ExamAttempt>().GetManyQueryable(t => t.Id == requestDto.ExamId, tracking: true).FirstOrDefaultAsync();
                var exam = attempt is null ? null : await uow.GetRepository<Exam>().GetManyQueryable(t => t.Id == attempt.ExamId).FirstOrDefaultAsync();
                if (attempt is null || exam is null)
                {
                    return Failed<JsonObject>("noResult");
                }

                var examOwner = requestDto.UserId is long caller && exam.OwnerUserId == caller;
                var resultOwner = requestDto.UserId is long self && attempt.UserId == self;
                if (!examOwner && !resultOwner)
                {
                    return Failed<JsonObject>("accessDenied");
                }

                // gama-api shows a timed exam's results after its end, except to the exam's owner.
                var active = examOwner || exam.EndDate is not { } end || end <= DateTimeOffset.UtcNow;
                JsonNode answerStats = No();
                JsonNode result = No();
                if (active)
                {
                    List<long> ids = [.. ExamCopyService.QuestionIdsOf(exam.QuestionIds)];
                    var questions = await uow.GetRepository<ExamQuestion>().GetManyQueryable(t => ids.Contains(t.Id)).ToDictionaryAsync(t => t.Id);
                    var sheet = await uow.GetRepository<ExamAnswerSheet>().GetManyQueryable(t => t.AttemptId == attempt.Id).Select(t => t.Answers).FirstOrDefaultAsync();
                    var given = SheetAnswers(sheet);
                    var context = await LoadContextAsync(uow, [exam], [.. questions.Values]);
                    var items = ids.Where(questions.ContainsKey).Select(t => ResultQuestion(questions[t], given.GetValueOrDefault(t), context)).ToList();
                    var stats = AnswerStats(items, exam.ScoreType ?? 1);
                    answerStats = stats;
                    result = new JsonArray([.. items.Select(t => (JsonNode)t)]);
                    if (attempt.ResultScore is null)
                    {
                        attempt.ResultScore = PastPaperOutput.Number((decimal)stats["total"]!["percent"]!);
                        _ = await uow.SaveChangesAsync();
                    }
                }

                var student = attempt.UserId is long studentId
                    ? await uow.GetRepository<ApplicationUser>().GetManyQueryable(t => t.Id == studentId).Select(t => new { t.Id, t.FirstName, t.LastName, t.Gender, t.AvatarId }).FirstOrDefaultAsync()
                    : null;
                var limit = (exam.DurationMinutes ?? 0) * 60;
                var invalid = attempt.SubmitSeconds is int taken && taken - limit > 10 ? taken - limit : (int?)null;
                return new(OperationResult.Succeeded)
                {
                    Data = new()
                    {
                        ["user"] = new JsonObject
                        {
                            ["id"] = Invariant(student?.Id),
                            ["first_name"] = student?.FirstName,
                            ["last_name"] = student?.LastName,
                            ["sex"] = Sex(student?.Gender),
                            ["avatar"] = fileService.Value.GetStaticFileUrl(new() { FileId = student?.AvatarId, ContainerType = ContainerType.User }),
                        },
                        ["proAccess"] = new JsonObject { ["status"] = 1 },
                        ["admin"] = false,
                        ["resultOwner"] = resultOwner,
                        ["examOwner"] = examOwner,
                        ["activeResult"] = active
                            ? new JsonObject { ["status"] = 1 }
                            : new JsonObject { ["status"] = 0, ["error"] = new JsonObject { ["activeAfter"] = exam.EndDate?.UtcDateTime.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) } },
                        ["exam"] = ResultExam(exam),
                        ["isEnhanced"] = !NormalAnswerTypes.Contains(exam.Type),
                        ["price"] = Price(exam)["pdf"]!.DeepClone(),
                        ["userData"] = new JsonObject
                        {
                            ["id"] = Invariant(attempt.Id),
                            ["user_"] = Invariant(attempt.CoreUserId),
                            ["azmoon"] = Invariant(attempt.ExamId),
                            ["azmoon_code"] = attempt.ExamCode,
                            ["submit_time"] = Invariant(attempt.SubmitSeconds),
                            ["result_score"] = attempt.ResultScore,
                            ["status"] = Invariant(attempt.Status),
                            ["subdate"] = PastPaperOutput.Written(attempt.CreationDate),
                        },
                        ["invalidSeconds"] = invalid is int seconds ? seconds : No(),
                        // gama-api ranks only with its analytics store, which is not used here.
                        ["rank"] = false,
                        ["answerStats"] = answerStats,
                        ["result"] = result,
                        ["insightService"] = true,
                    },
                };
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return new(OperationResult.Failed) { Errors = [new() { Message = exc.Message }] };
            }
        }

        public async Task<ResultData<ExamResultDetailResponseDto>> GetExamResultDetailAsync([NotNull] ExamResultRequestDto requestDto)
        {
            var page = await GetExamResultPageAsync(requestDto);
            if (page.Data is null)
            {
                return new(page.OperationResult) { Errors = page.Errors };
            }

            var detail = ReadExamResultDetail(page.Data);
            return detail is null ? Failed<ExamResultDetailResponseDto>("noResult") : new(OperationResult.Succeeded) { Data = detail };
        }

        public ExamResultDetailResponseDto? ReadExamResultDetail([NotNull] JsonObject page) => ExamResultPageReader.Read(page);

        public async Task<ResultData<bool>> ValidateTestAsync([NotNull] TestTimeRequestDto requestDto)
        {
            try
            {
                var uow = UnitOfWorkProvider.Value.CreateUnitOfWork();
                var trueAnswer = await uow.GetRepository<ExamQuestion>().GetManyQueryable(t => t.Id == requestDto.TestId && t.Status == 1)
                    .Select(t => t.TrueAnswer).FirstOrDefaultAsync();
                return trueAnswer is null && !await uow.GetRepository<ExamQuestion>().AnyAsync(t => t.Id == requestDto.TestId && t.Status == 1)
                    ? Failed<bool>("TestNotFound")
                    : new(OperationResult.Succeeded) { Data = trueAnswer == requestDto.SubmissionId };
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return new(OperationResult.Failed) { Errors = [new() { Message = exc.Message }] };
            }
        }

        public async Task<ResultData<JsonObject>> GetUserResultsAsync([NotNull] ExamListRequestDto requestDto)
        {
            try
            {
                if (requestDto.UserId is not long userId)
                {
                    return Failed<JsonObject>("accessDenied");
                }

                var search = Parse(requestDto.Query ?? string.Empty);
                var perPage = Number(search, "perpage") is long p and > 0 ? (int)Math.Min(p, MaxPerPage) : DefaultResultsPerPage;
                var page = Number(search, "page") is long p2 and > 0 ? p2 : 1;
                var uow = UnitOfWorkProvider.Value.CreateUnitOfWork();
                var attempts = uow.GetRepository<ExamAttempt>().GetManyQueryable(t => t.UserId == userId);
                var total = await attempts.CountAsync();
                var list = await attempts.OrderByDescending(t => t.Id).Skip((int)((page - 1) * perPage)).Take(perPage).ToListAsync();
                List<long> examIds = [.. list.Select(t => t.ExamId).Distinct()];
                var exams = await uow.GetRepository<Exam>().GetManyQueryable(t => examIds.Contains(t.Id)).ToDictionaryAsync(t => t.Id);
                var context = await LoadContextAsync(uow, [.. exams.Values]);
                JsonArray items = [];
                foreach (var attempt in list)
                {
                    var exam = exams.GetValueOrDefault(attempt.ExamId);
                    string? Title(int? id) => id is > 0 && context.Items.TryGetValue(id.Value, out var item) ? item.Title : null;
                    items.Add(new JsonObject
                    {
                        ["id"] = Invariant(attempt.Id),
                        ["exam_id"] = Invariant(attempt.ExamId),
                        ["exam_code"] = attempt.ExamCode,
                        ["result_score"] = attempt.ResultScore,
                        ["status"] = Invariant(attempt.Status),
                        ["subdate"] = PastPaperOutput.Written(attempt.CreationDate),
                        ["base"] = Invariant(exam?.CoreGradeId),
                        ["lesson"] = Invariant(exam?.CoreSubjectId),
                        ["exam_title"] = exam?.Title,
                        ["base_title"] = Title(exam?.CoreGradeId),
                        ["lesson_title"] = Title(exam?.CoreSubjectId),
                        ["title_url"] = PastPaperOutput.TitleUrl(exam?.Title),
                    });
                }

                return new(OperationResult.Succeeded) { Data = new() { ["num"] = Invariant(total), ["list"] = items } };
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return new(OperationResult.Failed) { Errors = [new() { Message = exc.Message }] };
            }
        }

        public async Task<ResultData<JsonObject>> GetParticipantsAsync([NotNull] ExamListRequestDto requestDto)
        {
            try
            {
                var uow = UnitOfWorkProvider.Value.CreateUnitOfWork();
                var exam = requestDto.ExamId is long examId ? await uow.GetRepository<Exam>().GetManyQueryable(t => t.Id == examId).FirstOrDefaultAsync() : null;
                if (exam is null)
                {
                    return Failed<JsonObject>("requiredField");
                }

                // Only the exam's owner sees who took it (gama-api's admins see every exam's in its own panel).
                if (requestDto.UserId is not long caller || exam.OwnerUserId != caller)
                {
                    return Failed<JsonObject>("accessDenied");
                }

                var search = Parse(requestDto.Query ?? string.Empty);
                var perPage = Number(search, "perpage") is long p and > 0 ? (int)Math.Min(p, MaxParticipantsPerPage) : DefaultResultsPerPage;
                var page = Number(search, "page") is long p2 and > 0 ? p2 : 1;
                var attempts = uow.GetRepository<ExamAttempt>().GetManyQueryable(t => t.ExamId == exam.Id);
                if (search.TryGetValue("status", out var statusText) && byte.TryParse(statusText, NumberStyles.None, CultureInfo.InvariantCulture, out var status))
                {
                    attempts = attempts.Where(t => t.Status == status);
                }

                var total = await attempts.CountAsync();
                var list = await attempts.OrderByDescending(t => t.Id).Skip((int)((page - 1) * perPage)).Take(perPage).ToListAsync();
                List<long> userIds = [.. list.Select(t => t.UserId).OfType<long>().Distinct()];
                var users = await uow.GetRepository<ApplicationUser>().GetManyQueryable(t => userIds.Contains(t.Id))
                    .Select(t => new { t.Id, t.FirstName, t.LastName, t.AvatarId }).ToDictionaryAsync(t => t.Id);
                JsonArray items = [];
                foreach (var attempt in list)
                {
                    var user = attempt.UserId is long id ? users.GetValueOrDefault(id) : null;
                    items.Add(new JsonObject
                    {
                        ["id"] = Invariant(attempt.Id),
                        ["user_"] = Invariant(attempt.UserId ?? attempt.CoreUserId),
                        ["result_score"] = attempt.ResultScore,
                        ["submit_time"] = Invariant(attempt.SubmitSeconds),
                        ["status"] = Invariant(attempt.Status),
                        ["subdate"] = PastPaperOutput.Written(attempt.CreationDate),
                        ["first_name"] = user?.FirstName,
                        ["last_name"] = user?.LastName,
                        ["avatar"] = fileService.Value.GetStaticFileUrl(new() { FileId = user?.AvatarId, ContainerType = ContainerType.User }),
                    });
                }

                return new(OperationResult.Succeeded)
                {
                    Data = new()
                    {
                        ["num"] = Invariant(total),
                        ["list"] = items,
                        ["info"] = new JsonObject
                        {
                            ["exam"] = new JsonObject
                            {
                                ["id"] = Invariant(exam.Id),
                                ["title"] = exam.Title,
                                ["code"] = exam.Code,
                                ["azmoon_time"] = Invariant(exam.DurationMinutes),
                                ["status"] = Invariant(exam.Status),
                                ["type"] = Invariant(exam.Type),
                                ["isEnhanced"] = !NormalAnswerTypes.Contains(exam.Type),
                            },
                        },
                    },
                };
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return new(OperationResult.Failed) { Errors = [new() { Message = exc.Message }] };
            }
        }

        /// <summary>gama-api's <c>calcScore</c>: 3 per right answer, minus 1 per wrong one with negative marking (score type 2),
        /// as a percent of the exam's question count, rounded to one decimal. Internal for the tests.</summary>
        internal static decimal Score(Exam exam, IEnumerable<(byte? TrueAnswer, int? Chosen)> answers)
        {
            int right = 0, wrong = 0;
            foreach (var (trueAnswer, chosen) in answers)
            {
                if (chosen is null)
                {
                    continue;
                }

                if (chosen == trueAnswer)
                {
                    right++;
                }
                else
                {
                    wrong++;
                }
            }

            var sum = (right * 3) - (exam.ScoreType == 2 ? wrong : 0);
            return exam.QuestionCount > 0 ? Math.Round(sum * 100m / (exam.QuestionCount * 3), 1, MidpointRounding.AwayFromZero) : 0;
        }

        /// <summary>gama-api's <c>calcAnswerStats</c>: right, wrong and unanswered per subject and in total, with their percent.</summary>
        private static JsonObject AnswerStats(List<JsonObject> questions, byte scoreType)
        {
            Dictionary<string, (string? Title, int Num, int True, int False, int None)> lessons = [];
            int right = 0, wrong = 0, none = 0;
            foreach (var question in questions)
            {
                var lesson = (string?)question["lesson"] ?? string.Empty;
                if (!lessons.TryGetValue(lesson, out var current))
                {
                    current = ((string?)question["lesson_title"], 0, 0, 0, 0);
                }

                var trueAnswer = (string?)question["true_answer"];
                var given = (string?)question["user_answer"];
                current.Num++;
                if (!string.IsNullOrEmpty(given) && given == trueAnswer)
                {
                    right++;
                    current.True++;
                }
                else if (!string.IsNullOrEmpty(given))
                {
                    wrong++;
                    current.False++;
                }
                else
                {
                    none++;
                    current.None++;
                }

                lessons[lesson] = current;
            }

            decimal Percent(int num, int t, int f)
            {
                var penalty = scoreType == 2 ? f : 0;
                return num == 0 ? 0 : Math.Round(((t * 3) - penalty) * 100m / (num * 3), 1, MidpointRounding.AwayFromZero);
            }

            JsonObject byLesson = [];
            foreach (var (key, value) in lessons)
            {
                byLesson[key] = new JsonObject
                {
                    ["title"] = value.Title,
                    ["num"] = value.Num,
                    ["true"] = value.True,
                    ["false"] = value.False,
                    ["noanswer"] = value.None,
                    ["percent"] = Percent(value.Num, value.True, value.False),
                };
            }

            return new()
            {
                ["total"] = new JsonObject { ["num"] = questions.Count, ["true"] = right, ["false"] = wrong, ["noAnswer"] = none, ["percent"] = Percent(questions.Count, right, wrong) },
                ["lessons"] = byLesson,
            };
        }

        /// <summary>gama-api's per-question answer counts (<c>answers_stat</c>, "" for no answer) with one more answer.</summary>
        internal static string AnswersStat(string? current, int? chosen)
        {
            JsonObject stats;
            try
            {
                stats = string.IsNullOrWhiteSpace(current) ? [] : JsonNode.Parse(current) as JsonObject ?? [];
            }
            catch (System.Text.Json.JsonException)
            {
                stats = [];
            }

            if (stats.Count == 0)
            {
                foreach (var key in new[] { string.Empty, "1", "2", "3", "4" })
                {
                    stats[key] = 0;
                }
            }

            var answer = chosen?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
            stats[answer] = (Long(stats[answer]) ?? 0) + 1;
            return stats.ToJsonString();
        }

        private static Dictionary<long, int> SheetAnswers(string? sheet)
        {
            Dictionary<long, int> answers = [];
            foreach (var pair in (sheet ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var parts = pair.Split(':');
                if (parts.Length == 2 && long.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var id)
                    && int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var choice))
                {
                    answers[id] = choice;
                }
            }

            return answers;
        }

        /// <summary>The exam's refusal of a start or submit, gama-api's <c>isParticipable</c>: gone, not public, not open yet, or (to
        /// start) over.</summary>
        private static string? ParticipationRefusal(Exam exam, DateTimeOffset now, bool ending) => exam switch
        {
            { IsDeleted: true } => "alreadyDeleted",
            _ when !PublicStatuses.Contains(exam.Status) => "statusNotValid",
            { StartDate: { } start } when start > now => "participationTime",
            { EndDate: { } end } when !ending && now > end => "participationTime",
            _ => null,
        };

        private static JsonObject TakingExam(Exam exam) => new()
        {
            ["id"] = Invariant(exam.Id),
            ["code"] = exam.Code,
            ["user_"] = Invariant(exam.CoreOwnerId),
            ["base"] = Invariant(exam.CoreGradeId),
            ["lesson"] = Invariant(exam.CoreSubjectId),
            ["topics"] = exam.CoreTopicIds ?? string.Empty,
            ["type"] = Invariant(exam.Type),
            ["azmoon_type"] = Invariant(exam.CoreExamTypeId),
            ["file_pdf"] = !string.IsNullOrEmpty(exam.FilePdf),
            ["title"] = exam.Title,
            ["tests"] = exam.QuestionIds,
            ["tests_num"] = Invariant(exam.QuestionCount),
            ["start_date"] = Written(exam.StartDate),
            ["end_date"] = Written(exam.EndDate),
            ["azmoon_time"] = Invariant(exam.DurationMinutes),
            ["score_type"] = Invariant(exam.ScoreType),
            ["status"] = Invariant(exam.Status),
            ["isEnhanced"] = !NormalAnswerTypes.Contains(exam.Type),
        };

        private static JsonObject ResultExam(Exam exam)
        {
            var data = TakingExam(exam);
            _ = data.Remove("start_date");
            _ = data.Remove("azmoon_type");
            _ = data.Remove("isEnhanced");
            return data;
        }

        /// <summary>A question to answer (gama-api's <c>getItemsForParticipation</c>): no correct answer or worked answer.</summary>
        private JsonObject TakingQuestion(ExamQuestion question, Context context)
        {
            var (title, text) = ExamQuestionText.SplitTitle(question.Question);
            return new()
            {
                ["id"] = Invariant(question.Id),
                ["code"] = question.Code,
                ["user_"] = Invariant(question.CoreOwnerId),
                ["question"] = text,
                ["title"] = title,
                ["lesson"] = Invariant(question.CoreSubjectId),
                ["lesson_title"] = question.CoreSubjectId is int subject && context.Items.TryGetValue(subject, out var item) ? item.Title : null,
                ["type"] = ExamQuestionText.TypeTitle(question.Type),
                ["answer_a"] = question.AnswerA,
                ["answer_b"] = question.AnswerB,
                ["answer_c"] = question.AnswerC,
                ["answer_d"] = question.AnswerD,
                ["q_file"] = ImageUrl(question.QuestionFile) ?? string.Empty,
                ["a_file"] = ImageUrl(question.FileA) ?? string.Empty,
                ["b_file"] = ImageUrl(question.FileB) ?? string.Empty,
                ["c_file"] = ImageUrl(question.FileC) ?? string.Empty,
                ["d_file"] = ImageUrl(question.FileD) ?? string.Empty,
                ["answer_view_type"] = Invariant(question.AnswerViewType),
                ["direction"] = question.Direction,
                ["testImgAnswers"] = !string.IsNullOrEmpty(question.FileA),
            };
        }

        /// <summary>A question in the result (gama-api's <c>getItemsForResult</c>): with its correct and the student's answer.</summary>
        private JsonObject ResultQuestion(ExamQuestion question, int given, Context context)
        {
            var data = TakingQuestion(question, context);
            data["topics"] = question.CoreTopicIds ?? string.Empty;
            data["topics_title"] = FirstTopic(question.CoreTopicIds) is int topic && context.Items.TryGetValue(topic, out var item)
                ? (item.Title ?? string.Empty).Replace("**", string.Empty, StringComparison.Ordinal).Trim()
                : null;
            data["answer_full"] = question.AnswerFull ?? string.Empty;
            data["answer_full_file"] = ImageUrl(question.AnswerFullFile) ?? string.Empty;
            data["true_answer"] = Invariant(question.TrueAnswer);
            data["user_answer"] = given is >= 1 and <= 4 ? Invariant(given) : null;
            return data;
        }

        /// <summary>Saves a new row under the next id after the highest one (gama-api's ids are kept), again if taken meanwhile.</summary>
        private async Task<long> SaveNewAsync<T>(T row, Func<IUnitOfWork, Common.DataAccess.Repositories.IRepository<T, long>> repositoryOf)
            where T : class, Common.DataAccess.Entities.IEntity<T, long>
        {
            var attempt = 1;
            while (true)
            {
                var uow = UnitOfWorkProvider.Value.CreateUnitOfWork();
                var repository = repositoryOf(uow);
                row.Id = (await repository.GetManyQueryable(t => true).MaxAsync(t => (long?)t.Id) ?? 0) + 1;
                repository.Add(row);
                try
                {
                    _ = await uow.SaveChangesAsync();
                    return row.Id;
                }
                catch (DbUpdateException exc) when (attempt < SaveAttempts)
                {
                    Logger.Value.LogWarning(exc, "{Type} id {Id} was taken meanwhile; trying the next one", typeof(T).Name, row.Id);
                    attempt++;
                }
            }
        }

        private static string? Sex(GenderType? gender) => gender switch
        {
            _ when gender == GenderType.Male => "1",
            _ when gender == GenderType.Female => "2",
            _ => null,
        };

        private static int? Choice(JsonNode? value) => Long(value) is long choice and >= 1 and <= 4 ? (int)choice : null;

        private static long? Long(JsonNode? value) => value is JsonValue v
            && long.TryParse(v.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var number) ? number : null;
    }
}
