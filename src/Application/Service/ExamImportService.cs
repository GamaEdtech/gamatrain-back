namespace GamaEdtech.Application.Service
{
    using System;
    using System.Diagnostics.CodeAnalysis;
    using System.Globalization;
    using System.Security.Cryptography;
    using System.Text.Json;
    using System.Text.Json.Serialization;

    using GamaEdtech.Application.Interface;
    using GamaEdtech.Common.Core;
    using GamaEdtech.Common.Data;
    using GamaEdtech.Common.DataAccess.UnitOfWork;
    using GamaEdtech.Common.Service;
    using GamaEdtech.Data.Dto.ExamImport;
    using GamaEdtech.Domain.Entity;
    using GamaEdtech.Domain.Specification;
    using GamaEdtech.Infrastructure.Interface;

    using Microsoft.AspNetCore.DataProtection;
    using Microsoft.AspNetCore.Http;
    using Microsoft.EntityFrameworkCore;
    using Microsoft.Extensions.Configuration;
    using Microsoft.Extensions.Localization;
    using Microsoft.Extensions.Logging;

    using static GamaEdtech.Application.Service.ExamImportRules;
    using static GamaEdtech.Common.Core.Constants;

    using Void = Common.Data.Void;

    public sealed class ExamImportService(Lazy<IUnitOfWorkProvider> unitOfWorkProvider, Lazy<IHttpContextAccessor> httpContextAccessor,
        Lazy<IStringLocalizer<ExamImportService>> localizer, Lazy<ILogger<ExamImportService>> logger, Lazy<ICoreProvider> coreProvider,
        Lazy<IConfiguration> configuration, Lazy<IDataProtectionProvider> dataProtectionProvider)
        : LocalizableServiceBase<ExamImportService>(unitOfWorkProvider, httpContextAccessor, localizer, logger), IExamImportService
    {
        private const string Idle = "idle";
        private const string Running = "running";
        private const string WaitingRateLimit = "waitingRateLimit";
        private const string Interrupted = "interrupted";
        private const string DraftReady = "draftReady";
        private const string Published = "published";
        private const string FailedPhase = "failed";
        private const string Cancelled = "cancelled";

        /// <summary>The <see cref="ExamImportUploadDto.Failed"/> key of a failure that stopped the whole upload.</summary>
        private const string JobKey = "_job";

        private const string TokenPurpose = "GamaEdtech.ExamImport.GamaToken";
        private const string PreviewLinkPurpose = "GamaEdtech.ExamImport.PreviewLink";
        private const string UploadLinkPurpose = "GamaEdtech.ExamImport.FigureUploadLink";
        private const string UploadRunningMessage = "An upload is running. Wait until submission_status reports it finished.";

        private const int MaxFigureBytes = 5 * 1024 * 1024;
        private const int MaxFigures = 300;
        private const int MaxQuestions = 300;

        /// <summary>gama-api allows one new question per user every 20 seconds and one new exam every 60.</summary>
        private static readonly TimeSpan QuestionInterval = TimeSpan.FromSeconds(21);
        private static readonly TimeSpan ExamInterval = TimeSpan.FromSeconds(61);

        /// <summary>A running upload saves its progress at least every minute; older than this, it died (a restart).</summary>
        private static readonly TimeSpan StaleAfter = TimeSpan.FromMinutes(5);
        private static readonly TimeSpan PreviewLinkLifetime = TimeSpan.FromHours(24);
        private static readonly TimeSpan UploadLinkLifetime = TimeSpan.FromHours(2);

        /// <summary>gama-api errors that no retry fixes: they stop the whole upload.</summary>
        private static readonly HashSet<string> FatalCodes = new(StringComparer.Ordinal) { "notCompletedInfo", "permissionDenied", "serviceIsDeactive" };

        private static readonly Dictionary<string, (string Type, string? ParentFilter, string? Parent)> OptionKinds = new(StringComparer.Ordinal)
        {
            ["board"] = ("section", null, null),
            ["grade"] = ("base", "section_id", "board"),
            ["course"] = ("course", "section_id", "board"),
            ["subject"] = ("lesson", "base_id", "grade"),
            ["topic"] = ("topic", "lesson_id", "subject"),
            ["paper"] = ("exam_type", null, null),
        };

        private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };

        public async Task<ResultData<ExamImportStatusDto>> GetStatusAsync(long userId)
        {
            try
            {
                return new(OperationResult.Succeeded) { Data = await BuildStatusAsync(await LoadAsync(userId, false)) };
            }
            catch (Exception exc)
            {
                return Failure<ExamImportStatusDto>(exc);
            }
        }

        public async Task<ResultData<Void>> StartNewAsync(long userId)
        {
            try
            {
                var state = await LoadAsync(userId, false);
                if (state is not null && IsRunning(state.Upload))
                {
                    return Invalid<Void>(UploadRunningMessage, "uploadRunning");
                }

                // The figures go with it (cascade).
                _ = await UnitOfWorkProvider.Value.CreateUnitOfWork().GetRepository<ExamImport>()
                    .GetManyQueryable(new UserIdEqualsSpecification<ExamImport, long>(userId)).ExecuteDeleteAsync();
                return new(OperationResult.Succeeded) { Data = new() };
            }
            catch (Exception exc)
            {
                return Failure<Void>(exc);
            }
        }

        public async Task<ResultData<IEnumerable<ExamImportOptionDto>>> GetOptionsAsync(long userId, [NotNull] string token, [NotNull] string kind, int? parentId, string? search)
        {
            try
            {
                if (!OptionKinds.TryGetValue(kind, out var option))
                {
                    return Invalid<IEnumerable<ExamImportOptionDto>>("kind must be board, grade, course, subject, topic or paper.");
                }

                if (option.ParentFilter is not null && parentId is not > 0)
                {
                    return Invalid<IEnumerable<ExamImportOptionDto>>($"Listing {kind}s needs parentId (the {option.Parent} id).");
                }

                Dictionary<string, string?> filters = new(StringComparer.Ordinal);
                if (option.ParentFilter is not null)
                {
                    filters[option.ParentFilter] = parentId!.Value.ToString(CultureInfo.InvariantCulture);
                }

                if (kind == "subject" && (await LoadAsync(userId, false))?.Details.CourseId is { } courseId)
                {
                    filters["course_id"] = courseId.ToString(CultureInfo.InvariantCulture);
                }

                var result = await coreProvider.Value.GetTypesAsync(token, option.Type, filters);
                return result.OperationResult is OperationResult.Succeeded
                    ? new(OperationResult.Succeeded) { Data = [.. Rank(result.Data ?? [], search).Take(300)] }
                    : CoreFailure<IEnumerable<ExamImportOptionDto>>(result.Errors);
            }
            catch (Exception exc)
            {
                return Failure<IEnumerable<ExamImportOptionDto>>(exc);
            }
        }

        public async Task<ResultData<ExamImportStatusDto>> SetDetailsAsync(long userId, [NotNull] string token, [NotNull] ExamImportDetailsRequestDto requestDto)
        {
            try
            {
                var state = await LoadOrCreateAsync(userId);
                if (IsRunning(state.Upload))
                {
                    return Invalid<ExamImportStatusDto>(UploadRunningMessage, "uploadRunning");
                }

                var details = state.Details;
                if (requestDto.BoardId is { } boardId)
                {
                    var boards = await coreProvider.Value.GetTypesAsync(token, "section");
                    if (boards.OperationResult is not OperationResult.Succeeded)
                    {
                        return CoreFailure<ExamImportStatusDto>(boards.Errors);
                    }

                    if (boards.Data?.FirstOrDefault(t => t.Id == boardId) is not { } board)
                    {
                        return Invalid<ExamImportStatusDto>($"Board {boardId} does not exist. Use list_options(kind=board).");
                    }

                    if (boardId != details.BoardId)
                    {
                        // Another board: its grade, course, subject, topics and past paper no longer apply.
                        details = new()
                        {
                            PaperId = details.PaperId,
                            Paper = details.Paper,
                            Component = details.Component,
                            SessionMonth = details.SessionMonth,
                            Year = details.Year,
                            DurationMinutes = details.DurationMinutes,
                            Title = details.Title,
                            Level = details.Level,
                            NegativeMarking = details.NegativeMarking,
                        };
                    }

                    details.BoardId = boardId;
                    details.Board = board.Title;
                }

                if (requestDto.GradeId is { } gradeId)
                {
                    if (details.BoardId is null)
                    {
                        return Invalid<ExamImportStatusDto>("Set the board first.");
                    }

                    var grades = await coreProvider.Value.GetTypesAsync(token, "base", Filter("section_id", details.BoardId));
                    if (grades.OperationResult is not OperationResult.Succeeded)
                    {
                        return CoreFailure<ExamImportStatusDto>(grades.Errors);
                    }

                    if (grades.Data?.FirstOrDefault(t => t.Id == gradeId) is not { } grade)
                    {
                        return Invalid<ExamImportStatusDto>($"Grade {gradeId} is not under {details.Board}. Use list_options(kind=grade, parentId={details.BoardId}).");
                    }

                    details.GradeId = gradeId;
                    details.Grade = grade.Title;
                }

                ResultData<IEnumerable<ExamImportOptionDto>>? courses = details.BoardId is null ? null : await coreProvider.Value.GetTypesAsync(token, "course", Filter("section_id", details.BoardId));
                if (courses is { OperationResult: not OperationResult.Succeeded })
                {
                    return CoreFailure<ExamImportStatusDto>(courses.Value.Errors);
                }

                var courseOptions = courses?.Data?.ToList() ?? [];
                if (requestDto.CourseId is { } courseId)
                {
                    if (courseId > 0 && courseOptions.TrueForAll(t => t.Id != courseId))
                    {
                        return Invalid<ExamImportStatusDto>(details.BoardId is null ? "Set the board first." : $"Course {courseId} is not under {details.Board}.");
                    }

                    details.CourseId = courseId > 0 ? courseId : null;
                }

                if (requestDto.SubjectId is { } subjectId)
                {
                    if (details.GradeId is null)
                    {
                        return Invalid<ExamImportStatusDto>("Set the grade first.");
                    }

                    var filters = Filter("base_id", details.GradeId);
                    if (details.CourseId is { } course)
                    {
                        filters["course_id"] = course.ToString(CultureInfo.InvariantCulture);
                    }

                    var subjects = await coreProvider.Value.GetTypesAsync(token, "lesson", filters);
                    if (subjects.OperationResult is not OperationResult.Succeeded)
                    {
                        return CoreFailure<ExamImportStatusDto>(subjects.Errors);
                    }

                    if (subjects.Data?.FirstOrDefault(t => t.Id == subjectId) is not { } subject)
                    {
                        return Invalid<ExamImportStatusDto>($"Subject {subjectId} is not under {details.Grade}. Use list_options(kind=subject, parentId={details.GradeId}).");
                    }

                    details.SubjectId = subjectId;
                    details.Subject = subject.Title;
                }

                if (requestDto.PaperId is { } paperId)
                {
                    var papers = await coreProvider.Value.GetTypesAsync(token, "exam_type");
                    if (papers.OperationResult is not OperationResult.Succeeded)
                    {
                        return CoreFailure<ExamImportStatusDto>(papers.Errors);
                    }

                    if (papers.Data?.FirstOrDefault(t => t.Id == paperId) is not { } paper)
                    {
                        return Invalid<ExamImportStatusDto>($"Paper type {paperId} does not exist. Use list_options(kind=paper).");
                    }

                    details.PaperId = paperId;
                    details.Paper = paper.Title;
                }

                var invalid = requestDto switch
                {
                    { SessionMonth: < 1 or > 12 } => "sessionMonth must be 1-12 (3 Feb/March, 6 May/June, 11 Oct/Nov).",
                    { Year: { } year } when year < 1990 || year > DateTime.UtcNow.Year + 1 => "The year looks wrong.",
                    { DurationMinutes: < 1 or > 600 } => "durationMinutes must be 1-600.",
                    { Level: < 0 or > 3 } => "level must be 1 (easy), 2 (medium) or 3 (hard), or 0 to clear it.",
                    _ => null,
                };
                if (invalid is not null)
                {
                    return Invalid<ExamImportStatusDto>(invalid);
                }

                details.SessionMonth = requestDto.SessionMonth ?? details.SessionMonth;
                details.Year = requestDto.Year ?? details.Year;
                details.DurationMinutes = requestDto.DurationMinutes ?? details.DurationMinutes;
                details.Level = requestDto.Level switch
                {
                    null => details.Level,
                    > 0 and var level => level,
                    _ => null,
                };
                details.Component = requestDto.Component is null ? details.Component : Clean(requestDto.Component);
                details.Title = requestDto.Title is null ? details.Title : Clean(requestDto.Title);
                details.NegativeMarking = requestDto.NegativeMarking ?? details.NegativeMarking;
                details.PastPaperId = requestDto.PastPaperId switch
                {
                    null => details.PastPaperId,
                    > 0 and var pastPaperId => pastPaperId,
                    _ => null,
                };
                details.CourseRequired = details.CourseId is null && courseOptions.Count > 0;

                if (details.SubjectId is null)
                {
                    details.Topics = [];
                }
                else
                {
                    var topics = await coreProvider.Value.GetTypesAsync(token, "topic", Filter("lesson_id", details.SubjectId));
                    if (topics.OperationResult is not OperationResult.Succeeded)
                    {
                        return CoreFailure<ExamImportStatusDto>(topics.Errors);
                    }

                    details.Topics = [.. topics.Data ?? []];
                }

                state.Details = details;
                await SaveAsync(state, details: true);
                return new(OperationResult.Succeeded) { Data = await BuildStatusAsync(state) };
            }
            catch (Exception exc)
            {
                return Failure<ExamImportStatusDto>(exc);
            }
        }

        public async Task<ResultData<IEnumerable<ExamImportPastPaperDto>>> FindPastPapersAsync(long userId, [NotNull] string token)
        {
            try
            {
                var details = (await LoadAsync(userId, false))?.Details;
                if (details?.SubjectId is null)
                {
                    return Invalid<IEnumerable<ExamImportPastPaperDto>>("Set the board, grade and subject first.");
                }

                var result = await coreProvider.Value.GetPastPapersAsync(token, new Dictionary<string, string?>(StringComparer.Ordinal)
                {
                    ["section"] = Invariant(details.BoardId),
                    ["base"] = Invariant(details.GradeId),
                    ["lesson"] = Invariant(details.SubjectId),
                    ["edu_year"] = Invariant(details.Year),
                    ["edu_month"] = Invariant(details.SessionMonth),
                    ["is_paper"] = "true",
                    ["perpage"] = "15",
                });
                return result.OperationResult is OperationResult.Succeeded
                    ? new(OperationResult.Succeeded) { Data = result.Data }
                    : CoreFailure<IEnumerable<ExamImportPastPaperDto>>(result.Errors);
            }
            catch (Exception exc)
            {
                return Failure<IEnumerable<ExamImportPastPaperDto>>(exc);
            }
        }

        public async Task<ResultData<ExamImportFigureDto>> AddFigureAsync([NotNull] AddExamImportFigureRequestDto requestDto)
        {
            try
            {
                var content = requestDto.Content ?? [];
                var contentType = ImageContentType(content);
                var invalid = content switch
                {
                    { Length: 0 } => "The image is empty.",
                    { Length: > MaxFigureBytes } => $"The image is larger than {MaxFigureBytes / 1024 / 1024} MB.",
                    _ when contentType is null => "Only PNG and JPEG images can be attached to questions (gama-api's limit). Convert the image first.",
                    _ => null,
                };
                if (invalid is not null)
                {
                    return Invalid<ExamImportFigureDto>(invalid);
                }

                var state = await LoadOrCreateAsync(requestDto.UserId);
                var figures = state.UnitOfWork.GetRepository<ExamImportFigure>();
                if (state.Entity.Id > 0 && await figures.CountAsync(t => t.ExamImportId == state.Entity.Id) >= MaxFigures)
                {
                    return Invalid<ExamImportFigureDto>($"An import can have at most {MaxFigures} figures.");
                }

                var name = Path.GetFileName(requestDto.Name ?? string.Empty).Trim();
                ExamImportFigure figure = new()
                {
                    ExamImport = state.Entity,
                    Name = name.Length == 0 ? "figure" : name[..Math.Min(name.Length, 200)],
                    ContentType = contentType!,
                    Content = content,
                    CreationDate = DateTimeOffset.UtcNow,
                };
                figures.Add(figure);
                await SaveAsync(state);

                return new(OperationResult.Succeeded)
                {
                    Data = new()
                    {
                        FigureId = figure.Id.ToString(CultureInfo.InvariantCulture),
                        Name = figure.Name,
                        ContentType = figure.ContentType,
                        Size = content.Length,
                        Url = new($"{PreviewBaseUrl(requestDto.UserId)}/figures/{figure.Id}"),
                    },
                };
            }
            catch (Exception exc)
            {
                return Failure<ExamImportFigureDto>(exc);
            }
        }

        public async Task<ResultData<ExamImportFigureDto>> AddFigureByLinkAsync([NotNull] string link, [NotNull] AddExamImportFigureRequestDto requestDto)
        {
            if (ReadLink(UploadLinkPurpose, link) is not { } userId)
            {
                return Invalid<ExamImportFigureDto>("This upload link has expired. Ask the assistant for a new one.", "linkExpired");
            }

            requestDto.UserId = userId;
            return await AddFigureAsync(requestDto);
        }

        public ResultData<Uri> GetFigureUploadLink(long userId)
        {
            try
            {
                return new(OperationResult.Succeeded) { Data = new($"{PublicUrl}/mcp/figures/{ProtectLink(UploadLinkPurpose, userId, UploadLinkLifetime)}") };
            }
            catch (Exception exc)
            {
                return Failure<Uri>(exc);
            }
        }

        public async Task<ResultData<ExamImportReportDto>> SaveQuestionsAsync(long userId, [NotNull] IEnumerable<ExamImportQuestionDto> questions, bool replaceAll)
        {
            try
            {
                var input = questions.ToList();
                var problems = input.SelectMany(Validate).ToList();
                if (problems.Count > 0)
                {
                    return Invalid<ExamImportReportDto>($"Nothing was saved. {string.Join(" ", problems)}", "invalidQuestions");
                }

                var state = await LoadOrCreateAsync(userId);
                if (IsRunning(state.Upload))
                {
                    return Invalid<ExamImportReportDto>(UploadRunningMessage, "uploadRunning");
                }

                if (replaceAll)
                {
                    state.Questions.Clear();
                }

                List<string> saved = [];
                foreach (var question in input.Select(Normalize))
                {
                    var index = state.Questions.FindIndex(t => t.Number == question.Number);
                    if (index >= 0)
                    {
                        state.Questions[index] = question;
                    }
                    else
                    {
                        state.Questions.Add(question);
                    }

                    if (!saved.Contains(question.Number))
                    {
                        saved.Add(question.Number);
                    }
                }

                if (state.Questions.Count > MaxQuestions)
                {
                    return Invalid<ExamImportReportDto>($"Nothing was saved: an import can have at most {MaxQuestions} questions.");
                }

                await SaveAsync(state, questions: true);
                var report = Report(CheckAll(state.Questions, state.Details, await FigureIdsAsync(state)), state.Details, saved);
                report.Saved = saved;
                return new(OperationResult.Succeeded) { Data = report };
            }
            catch (Exception exc)
            {
                return Failure<ExamImportReportDto>(exc);
            }
        }

        public async Task<ResultData<ExamImportReportDto>> RemoveQuestionAsync(long userId, [NotNull] string number)
        {
            try
            {
                var state = await LoadAsync(userId, true);
                if (state is not null && IsRunning(state.Upload))
                {
                    return Invalid<ExamImportReportDto>(UploadRunningMessage, "uploadRunning");
                }

                if (state is null || state.Questions.RemoveAll(t => t.Number == number.Trim()) == 0)
                {
                    return Invalid<ExamImportReportDto>($"There is no question {number}.");
                }

                await SaveAsync(state, questions: true);
                return new(OperationResult.Succeeded)
                {
                    Data = new()
                    {
                        Removed = number,
                        Summary = Summarize(CheckAll(state.Questions, state.Details, await FigureIdsAsync(state)), state.Details),
                    },
                };
            }
            catch (Exception exc)
            {
                return Failure<ExamImportReportDto>(exc);
            }
        }

        public async Task<ResultData<ExamImportReportDto>> GetReviewAsync(long userId, bool details)
        {
            try
            {
                var state = await LoadAsync(userId, false);
                var importDetails = state?.Details ?? new();
                var rows = state is null ? [] : CheckAll(state.Questions, importDetails, await FigureIdsAsync(state));
                var report = Report(rows, importDetails, null);
                report.Issues = details ? report.Issues : null;
                return new(OperationResult.Succeeded) { Data = report };
            }
            catch (Exception exc)
            {
                return Failure<ExamImportReportDto>(exc);
            }
        }

        public async Task<ResultData<ExamImportPreviewDto>> GetPreviewAsync(long userId)
        {
            try
            {
                return new(OperationResult.Succeeded) { Data = await BuildPreviewAsync(await LoadAsync(userId, false), PreviewBaseUrl(userId)) };
            }
            catch (Exception exc)
            {
                return Failure<ExamImportPreviewDto>(exc);
            }
        }

        public async Task<ResultData<ExamImportPreviewDto>> GetPreviewByLinkAsync([NotNull] string link)
        {
            try
            {
                return ReadLink(PreviewLinkPurpose, link) is { } userId
                    ? new(OperationResult.Succeeded) { Data = await BuildPreviewAsync(await LoadAsync(userId, false), $"{PublicUrl}/mcp/preview/{link}") }
                    : Invalid<ExamImportPreviewDto>("This preview link has expired. Ask for a new preview in the chat.", "linkExpired");
            }
            catch (Exception exc)
            {
                return Failure<ExamImportPreviewDto>(exc);
            }
        }

        public async Task<ResultData<ExamImportFigureDto>> GetFigureByLinkAsync([NotNull] string link, long figureId)
        {
            try
            {
                if (ReadLink(PreviewLinkPurpose, link) is not { } userId)
                {
                    return new(OperationResult.NotFound);
                }

                var figure = await UnitOfWorkProvider.Value.CreateUnitOfWork().GetRepository<ExamImportFigure>()
                    .GetManyQueryable(t => t.Id == figureId && t.ExamImport!.UserId == userId)
                    .Select(t => new ExamImportFigureDto { Name = t.Name, ContentType = t.ContentType, Content = t.Content })
                    .FirstOrDefaultAsync();
                return figure is null ? new(OperationResult.NotFound) : new(OperationResult.Succeeded) { Data = figure };
            }
            catch (Exception exc)
            {
                return Failure<ExamImportFigureDto>(exc);
            }
        }

        public async Task<ResultData<ExamImportUploadResultDto>> StartUploadAsync([NotNull] StartExamImportUploadRequestDto requestDto)
        {
            try
            {
                var state = await LoadAsync(requestDto.UserId, true);
                if (state is null || state.Questions.Count == 0)
                {
                    return Invalid<ExamImportUploadResultDto>("There are no questions to upload yet.", "noQuestions");
                }

                if (IsRunning(state.Upload))
                {
                    return Invalid<ExamImportUploadResultDto>("An upload is already running. Use submission_status.", "uploadRunning");
                }

                if (requestDto.ExistingDraft is not ("ask" or "useExisting" or "deleteExisting"))
                {
                    return Invalid<ExamImportUploadResultDto>("existingDraft must be ask, useExisting or deleteExisting.");
                }

                var missing = MissingDetails(state.Details);
                if (missing.Count > 0)
                {
                    return Invalid<ExamImportUploadResultDto>($"Some exam details are missing: {string.Join(", ", missing)}.", "missingDetails", missing);
                }

                var rows = CheckAll(state.Questions, state.Details, await FigureIdsAsync(state));
                var blocked = rows.Where(t => t.Status == BlockedStatus).Select(t => t.Question.Number).ToList();
                if (blocked.Count > 0)
                {
                    return Invalid<ExamImportUploadResultDto>("Some questions must be fixed or skipped first.", "mustFix", Report(rows, state.Details, blocked).Issues);
                }

                var review = rows.Where(t => t.Status == ReviewStatus).Select(t => t.Question.Number).ToList();
                if (review.Count > 0 && !requestDto.IncludeNeedsReview)
                {
                    return Invalid<ExamImportUploadResultDto>("Some questions are flagged for review. Ask the user whether to include them (includeNeedsReview=true) or skip them.", "needsReview", review);
                }

                var numbers = rows.Where(t => t.Status is ReadyStatus or ReviewStatus).Select(t => t.Question.Number).ToList();
                if (numbers.Count == 0)
                {
                    return Invalid<ExamImportUploadResultDto>("There are no questions to upload: every question is skipped.", "noQuestions");
                }

                var examId = state.Upload.ExamId;
                if (examId is null)
                {
                    var current = await coreProvider.Value.GetCurrentExamAsync(requestDto.Token);
                    if (current.OperationResult is not OperationResult.Succeeded)
                    {
                        return CoreFailure<ExamImportUploadResultDto>(current.Errors);
                    }

                    if (current.Data is { } draft)
                    {
                        if (requestDto.ExistingDraft == "ask")
                        {
                            return Invalid<ExamImportUploadResultDto>("The user already has an unpublished draft exam on Gamatrain (only one is allowed). Ask whether to reuse it (its details and questions will be replaced) or delete it, then call submit again with existingDraft=useExisting or deleteExisting.", "existingDraft", draft);
                        }

                        var cleared = requestDto.ExistingDraft == "deleteExisting"
                            ? await coreProvider.Value.DeleteExamAsync(requestDto.Token, draft.Id)
                            : await coreProvider.Value.UpdateExamAsync(requestDto.Token, draft.Id, ExamForm(state.Details));
                        if (cleared.OperationResult is not OperationResult.Succeeded)
                        {
                            return CoreFailure<ExamImportUploadResultDto>(cleared.Errors);
                        }

                        examId = requestDto.ExistingDraft == "useExisting" ? draft.Id : null;
                    }
                }

                var now = DateTimeOffset.UtcNow;
                state.Upload = new()
                {
                    Phase = Running,
                    RunId = Guid.NewGuid().ToString("N"),
                    ExamId = examId,
                    ExamCode = examId == state.Upload.ExamId ? state.Upload.ExamCode : null,
                    Created = state.Upload.Created,
                    Queue = numbers,
                    Message = "Starting…",
                    StartedAt = now,
                    UpdatedAt = now,
                };
                return await StartRunAsync(state, requestDto.Token, numbers.Count);
            }
            catch (Exception exc)
            {
                return Failure<ExamImportUploadResultDto>(exc);
            }
        }

        public async Task<ResultData<ExamImportUploadResultDto>> RetryUploadAsync(long userId, [NotNull] string token)
        {
            try
            {
                var state = await LoadAsync(userId, true);
                if (state is null)
                {
                    return Invalid<ExamImportUploadResultDto>("Nothing to retry.", "nothingToRetry");
                }

                var phase = PhaseOf(state.Upload);
                if (phase is Running or WaitingRateLimit)
                {
                    return Invalid<ExamImportUploadResultDto>(UploadRunningMessage, "uploadRunning");
                }

                var upload = state.Upload;
                var known = state.Questions.Select(t => t.Number).ToHashSet(StringComparer.Ordinal);
                List<string> numbers = phase is Interrupted or FailedPhase || upload.Failed.ContainsKey(JobKey)
                    ? [.. upload.Queue.Where(t => !upload.Created.ContainsKey(t) && known.Contains(t))]
                    : [.. upload.Failed.Keys.Where(t => t != JobKey && known.Contains(t))];
                if (numbers.Count == 0)
                {
                    return Invalid<ExamImportUploadResultDto>("Nothing to retry.", "nothingToRetry");
                }

                var blocked = CheckAll(state.Questions.Where(t => numbers.Contains(t.Number)), state.Details, await FigureIdsAsync(state))
                    .Where(t => t.Status is BlockedStatus or SkippedStatus)
                    .Select(t => t.Question.Number)
                    .ToList();
                if (blocked.Count > 0)
                {
                    return Invalid<ExamImportUploadResultDto>($"Questions {string.Join(", ", blocked)} must be fixed (or skipped and left out) first.", "mustFix", blocked);
                }

                _ = upload.Failed.Remove(JobKey);
                upload.Phase = Running;
                upload.RunId = Guid.NewGuid().ToString("N");
                upload.Queue = numbers;
                upload.Message = "Starting…";
                upload.UpdatedAt = DateTimeOffset.UtcNow;
                return await StartRunAsync(state, token, numbers.Count);
            }
            catch (Exception exc)
            {
                return Failure<ExamImportUploadResultDto>(exc);
            }
        }

        public async Task<ResultData<Void>> RunUploadAsync(long importId, [NotNull] string runId, CancellationToken cancellationToken)
        {
            try
            {
                var uow = UnitOfWorkProvider.Value.CreateUnitOfWork();
                var entity = await uow.GetRepository<ExamImport>().GetAsync(importId, tracking: false);
                var upload = Read<ExamImportUploadDto>(entity?.Upload);
                if (entity is null || upload.RunId != runId || upload.Phase is not (Running or WaitingRateLimit))
                {
                    return new(OperationResult.Succeeded) { Data = new() };
                }

                UploadRun run = new(uow, entity.Id, entity.Upload, upload, UnprotectToken(entity.GamaToken), Read<ExamImportDetailsDto>(entity.Details),
                    Read<List<ExamImportQuestionDto>>(entity.Questions), cancellationToken);
                await RunAsync(run);
                return new(OperationResult.Succeeded) { Data = new() };
            }
            catch (OperationCanceledException)
            {
                // Shutdown: Hangfire runs the job again after the restart, and it resumes from the saved progress.
                throw;
            }
            catch (Exception exc)
            {
                return Failure<Void>(exc);
            }
        }

        public async Task<ResultData<ExamImportUploadResultDto>> GetUploadStatusAsync(long userId, int waitSeconds, CancellationToken cancellationToken)
        {
            try
            {
                var deadline = DateTimeOffset.UtcNow.AddSeconds(Math.Clamp(waitSeconds, 0, 40));
                var uow = UnitOfWorkProvider.Value.CreateUnitOfWork();
                var state = await LoadAsync(userId, false, uow);
                var updatedAt = state?.Upload.UpdatedAt;
                while (state is not null && IsRunning(state.Upload) && state.Upload.UpdatedAt == updatedAt && DateTimeOffset.UtcNow < deadline)
                {
                    await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
                    state = await LoadAsync(userId, false, uow);
                }

                return new(OperationResult.Succeeded) { Data = ToUploadResult(state?.Upload ?? new()) };
            }
            catch (Exception exc)
            {
                return Failure<ExamImportUploadResultDto>(exc);
            }
        }

        public async Task<ResultData<ExamImportUploadResultDto>> PublishAsync(long userId, [NotNull] string token)
        {
            try
            {
                var state = await LoadAsync(userId, true);
                var upload = state?.Upload ?? new();
                var created = state?.Questions.Where(t => upload.Created.ContainsKey(t.Number)).Select(t => upload.Created[t.Number]).ToList() ?? [];
                var invalid = upload switch
                {
                    _ when IsRunning(upload) => "Questions are still uploading. Wait until submission_status reports draftReady.",
                    { ExamId: null } => "There is no draft exam to publish yet.",
                    _ when created.Count < MinQuestionsToPublish => $"Gamatrain needs at least {MinQuestionsToPublish} question to publish; the draft has {created.Count}.",
                    _ => null,
                };
                if (invalid is not null || state is null)
                {
                    return Invalid<ExamImportUploadResultDto>(invalid ?? "There is no import.");
                }

                var attached = await coreProvider.Value.SetExamTestsAsync(token, upload.ExamId!.Value, created);
                var published = attached.OperationResult is OperationResult.Succeeded ? await coreProvider.Value.PublishExamAsync(token, upload.ExamId.Value) : attached;
                if (published.OperationResult is not OperationResult.Succeeded)
                {
                    return CoreFailure<ExamImportUploadResultDto>(published.Errors);
                }

                upload.Phase = Published;
                upload.RunId = null;
                upload.Message = "Published.";
                upload.UpdatedAt = DateTimeOffset.UtcNow;
                state.Entity.GamaToken = null;
                await SaveAsync(state, upload: true);

                var result = ToUploadResult(upload);
                result.Questions = created.Count;
                return new(OperationResult.Succeeded) { Data = result };
            }
            catch (Exception exc)
            {
                return Failure<ExamImportUploadResultDto>(exc);
            }
        }

        public async Task<ResultData<ExamImportUploadResultDto>> DiscardAsync(long userId, [NotNull] string token, bool deleteQuestions)
        {
            try
            {
                var state = await LoadAsync(userId, true);
                if (state is null)
                {
                    return Invalid<ExamImportUploadResultDto>("There is nothing to discard.");
                }

                var upload = state.Upload;

                // Stop a running upload first: its next progress save sees another run and ends there.
                upload.RunId = null;
                upload.Phase = Cancelled;
                upload.UpdatedAt = DateTimeOffset.UtcNow;
                await SaveAsync(state, upload: true);

                List<string> errors = [];
                long? deletedExam = null;
                if (upload.ExamId is { } examId)
                {
                    var deleted = await coreProvider.Value.DeleteExamAsync(token, examId);
                    if (deleted.OperationResult is OperationResult.Succeeded)
                    {
                        deletedExam = examId;
                    }
                    else
                    {
                        errors.Add($"Exam {examId}: {deleted.Errors?.FirstOrDefault().Message}");
                    }
                }

                List<long> deletedQuestions = [];
                foreach (var (number, questionId) in deleteQuestions ? upload.Created.ToList() : [])
                {
                    var deleted = await coreProvider.Value.DeleteExamTestAsync(token, questionId);
                    if (deleted.OperationResult is OperationResult.Succeeded)
                    {
                        deletedQuestions.Add(questionId);
                        _ = upload.Created.Remove(number);
                    }
                    else
                    {
                        errors.Add($"Question {number} ({questionId}): {deleted.Errors?.FirstOrDefault().Message}");
                    }
                }

                upload.ExamId = deletedExam is null ? upload.ExamId : null;
                upload.ExamCode = deletedExam is null ? upload.ExamCode : null;
                upload.Message = "Draft discarded.";
                upload.UpdatedAt = DateTimeOffset.UtcNow;
                state.Entity.GamaToken = null;
                await SaveAsync(state, upload: true);

                var result = ToUploadResult(upload);
                result.DeletedExam = deletedExam;
                result.DeletedQuestions = deletedQuestions;
                result.Errors = errors.Count > 0 ? errors : null;
                return new(OperationResult.Succeeded) { Data = result };
            }
            catch (Exception exc)
            {
                return Failure<ExamImportUploadResultDto>(exc);
            }
        }

        public async Task<ResultData<int>> RemoveStaleImportsAsync()
        {
            try
            {
                // A running upload saves its progress (and LastModifyDate) at least every minute, so it is never stale.
                var cutoff = DateTimeOffset.UtcNow.AddDays(-(configuration.Value.GetValue<int?>("Mcp:ImportRetentionDays") ?? 14));
                var removed = await UnitOfWorkProvider.Value.CreateUnitOfWork().GetRepository<ExamImport>()
                    .GetManyQueryable(t => t.LastModifyDate < cutoff).ExecuteDeleteAsync();
                return new(OperationResult.Succeeded) { Data = removed };
            }
            catch (Exception exc)
            {
                return Failure<int>(exc);
            }
        }

        #region Upload run

        /// <summary>
        /// The background upload, ported from the Python exam tools: the draft exam first (so a problem with the details
        /// shows before minutes are spent on questions), then each question with its images, attached to the draft right
        /// away so the draft always shows the progress. Waits between creates for gama-api's rate limits and retries on
        /// <c>rateLimit-addnew</c> and on 5xx/network errors.
        /// </summary>
        private async Task RunAsync(UploadRun run)
        {
            if (run.Token is null)
            {
                await FailRunAsync(run, "Your Gamatrain sign-in is not available to the upload any more. Sign in again (reconnect the Gamatrain app), then resume with retry_failed.", null);
                return;
            }

            if (run.Upload.ExamId is null)
            {
                if (!await SaveProgressAsync(run, Running, "Creating the draft exam…"))
                {
                    return;
                }

                var draft = await CallAsync(run, () => coreProvider.Value.CreateExamAsync(run.Token, ExamForm(run.Details)), ExamInterval);
                if (draft.OperationResult is not OperationResult.Succeeded || draft.Data is null)
                {
                    await FailRunAsync(run, null, draft.Errors?.FirstOrDefault());
                    return;
                }

                run.Upload.ExamId = draft.Data.Id;
                run.Upload.ExamCode = draft.Data.Code;
                if (!await SaveProgressAsync(run, null, null))
                {
                    // Discarded meanwhile: don't leave the new draft behind.
                    _ = await coreProvider.Value.DeleteExamAsync(run.Token, draft.Data.Id);
                    return;
                }
            }

            var total = run.Upload.Queue.Count;
            var index = 0;
            foreach (var number in run.Upload.Queue)
            {
                index++;
                if (run.Upload.Created.ContainsKey(number))
                {
                    continue;
                }

                var question = run.Questions.Find(t => t.Number == number);
                if (question is null)
                {
                    run.Upload.Failed[number] = new() { Error = "removed", Message = "The question was removed before the upload." };
                    continue;
                }

                if (!await SaveProgressAsync(run, Running, $"Question {number} ({index}/{total})…"))
                {
                    return;
                }

                var created = await CreateQuestionAsync(run, question);
                if (run.Stopped)
                {
                    return;
                }

                if (created.OperationResult is not OperationResult.Succeeded)
                {
                    var error = created.Errors?.FirstOrDefault() ?? default;
                    if (IsAuthError(error) || FatalCodes.Contains(error.Reference ?? string.Empty))
                    {
                        await FailRunAsync(run, null, error);
                        return;
                    }

                    run.Upload.Failed[number] = new() { Error = error.Reference, Message = error.Message };
                    continue;
                }

                run.Upload.Created[number] = created.Data;
                _ = run.Upload.Failed.Remove(number);
                if (!await SaveProgressAsync(run, null, null))
                {
                    // Discarded while this question was being created: don't leave it behind.
                    _ = await coreProvider.Value.DeleteExamTestAsync(run.Token, created.Data);
                    return;
                }

                if (!await AttachAsync(run))
                {
                    return;
                }
            }

            var done = run.Upload.Queue.Count(run.Upload.Created.ContainsKey);
            _ = await SaveProgressAsync(run, DraftReady, $"{done} of {total} questions are on the draft exam.", clearToken: true);
        }

        private async Task<ResultData<long>> CreateQuestionAsync(UploadRun run, ExamImportQuestionDto question)
        {
            Dictionary<string, string> fileKeys = new(StringComparer.Ordinal);
            List<(string Field, string FigureId)> figures = [];
            if (question.Figure is not null)
            {
                figures.Add(("q_file", question.Figure));
            }

            if (OptionCount(question.Type) > 0)
            {
                figures.AddRange((question.OptionFigures ?? []).Take(OptionCount(question.Type)).Select((t, i) => ($"{Letter(i).ToLowerInvariant()}_file", t)));
            }

            if (question.AnswerFigure is not null)
            {
                figures.Add(("answer_full_file", question.AnswerFigure));
            }

            foreach (var (field, figureId) in figures)
            {
                var key = await UploadFigureAsync(run, figureId);
                if (key.OperationResult is not OperationResult.Succeeded || key.Data is null)
                {
                    return new(OperationResult.Failed) { Errors = key.Errors };
                }

                fileKeys[field] = key.Data;
            }

            var form = QuestionForm(question, run.Details, fileKeys);
            var wait = run.LastQuestionAt + QuestionInterval - DateTimeOffset.UtcNow;
            if (wait > TimeSpan.Zero)
            {
                if (!await SaveProgressAsync(run, WaitingRateLimit, $"Waiting {(int)wait.TotalSeconds}s (Gamatrain allows one question every 20 seconds)…"))
                {
                    return new(OperationResult.Failed);
                }

                await Task.Delay(wait, run.CancellationToken);
            }

            try
            {
                return await CallAsync(run, () => coreProvider.Value.CreateExamTestAsync(run.Token!, form), QuestionInterval);
            }
            finally
            {
                run.LastQuestionAt = DateTimeOffset.UtcNow;
            }
        }

        private async Task<ResultData<string>> UploadFigureAsync(UploadRun run, string figureId)
        {
            var id = long.TryParse(figureId, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : 0;
            var figure = await run.UnitOfWork.GetRepository<ExamImportFigure>()
                .GetManyQueryable(t => t.Id == id && t.ExamImportId == run.ImportId)
                .Select(t => new { t.ContentType, t.Content })
                .FirstOrDefaultAsync(run.CancellationToken);
            if (figure is null)
            {
                return new(OperationResult.Failed) { Errors = [new() { Message = $"Figure {figureId} no longer exists.", Reference = "unknownFigure" }] };
            }

            // gama-api checks the extension (jpg, jpeg, png) and moves the file into the question, so each use uploads it again.
            var fileName = $"figure-{figureId}{(figure.ContentType == "image/png" ? ".png" : ".jpg")}";
            return await CallAsync(run, () => coreProvider.Value.UploadFileAsync(run.Token!, fileName, figure.ContentType, figure.Content), TimeSpan.FromSeconds(3));
        }

        /// <summary>Attaches every question created so far to the draft (gama-api replaces the whole list).</summary>
        private async Task<bool> AttachAsync(UploadRun run)
        {
            var ids = run.Questions.Where(t => run.Upload.Created.ContainsKey(t.Number)).Select(t => run.Upload.Created[t.Number]).ToList();
            var attached = await CallAsync(run, () => coreProvider.Value.SetExamTestsAsync(run.Token!, run.Upload.ExamId!.Value, ids), TimeSpan.FromSeconds(1.5));
            if (attached.OperationResult is OperationResult.Succeeded || run.Stopped)
            {
                return !run.Stopped;
            }

            await FailRunAsync(run, null, attached.Errors?.FirstOrDefault());
            return false;
        }

        private static async Task<ResultData<T>> CallAsync<T>(UploadRun run, Func<Task<ResultData<T>>> call, TimeSpan rateLimitWait)
        {
            const int attempts = 5;
            ResultData<T> result = default;
            for (var attempt = 1; attempt <= attempts; attempt++)
            {
                result = await call();
                var error = result.Errors?.FirstOrDefault() ?? default;
                if (result.OperationResult is OperationResult.Succeeded || attempt == attempts)
                {
                    return result;
                }

                if (IsRateLimited(error))
                {
                    if (!await SaveProgressAsync(run, WaitingRateLimit, $"Gamatrain rate limit; waiting {(int)rateLimitWait.TotalSeconds}s…"))
                    {
                        return result;
                    }

                    await Task.Delay(rateLimitWait, run.CancellationToken);
                }
                else if (IsTransient(error))
                {
                    await Task.Delay(TimeSpan.FromSeconds(Math.Min(Math.Pow(2, attempt), 20)), run.CancellationToken);
                }
                else
                {
                    return result;
                }
            }

            return result;
        }

        private static async Task FailRunAsync(UploadRun run, string? message, Error? error)
        {
            message ??= error switch
            {
                { } e when IsAuthError(e) => "The Gamatrain sign-in expired. Sign in again (reconnect the Gamatrain app), then resume with retry_failed.",
                { Reference: "notCompletedInfo" } => "Gamatrain asks the user to complete their teacher profile (section, state, area, school) before adding questions.",
                { Info: "alreadyInProgress" } => "The user already has an unpublished draft exam on Gamatrain. Discard it or reuse it, then resume.",
                { } e => $"Gamatrain refused the request: {e.Message}",
                null => "The upload stopped.",
            };
            run.Upload.Failed[JobKey] = new() { Error = error?.Reference, Message = error?.Message ?? message };
            _ = await SaveProgressAsync(run, FailedPhase, message, clearToken: true);
        }

        /// <summary>
        /// Saves the run's progress only if the stored upload is still exactly what this run last saw: a discard or a newer
        /// run (or a second copy of this job) changes it, and this run then stops (<see cref="UploadRun.Stopped"/>).
        /// </summary>
        private static async Task<bool> SaveProgressAsync(UploadRun run, string? phase, string? message, bool clearToken = false)
        {
            var now = DateTimeOffset.UtcNow;
            run.Upload.Phase = phase ?? run.Upload.Phase;
            run.Upload.Message = message ?? run.Upload.Message;
            run.Upload.UpdatedAt = now;
            var json = Write(run.Upload);
            var expected = run.SavedJson;
            var saved = await run.UnitOfWork.GetRepository<ExamImport>()
                .GetManyQueryable(t => t.Id == run.ImportId && t.Upload == expected)
                .ExecuteUpdateAsync(t =>
                {
                    _ = t.SetProperty(p => p.Upload, json).SetProperty(p => p.LastModifyDate, now);
                    if (clearToken)
                    {
                        _ = t.SetProperty(p => p.GamaToken, (string?)null);
                    }
                }, run.CancellationToken);
            run.SavedJson = saved == 1 ? json : run.SavedJson;
            run.Stopped = saved != 1;
            return saved == 1;
        }

        private static bool IsRateLimited(Error error) => error.Value is StatusCodes.Status429TooManyRequests || error.Info == "rateLimit-addnew";

        private static bool IsAuthError(Error error) => error.Value is StatusCodes.Status401Unauthorized || error.Reference is "accessDenied" or "unauthorized";

        /// <summary>No answer from gama-api, or a 5xx without its own error code.</summary>
        private static bool IsTransient(Error error) => error.Reference is null && (error.Value is null or >= StatusCodes.Status500InternalServerError);

        #endregion

        private async Task<ResultData<ExamImportUploadResultDto>> StartRunAsync(ImportState state, string token, int questions)
        {
            state.Entity.GamaToken = dataProtectionProvider.Value.CreateProtector(TokenPurpose).Protect(token);
            await SaveAsync(state, upload: true);

            var minutes = Math.Max(1, (int)Math.Round(questions * QuestionInterval.TotalSeconds / 60));
            var result = ToUploadResult(state.Upload);
            result.ImportId = state.Entity.Id;
            result.RunId = state.Upload.RunId;
            result.Questions = questions;
            result.EstimatedMinutes = minutes;
            return new(OperationResult.Succeeded) { Data = result };
        }

        private async Task<ExamImportStatusDto> BuildStatusAsync(ImportState? state)
        {
            var details = state?.Details ?? new();
            var figureIds = await FigureIdsAsync(state);
            return new()
            {
                Details = details,
                TitleWillBe = Title(details),
                Figures = figureIds.Count,
                Summary = Summarize(state is null ? [] : CheckAll(state.Questions, details, figureIds), details),
                Upload = ToUploadResult(state?.Upload ?? new()),
            };
        }

        private static async Task<ExamImportPreviewDto> BuildPreviewAsync(ImportState? state, string baseUrl)
        {
            var details = state?.Details ?? new();
            var figureIds = await FigureIdsAsync(state);
            var rows = state is null ? [] : CheckAll(state.Questions, details, figureIds);
            var topics = (details.Topics ?? []).ToDictionary(t => t.Id, t => t.Title);
            Uri? Image(string? figureId) => figureId is not null && figureIds.Contains(figureId) ? new($"{baseUrl}/figures/{figureId}") : null;
            List<ExamImportPreviewDto.OptionDto>? Options(ExamImportQuestionDto question)
            {
                var count = OptionCount(question.Type);
                var texts = question switch
                {
                    { Options.Count: > 0 } => question.Options,
                    { Type: "tf" } => ["True", "False"],
                    _ => [],
                };
                return count == 0 ? null : [.. Enumerable.Range(0, count).Select(i => new ExamImportPreviewDto.OptionDto
                {
                    Letter = Letter(i),
                    Html = i < texts.Count ? ExamImportText.ToHtml(texts[i]) : string.Empty,
                    Image = Image(question.OptionFigures?.ElementAtOrDefault(i)),
                    Correct = question.Correct == Letter(i),
                })];
            }

            return new()
            {
                Title = Title(details),
                Details =
                [
                    new() { Label = "Board", Value = details.Board },
                    new() { Label = "Grade", Value = details.Grade },
                    new() { Label = "Subject", Value = details.Subject },
                    new() { Label = "Paper", Value = details.Paper },
                    new() { Label = "Component", Value = details.Component },
                    new() { Label = "Session", Value = SessionLabel(details) },
                    new() { Label = "Duration", Value = details.DurationMinutes is { } minutes ? $"{minutes} min" : null },
                ],
                Summary = Summarize(rows, details),
                Questions = [.. rows.Select(t => new ExamImportPreviewDto.QuestionDto
                {
                    Number = t.Question.Number,
                    Type = TypeLabels.GetValueOrDefault(t.Question.Type, t.Question.Type),
                    Status = t.Status,
                    StatusLabel = StatusLabels.GetValueOrDefault(t.Status, t.Status),
                    Html = t.Question.Text is null ? "<p><i>Refer to the figure.</i></p>" : ExamImportText.ToHtml(t.Question.Text),
                    Image = Image(t.Question.Figure),
                    Options = Options(t.Question),
                    AnswerHtml = t.Question.Answer is null ? null : ExamImportText.ToHtml(t.Question.Answer),
                    AnswerImage = Image(t.Question.AnswerFigure),
                    AnswerSource = t.Question.AnswerSource,
                    Marks = t.Question.Marks,
                    Topic = t.Question.TopicId is { } topicId ? topics.GetValueOrDefault(topicId) : null,
                    Issues = t.Issues,
                })],
                PreviewUrl = new(baseUrl),
            };
        }

        private ExamImportUploadResultDto ToUploadResult(ExamImportUploadDto upload)
        {
            var phase = PhaseOf(upload);
            return new()
            {
                Phase = phase,
                Message = phase == Interrupted ? "The upload was interrupted (a server restart). Resume it with retry_failed; it continues where it stopped." : upload.Message,
                Progress = upload.Queue.Count == 0 ? null : $"{upload.Queue.Count(upload.Created.ContainsKey)}/{upload.Queue.Count}",
                Failed = upload.Failed.Count == 0 ? null : upload.Failed,
                ExamId = upload.ExamId,
                DraftUrl = phase is DraftReady or FailedPhase or Interrupted && upload.ExamId is { } draftId ? SiteUrl("Mcp:ExamDraftUrl", draftId) : null,
                ExamUrl = phase == Published && upload.ExamId is { } examId ? SiteUrl("Mcp:ExamUrl", examId) : null,
            };
        }

        private static ExamImportReportDto Report(IReadOnlyList<CheckedQuestion> rows, ExamImportDetailsDto details, IReadOnlyCollection<string>? numbers) => new()
        {
            Issues = [.. rows
                .Where(t => numbers?.Contains(t.Question.Number) ?? (t.Issues.Count > 0))
                .Select(t => new ExamImportReportDto.QuestionDto { Number = t.Question.Number, Status = t.Status, Issues = t.Issues })],
            Summary = Summarize(rows, details),
        };

        private static IEnumerable<ExamImportOptionDto> Rank(IEnumerable<ExamImportOptionDto> items, string? search)
        {
            if (string.IsNullOrWhiteSpace(search))
            {
                return items;
            }

            var wanted = search.Trim().ToUpperInvariant();
            return items
                .Select(t => (Item: t, Score: Similarity(wanted, t.Title?.Trim().ToUpperInvariant() ?? string.Empty)))
                .Where(t => t.Score >= 0.45)
                .OrderByDescending(t => t.Score)
                .Select(t => t.Item);

            static double Similarity(string a, string b) => b switch
            {
                { Length: 0 } => 0,
                _ when a == b => 1,
                _ when a.Contains(b, StringComparison.Ordinal) || b.Contains(a, StringComparison.Ordinal) => 0.9,
                _ => 1 - ((double)Distance(a, b) / Math.Max(a.Length, b.Length)),
            };

            static int Distance(string a, string b)
            {
                var previous = Enumerable.Range(0, b.Length + 1).ToArray();
                for (var i = 1; i <= a.Length; i++)
                {
                    var current = new int[b.Length + 1];
                    current[0] = i;
                    for (var j = 1; j <= b.Length; j++)
                    {
                        current[j] = Math.Min(Math.Min(current[j - 1] + 1, previous[j] + 1), previous[j - 1] + (a[i - 1] == b[j - 1] ? 0 : 1));
                    }

                    previous = current;
                }

                return previous[b.Length];
            }
        }

        private static string PhaseOf(ExamImportUploadDto upload) =>
            upload.Phase is Running or WaitingRateLimit && upload.UpdatedAt < DateTimeOffset.UtcNow - StaleAfter ? Interrupted : upload.Phase ?? Idle;

        private static bool IsRunning(ExamImportUploadDto upload) => PhaseOf(upload) is Running or WaitingRateLimit;

        /// <summary>PNG or JPEG by their signature, the only images gama-api takes for a question.</summary>
        private static string? ImageContentType(byte[] content) => content switch
        {
            [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, ..] => "image/png",
            [0xFF, 0xD8, 0xFF, ..] => "image/jpeg",
            _ => null,
        };

        private static Dictionary<string, string?> Filter(string name, int? value) => new(StringComparer.Ordinal) { [name] = Invariant(value) };

        private static string? Invariant(long? value) => value?.ToString(CultureInfo.InvariantCulture);

        private static string? Clean(string value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

        private static T Read<T>(string? json)
            where T : new() => string.IsNullOrEmpty(json) ? new() : JsonSerializer.Deserialize<T>(json, JsonOptions) ?? new();

        private static string Write<T>(T value) => JsonSerializer.Serialize(value, JsonOptions);

        private static ResultData<T> Invalid<T>(string message, string? code = null, object? data = null) =>
            new(OperationResult.NotValid) { Errors = [new() { Message = message, Reference = code, Value = data }] };

        /// <summary>A gama-api failure, said so the assistant can explain it.</summary>
        private static ResultData<T> CoreFailure<T>(IEnumerable<Error>? errors)
        {
            var error = errors?.FirstOrDefault() ?? default;
            return IsAuthError(error)
                ? Invalid<T>("The Gamatrain sign-in has expired or was revoked. Ask the user to reconnect the Gamatrain app (sign in again), then try again.", "signInExpired")
                : new(OperationResult.Failed) { Errors = [new() { Message = $"Gamatrain answered: {error.Message}", Reference = error.Reference ?? "gamaError", Info = error.Info }] };
        }

        private ResultData<T> Failure<T>(Exception exc)
        {
            Logger.Value.LogException(exc);
            return new(OperationResult.Failed) { Errors = [new() { Message = exc.Message }] };
        }

        private string PublicUrl => McpPublicUrl.Get(configuration.Value, HttpContextAccessor.Value.HttpContext);

        private string PreviewBaseUrl(long userId) => $"{PublicUrl}/mcp/preview/{ProtectLink(PreviewLinkPurpose, userId, PreviewLinkLifetime)}";

        private Uri SiteUrl(string key, long examId) => new(string.Format(CultureInfo.InvariantCulture, configuration.Value.GetValue<string>(key)!, examId));

        private string ProtectLink(string purpose, long userId, TimeSpan lifetime) =>
            dataProtectionProvider.Value.CreateProtector(purpose).ToTimeLimitedDataProtector().Protect(userId.ToString(CultureInfo.InvariantCulture), lifetime);

        private long? ReadLink(string purpose, string link)
        {
            try
            {
                var value = dataProtectionProvider.Value.CreateProtector(purpose).ToTimeLimitedDataProtector().Unprotect(link);
                return long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var userId) ? userId : null;
            }
            catch (CryptographicException)
            {
                return null;
            }
        }

        private string? UnprotectToken(string? value)
        {
            try
            {
                return value is null ? null : dataProtectionProvider.Value.CreateProtector(TokenPurpose).Unprotect(value);
            }
            catch (CryptographicException)
            {
                return null;
            }
        }

        private static async Task<HashSet<string>> FigureIdsAsync(ImportState? state) => state is null || state.Entity.Id == 0
            ? []
            : [.. (await state.UnitOfWork.GetRepository<ExamImportFigure>()
                .GetManyQueryable(t => t.ExamImportId == state.Entity.Id).Select(t => t.Id).ToListAsync())
                .Select(t => t.ToString(CultureInfo.InvariantCulture))];

        /// <summary>The caller's import, tracked (to change it) or not, with the unit of work it belongs to: every
        /// CreateUnitOfWork call gets its own DbContext, so an import is read, changed and saved through one.</summary>
        private async Task<ImportState?> LoadAsync(long userId, bool tracking, IUnitOfWork? unitOfWork = null)
        {
            var uow = unitOfWork ?? UnitOfWorkProvider.Value.CreateUnitOfWork();
            var entity = await uow.GetRepository<ExamImport>().GetAsync(new UserIdEqualsSpecification<ExamImport, long>(userId), tracking);
            return entity is null
                ? null
                : new()
                {
                    UnitOfWork = uow,
                    Entity = entity,
                    Details = Read<ExamImportDetailsDto>(entity.Details),
                    Questions = Read<List<ExamImportQuestionDto>>(entity.Questions),
                    Upload = Read<ExamImportUploadDto>(entity.Upload),
                };
        }

        private async Task<ImportState> LoadOrCreateAsync(long userId)
        {
            var uow = UnitOfWorkProvider.Value.CreateUnitOfWork();
            if (await LoadAsync(userId, true, uow) is { } state)
            {
                return state;
            }

            var now = DateTimeOffset.UtcNow;
            ExamImport entity = new() { UserId = userId, CreationDate = now, LastModifyDate = now };
            uow.GetRepository<ExamImport>().Add(entity);
            return new() { UnitOfWork = uow, Entity = entity, Details = new(), Questions = [], Upload = new() };
        }

        /// <summary>Writes the changed parts back; each is its own column, so a tool call and the background upload never
        /// overwrite each other's part.</summary>
        private static async Task SaveAsync(ImportState state, bool details = false, bool questions = false, bool upload = false)
        {
            state.Entity.Details = details ? Write(state.Details) : state.Entity.Details;
            state.Entity.Questions = questions ? Write(state.Questions) : state.Entity.Questions;
            state.Entity.Upload = upload ? Write(state.Upload) : state.Entity.Upload;
            state.Entity.LastModifyDate = DateTimeOffset.UtcNow;
            _ = await state.UnitOfWork.SaveChangesAsync();
        }

        private sealed class ImportState
        {
            public required IUnitOfWork UnitOfWork { get; init; }

            public required ExamImport Entity { get; init; }

            public required ExamImportDetailsDto Details { get; set; }

            public required List<ExamImportQuestionDto> Questions { get; init; }

            public required ExamImportUploadDto Upload { get; set; }
        }

        private sealed class UploadRun(IUnitOfWork unitOfWork, long importId, string? savedJson, ExamImportUploadDto upload, string? token,
            ExamImportDetailsDto details, List<ExamImportQuestionDto> questions, CancellationToken cancellationToken)
        {
            public IUnitOfWork UnitOfWork { get; } = unitOfWork;

            public long ImportId { get; } = importId;

            /// <summary>The upload JSON as this run last saved (or read) it.</summary>
            public string? SavedJson { get; set; } = savedJson;

            public ExamImportUploadDto Upload { get; } = upload;

            public string? Token { get; } = token;

            public ExamImportDetailsDto Details { get; } = details;

            public List<ExamImportQuestionDto> Questions { get; } = questions;

            public CancellationToken CancellationToken { get; } = cancellationToken;

            public DateTimeOffset LastQuestionAt { get; set; } = DateTimeOffset.MinValue;

            /// <summary>Another run, or a discard, took over the import: this run must not write anything more.</summary>
            public bool Stopped { get; set; }
        }
    }
}
