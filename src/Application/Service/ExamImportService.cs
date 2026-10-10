namespace GamaEdtech.Application.Service
{
    using System;
    using System.Diagnostics.CodeAnalysis;
    using System.Globalization;
    using System.Security.Cryptography;

    using GamaEdtech.Application.Interface;
    using GamaEdtech.Common.Caching;
    using GamaEdtech.Common.Core;
    using GamaEdtech.Common.Data;
    using GamaEdtech.Common.DataAccess.UnitOfWork;
    using GamaEdtech.Common.Service;
    using GamaEdtech.Data.Dto.ExamImport;
    using GamaEdtech.Infrastructure.Interface;

    using Microsoft.AspNetCore.DataProtection;
    using Microsoft.AspNetCore.Http;
    using Microsoft.Extensions.Configuration;
    using Microsoft.Extensions.Localization;
    using Microsoft.Extensions.Logging;

    using static GamaEdtech.Application.Service.ExamImportRules;
    using static GamaEdtech.Common.Core.Constants;

    public sealed class ExamImportService(Lazy<IUnitOfWorkProvider> unitOfWorkProvider, Lazy<IHttpContextAccessor> httpContextAccessor,
        Lazy<IStringLocalizer<ExamImportService>> localizer, Lazy<ILogger<ExamImportService>> logger, Lazy<ICoreProvider> coreProvider,
        Lazy<IConfiguration> configuration, Lazy<IDataProtectionProvider> dataProtectionProvider, Lazy<IIdentityService> identityService,
        Lazy<IWebDownloadProvider> webDownloadProvider, Lazy<ICacheProvider> cacheProvider, Lazy<IMcpAuthorizationService> mcpAuthorizationService)
        : LocalizableServiceBase<ExamImportService>(unitOfWorkProvider, httpContextAccessor, localizer, logger), IExamImportService
    {
        private const string UploadLinkPurpose = "GamaEdtech.ExamImport.FigureUploadLink";
        private const string StaffOnlyMessage = "Only Gamatrain staff (admins and sub-admins) can make an exam from a paper on Gamatrain. Import the paper's file instead.";

        /// <summary>gama-api's status of an unpublished draft exam.</summary>
        private const int DraftStatus = 6;

        private const int MaxFigureBytes = 5 * 1024 * 1024;

        /// <summary>A tool call has about a minute (ChatGPT): a batch of questions must fit in it.</summary>
        private const int MaxQuestionsPerSave = 40;

        /// <summary>The papers gama-api's search gives for the searched words, newest first, before they are matched here.</summary>
        private const int MaxSearchCandidates = 300;

        private const int MaxPageSize = 50;

        /// <summary>gama-api gives one download link a second per address, and every user of this API shares its address.</summary>
        private static readonly TimeSpan DownloadInterval = TimeSpan.FromSeconds(1.2);

        /// <summary>gama-api's admin (1) and sub-admin (7) groups: gamatrain's staff.</summary>
        private static readonly int[] StaffGroups = [1, 7];

        private static readonly TimeSpan UploadLinkLifetime = TimeSpan.FromHours(2);

        /// <summary>gama-api's exam statuses in each list: 6 a draft; 7 published by its owner, 1 confirmed, 5 a copy.</summary>
        private static readonly Dictionary<string, int[]> ExamStatuses = new(StringComparer.Ordinal)
        {
            ["draft"] = [DraftStatus],
            ["published"] = [1, 5, 7],
            ["all"] = [],
        };

        /// <summary>The option kinds and the kind of their parent.</summary>
        private static readonly Dictionary<string, string?> OptionParents = new(StringComparer.Ordinal)
        {
            ["board"] = null,
            ["grade"] = "board",
            ["course"] = "board",
            ["subject"] = "grade",
            ["topic"] = "subject",
            ["paper"] = null,
        };

        public async Task<ResultData<ExamImportStatusDto>> GetStatusAsync([NotNull] string token)
        {
            try
            {
                var current = await coreProvider.Value.GetCurrentExamIdAsync(new() { SecretKey = token });
                if (current.OperationResult is not OperationResult.Succeeded)
                {
                    return CoreFailure<ExamImportStatusDto>(current.Errors);
                }

                ExamImportDraftDto? draft = null;
                if (current.Data is { } examId)
                {
                    var exam = await GetDraftAsync(token, examId);
                    if (exam.Data is null)
                    {
                        return new(exam.OperationResult) { Errors = exam.Errors };
                    }

                    draft = exam.Data;
                }

                return new(OperationResult.Succeeded) { Data = new() { Staff = await IsStaffAsync(token), Draft = draft } };
            }
            catch (Exception exc)
            {
                return Failure<ExamImportStatusDto>(exc);
            }
        }

        public async Task<ResultData<IEnumerable<ExamImportOptionDto>>> GetOptionsAsync([NotNull] string token, [NotNull] string kind, int? parentId, int? courseId, string? search)
        {
            try
            {
                if (!OptionParents.TryGetValue(kind, out var parent))
                {
                    return Invalid<IEnumerable<ExamImportOptionDto>>("kind must be board, grade, course, subject, topic or paper.");
                }

                if (parent is not null && parentId is not > 0)
                {
                    return Invalid<IEnumerable<ExamImportOptionDto>>($"Listing {kind}s needs parentId (the {parent} id).");
                }

                var (options, errors) = await OptionsAsync(token, kind, parentId, courseId);
                return errors is null
                    ? new(OperationResult.Succeeded) { Data = [.. Rank(options, search).Take(300)] }
                    : CoreFailure<IEnumerable<ExamImportOptionDto>>(errors);
            }
            catch (Exception exc)
            {
                return Failure<IEnumerable<ExamImportOptionDto>>(exc);
            }
        }

        public async Task<ResultData<ExamImportDraftDto>> SetDetailsAsync([NotNull] string token, [NotNull] ExamImportDetailsRequestDto requestDto)
        {
            try
            {
                var invalid = requestDto switch
                {
                    { DurationMinutes: < 1 or > 600 } => "durationMinutes must be 1-600.",
                    { SessionMonth: < 1 or > 12 } => "sessionMonth must be 1-12 (3 Feb/March, 6 May/June, 11 Oct/Nov).",
                    { Year: { } year } when year < 1990 || year > DateTime.UtcNow.Year + 1 => "The year looks wrong.",
                    { Level: < 1 or > 3 } => "level must be 1 (easy), 2 (medium) or 3 (hard).",
                    _ => null,
                };
                if (invalid is not null)
                {
                    return Invalid<ExamImportDraftDto>(invalid);
                }

                if (requestDto.ExamId is { } examId)
                {
                    var existing = await GetDraftAsync(token, examId);
                    if (existing.Data is null)
                    {
                        return existing;
                    }

                    // The questions already saved carry the draft's board, grade, course and subject: changing those would leave them mismatched.
                    if (existing.Data is { QuestionIds.Count: > 0 } draftWithQuestions
                        && (draftWithQuestions.BoardId != requestDto.BoardId || draftWithQuestions.GradeId != requestDto.GradeId
                            || draftWithQuestions.CourseId != requestDto.CourseId || draftWithQuestions.SubjectId != requestDto.SubjectId))
                    {
                        return Invalid<ExamImportDraftDto>(
                            $"The draft already has {draftWithQuestions.QuestionIds.Count} questions saved for {draftWithQuestions.Subject} ({draftWithQuestions.Grade}, {draftWithQuestions.Board}). The board, grade, course and subject can't change now: remove those questions first, or discard the draft and start again.",
                            "draftHasQuestions");
                    }
                }

                // Every id is checked under its parent, so a board, grade or course that changed can't keep the subject chosen
                // before; one left out or not valid there is for the user to pick from gama-api's list.
                var (boards, errors) = await OptionsAsync(token, "board");
                if (errors is not null)
                {
                    return CoreFailure<ExamImportDraftDto>(errors);
                }

                if (boards.Find(t => t.Id == requestDto.BoardId) is not { } board)
                {
                    return Pick("board", requestDto.BoardId, boards);
                }

                (var grades, errors) = await OptionsAsync(token, "grade", board.Id);
                if (errors is not null)
                {
                    return CoreFailure<ExamImportDraftDto>(errors);
                }

                if (grades.Find(t => t.Id == requestDto.GradeId) is not { } grade)
                {
                    return Pick("grade", requestDto.GradeId, grades);
                }

                (var courses, errors) = await OptionsAsync(token, "course", board.Id);
                if (errors is not null)
                {
                    return CoreFailure<ExamImportDraftDto>(errors);
                }

                if (courses.Count == 0 && requestDto.CourseId is not null)
                {
                    return Invalid<ExamImportDraftDto>($"{board.Title} has no courses: leave courseId out.");
                }

                if (courses.Count > 0 && courses.TrueForAll(t => t.Id != requestDto.CourseId))
                {
                    return Pick("course", requestDto.CourseId, courses);
                }

                (var subjects, errors) = await OptionsAsync(token, "subject", grade.Id, requestDto.CourseId);
                if (errors is not null)
                {
                    return CoreFailure<ExamImportDraftDto>(errors);
                }

                if (subjects.Find(t => t.Id == requestDto.SubjectId) is not { } subject)
                {
                    return Pick("subject", requestDto.SubjectId, subjects);
                }

                (var papers, errors) = await OptionsAsync(token, "paper");
                if (errors is not null)
                {
                    return CoreFailure<ExamImportDraftDto>(errors);
                }

                if (papers.Find(t => t.Id == requestDto.PaperId) is not { } paper)
                {
                    return Pick("paper", requestDto.PaperId, papers);
                }

                if (requestDto.DurationMinutes is not { } duration)
                {
                    return Invalid<ExamImportDraftDto>("The duration isn't set: the user gives it.", "pickDuration");
                }

                ExamImportDraftDto details = new()
                {
                    Id = requestDto.ExamId ?? 0,
                    BoardId = board.Id,
                    Board = board.Title,
                    GradeId = grade.Id,
                    Grade = grade.Title,
                    CourseId = requestDto.CourseId,
                    SubjectId = subject.Id,
                    Subject = subject.Title,
                    PaperId = paper.Id,
                    Paper = paper.Title,
                    Component = Clean(requestDto.Component),
                    SessionMonth = requestDto.SessionMonth,
                    Year = requestDto.Year,
                    DurationMinutes = duration,
                    Level = requestDto.Level,
                    NegativeMarking = requestDto.NegativeMarking,
                    PastPaperId = requestDto.PastPaperId > 0 ? requestDto.PastPaperId : null,
                    Title = Clean(requestDto.Title),
                };

                // The default title is made of the subject, the component (or paper) and the session.
                details.Title = Title(details);
                details.Session = SessionLabel(details);
                if (!requestDto.Confirmed)
                {
                    return new(OperationResult.Succeeded) { Data = details };
                }

                (var topics, errors) = await OptionsAsync(token, "topic", subject.Id);
                if (errors is not null)
                {
                    return CoreFailure<ExamImportDraftDto>(errors);
                }

                var saved = await coreProvider.Value.SaveExamAsync(new()
                {
                    SecretKey = token,
                    ExamId = requestDto.ExamId,
                    BoardId = board.Id,
                    GradeId = grade.Id,
                    CourseId = requestDto.CourseId,
                    SubjectId = subject.Id,
                    PaperId = paper.Id,
                    DurationMinutes = duration,
                    Title = details.Title,
                    NegativeMarking = requestDto.NegativeMarking,
                    Level = requestDto.Level,
                    Year = requestDto.Year,
                    SessionMonth = requestDto.SessionMonth,
                    PastPaperId = details.PastPaperId,
                });
                if (saved.OperationResult is not OperationResult.Succeeded)
                {
                    // gama-api allows a teacher one unpublished draft.
                    return requestDto.ExamId is null && saved.Errors?.FirstOrDefault().Info == "alreadyInProgress"
                        ? await ExistingDraftAsync(token)
                        : CoreFailure<ExamImportDraftDto>(saved.Errors);
                }

                var draft = await GetDraftAsync(token, saved.Data);
                if (draft.Data is { } exam)
                {
                    exam.Topics = topics;
                }

                return draft;
            }
            catch (Exception exc)
            {
                return Failure<ExamImportDraftDto>(exc);
            }
        }

        public async Task<ResultData<IEnumerable<ExamImportPastPaperDto>>> FindPastPapersAsync([NotNull] string token, int boardId, int gradeId, int subjectId, int? year, int? sessionMonth, int? paperId, int page)
        {
            try
            {
                int? classificationId = null;
                if (paperId is not null)
                {
                    // The paper's classification (test_type, under the board) with the title of the exam's paper type (Paper 1..6).
                    var (paperTypes, errors) = await OptionsAsync(token, "paper");
                    if (errors is not null)
                    {
                        return CoreFailure<IEnumerable<ExamImportPastPaperDto>>(errors);
                    }

                    if (paperTypes.Find(t => t.Id == paperId) is not { } paperType)
                    {
                        return Invalid<IEnumerable<ExamImportPastPaperDto>>($"Paper type {paperId} does not exist. Use list_options(kind=paper).");
                    }

                    (var classifications, errors) = await OptionsAsync(token, "classification", boardId);
                    if (errors is not null)
                    {
                        return CoreFailure<IEnumerable<ExamImportPastPaperDto>>(errors);
                    }

                    classificationId = classifications.Find(t => string.Equals(t.Title, paperType.Title, StringComparison.OrdinalIgnoreCase))?.Id;
                    if (classificationId is null)
                    {
                        // No paper of that type on gamatrain.
                        return new(OperationResult.Succeeded) { Data = [] };
                    }
                }

                var result = await coreProvider.Value.GetPastPapersAsync(new()
                {
                    SecretKey = token,
                    BoardId = boardId,
                    GradeId = gradeId,
                    SubjectId = subjectId,
                    ClassificationId = classificationId,
                    Year = year,
                    SessionMonth = sessionMonth,
                    Page = Math.Max(page, 1),
                    PageSize = 15,
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

        public async Task<ResultData<IEnumerable<ExamImportPastPaperDto>>> GetRecentPapersAsync([NotNull] string token, int page, int pageSize)
        {
            try
            {
                if (!await IsStaffAsync(token))
                {
                    return Invalid<IEnumerable<ExamImportPastPaperDto>>(StaffOnlyMessage, "staffOnly");
                }

                var result = await coreProvider.Value.GetPastPapersAsync(new() { SecretKey = token, Latest = true, Page = Math.Max(page, 1), PageSize = Math.Clamp(pageSize, 1, MaxPageSize) });
                return result.OperationResult is OperationResult.Succeeded
                    ? new(OperationResult.Succeeded) { Data = result.Data }
                    : CoreFailure<IEnumerable<ExamImportPastPaperDto>>(result.Errors);
            }
            catch (Exception exc)
            {
                return Failure<IEnumerable<ExamImportPastPaperDto>>(exc);
            }
        }

        public async Task<ResultData<ListDataSource<ExamImportPastPaperDto>>> SearchPapersAsync([NotNull] string token, [NotNull] string text, int page, int pageSize)
        {
            try
            {
                if (!await IsStaffAsync(token))
                {
                    return Invalid<ListDataSource<ExamImportPastPaperDto>>(StaffOnlyMessage, "staffOnly");
                }

                var words = Words(text);
                if (words.Length == 0)
                {
                    return Invalid<ListDataSource<ExamImportPastPaperDto>>("Type a paper id or words of its title.");
                }

                // gama-api's search matches one piece of the title: the longest word that isn't the year (it filters by the
                // year itself) or "paper" (the paper's classification, Paper 1..6). Every word is then matched here.
                var year = words.Select(t => int.TryParse(t, NumberStyles.None, CultureInfo.InvariantCulture, out var value) && value is >= 1990 and <= 2100 ? value : (int?)null)
                    .FirstOrDefault(t => t is not null);
                var anchor = words.Where(t => t.Length > 1 && t != "PAPER" && t != year?.ToString(CultureInfo.InvariantCulture)).MaxBy(t => t.Length);
                var result = await coreProvider.Value.SearchPastPapersAsync(new() { SecretKey = token, Title = anchor, Year = year, PageSize = MaxSearchCandidates });
                if (result.OperationResult is not OperationResult.Succeeded)
                {
                    return CoreFailure<ListDataSource<ExamImportPastPaperDto>>(result.Errors);
                }

                var matches = (result.Data ?? []).Where(t => HasWords(t, words)).ToList();
                pageSize = Math.Clamp(pageSize, 1, MaxPageSize);
                return new(OperationResult.Succeeded)
                {
                    Data = new() { List = [.. matches.Skip((Math.Max(page, 1) - 1) * pageSize).Take(pageSize)], TotalRecordsCount = matches.Count },
                };
            }
            catch (Exception exc)
            {
                return Failure<ListDataSource<ExamImportPastPaperDto>>(exc);
            }
        }

        public async Task<ResultData<ExamImportPastPaperDto>> LoadPaperAsync([NotNull] string token, long paperId)
        {
            try
            {
                if (!await IsStaffAsync(token))
                {
                    return Invalid<ExamImportPastPaperDto>(StaffOnlyMessage, "staffOnly");
                }

                var paper = await coreProvider.Value.GetPastPaperAsync(new() { SecretKey = token, Id = paperId });
                if (paper.OperationResult is not OperationResult.Succeeded || paper.Data is null)
                {
                    return CoreFailure<ExamImportPastPaperDto>(paper.Errors);
                }

                // The exam's paper type is gama-api's exam_type with the title of the paper's classification (Paper 1..6).
                var (paperTypes, errors) = await OptionsAsync(token, "paper");
                if (errors is not null)
                {
                    return CoreFailure<ExamImportPastPaperDto>(errors);
                }

                paper.Data.PaperId = paperTypes.Find(t => string.Equals(t.Title, paper.Data.Classification, StringComparison.OrdinalIgnoreCase))?.Id;
                await AddFileLinksAsync(token, paper.Data);
                return new(OperationResult.Succeeded) { Data = paper.Data };
            }
            catch (Exception exc)
            {
                return Failure<ExamImportPastPaperDto>(exc);
            }
        }

        public async Task<ResultData<ExamImportFigureDto>> AddFigureAsync([NotNull] string token, [NotNull] AddExamImportFigureRequestDto requestDto, CancellationToken cancellationToken = default)
        {
            try
            {
                var content = requestDto.Content;
                if (content is null && requestDto.Url is { } url)
                {
                    var downloaded = await webDownloadProvider.Value.DownloadAsync(url, MaxFigureBytes, cancellationToken);
                    if (downloaded.OperationResult is not OperationResult.Succeeded)
                    {
                        return new(downloaded.OperationResult) { Errors = downloaded.Errors };
                    }

                    content = downloaded.Data;
                }

                content ??= [];
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

                // gama-api checks the extension (jpg, jpeg, png).
                var uploaded = await coreProvider.Value.UploadFileAsync(new()
                {
                    SecretKey = token,
                    FileName = contentType == "image/png" ? "figure.png" : "figure.jpg",
                    ContentType = contentType!,
                    Content = content,
                });
                if (uploaded.OperationResult is not OperationResult.Succeeded)
                {
                    return CoreFailure<ExamImportFigureDto>(uploaded.Errors);
                }

                var name = Path.GetFileName(requestDto.Name ?? string.Empty).Trim();
                return new(OperationResult.Succeeded)
                {
                    Data = new()
                    {
                        Figure = uploaded.Data,
                        Name = name.Length == 0 ? null : name[..Math.Min(name.Length, 200)],
                        ContentType = contentType,
                        Size = content.Length,
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
            if (ReadLink(link) is not { } token)
            {
                return Invalid<ExamImportFigureDto>("This upload link has expired. Ask the assistant for a new one.", "linkExpired");
            }

            // The link outlives a sign-out of its connection otherwise.
            return await mcpAuthorizationService.Value.IsSignedOutAsync(token)
                ? Invalid<ExamImportFigureDto>("This upload link stopped working: the user signed out of Gamatrain in the assistant.", "signedOut")
                : await AddFigureAsync(token, requestDto);
        }

        public ResultData<Uri> GetFigureUploadLink([NotNull] string token)
        {
            try
            {
                // The link carries the caller's gama-api token, protected and time-limited: it can only upload images for them.
                var link = dataProtectionProvider.Value.CreateProtector(UploadLinkPurpose).ToTimeLimitedDataProtector().Protect(token, UploadLinkLifetime);
                return new(OperationResult.Succeeded) { Data = new($"{PublicUrl}/mcp/figures/{link}") };
            }
            catch (Exception exc)
            {
                return Failure<Uri>(exc);
            }
        }

        public async Task<ResultData<ExamImportReportDto>> SaveQuestionsAsync([NotNull] string token, long examId, [NotNull] IEnumerable<ExamImportQuestionDto> questions)
        {
            try
            {
                var input = questions.ToList();
                var problems = input.SelectMany(Validate).ToList();
                var invalid = input.Count switch
                {
                    0 => "There are no questions to save.",
                    > MaxQuestionsPerSave => $"Save at most {MaxQuestionsPerSave} questions at a time.",
                    _ when problems.Count > 0 => $"Nothing was saved. {string.Join(" ", problems)}",
                    _ => null,
                };
                if (invalid is not null)
                {
                    return Invalid<ExamImportReportDto>(invalid, "invalidQuestions");
                }

                var draft = await GetDraftAsync(token, examId);
                if (draft.Data is null)
                {
                    return new(draft.OperationResult) { Errors = draft.Errors };
                }

                var (topics, errors) = await OptionsAsync(token, "topic", draft.Data.SubjectId);
                if (errors is not null)
                {
                    return CoreFailure<ExamImportReportDto>(errors);
                }

                List<ExamImportReportDto.QuestionDto> rows = [];
                List<long> created = [];
                foreach (var question in input.Select(Normalize))
                {
                    var issues = Check(question, topics);
                    ExamImportReportDto.QuestionDto row = new() { Number = question.Number, Id = question.Id, Status = StatusOf(question, issues), Issues = issues.Count > 0 ? issues : null };
                    rows.Add(row);

                    // Only the draft's own questions can be changed or removed: an id elsewhere could be anyone's question.
                    if (question.Id is { } id && !draft.Data.QuestionIds.Contains(id))
                    {
                        (row.Status, row.Error) = (FailedStatus, $"Question {id} is not on this draft. Leave the id out to add it as a new question.");
                        continue;
                    }

                    if (row.Status is SkippedStatus && question.Id is { } skippedId)
                    {
                        var removed = await RemoveFromDraftAsync(token, examId, skippedId);
                        (row.Status, row.Error) = removed.OperationResult is OperationResult.Succeeded ? (RemovedStatus, null) : (FailedStatus, ErrorMessage(removed.Errors));
                    }

                    if (row.Status is not (SavedStatus or ReviewStatus))
                    {
                        continue;
                    }

                    var saved = await coreProvider.Value.SaveExamTestAsync(ExamTestRequest(token, question, draft.Data));
                    if (saved.OperationResult is not OperationResult.Succeeded)
                    {
                        (row.Status, row.Error) = (FailedStatus, ErrorMessage(saved.Errors));
                    }
                    else if (question.Id is null)
                    {
                        row.Id = saved.Data;
                        created.Add(saved.Data);
                    }
                }

                var attached = await AttachAsync(token, examId, created);
                if (attached.OperationResult is not OperationResult.Succeeded)
                {
                    // Don't leave questions that aren't on the draft behind: the batch can simply be saved again.
                    foreach (var id in created)
                    {
                        _ = await coreProvider.Value.DeleteExamTestAsync(new() { SecretKey = token, Id = id });
                    }

                    return new(attached.OperationResult) { Errors = attached.Errors };
                }

                return new(OperationResult.Succeeded) { Data = new() { Questions = rows, DraftQuestions = attached.Data } };
            }
            catch (Exception exc)
            {
                return Failure<ExamImportReportDto>(exc);
            }
        }

        public async Task<ResultData<ExamImportReportDto>> RemoveQuestionAsync([NotNull] string token, long examId, long questionId)
        {
            try
            {
                var draft = await GetDraftAsync(token, examId);
                if (draft.Data is null)
                {
                    return new(draft.OperationResult) { Errors = draft.Errors };
                }

                if (!draft.Data.QuestionIds.Contains(questionId))
                {
                    return Invalid<ExamImportReportDto>($"Question {questionId} is not on this draft.");
                }

                var removed = await RemoveFromDraftAsync(token, examId, questionId);
                return removed.OperationResult is OperationResult.Succeeded
                    ? new(OperationResult.Succeeded)
                    {
                        Data = new()
                        {
                            Questions = [new() { Id = questionId, Status = RemovedStatus }],
                            DraftQuestions = draft.Data.QuestionIds.Count - 1,
                        },
                    }
                    : new(removed.OperationResult) { Errors = removed.Errors };
            }
            catch (Exception exc)
            {
                return Failure<ExamImportReportDto>(exc);
            }
        }

        public async Task<ResultData<ExamImportPreviewDto>> GetPreviewAsync([NotNull] string token, long examId)
        {
            try
            {
                var draft = await GetDraftAsync(token, examId);
                if (draft.Data is null)
                {
                    return new(draft.OperationResult) { Errors = draft.Errors };
                }

                var questions = await coreProvider.Value.GetExamQuestionsAsync(new() { SecretKey = token, Id = examId });
                if (questions.OperationResult is not OperationResult.Succeeded)
                {
                    return CoreFailure<ExamImportPreviewDto>(questions.Errors);
                }

                var details = draft.Data;
                return new(OperationResult.Succeeded)
                {
                    Data = new()
                    {
                        Title = details.Title,
                        Details =
                        [
                            new() { Label = "Board", Value = details.Board },
                            new() { Label = "Grade", Value = details.Grade },
                            new() { Label = "Subject", Value = details.Subject },
                            new() { Label = "Paper", Value = details.Paper },
                            new() { Label = "Session", Value = SessionLabel(details) },
                            new() { Label = "Duration", Value = details.DurationMinutes is { } minutes ? $"{minutes} min" : null },
                        ],
                        Questions = [.. (questions.Data ?? []).Select((t, i) => new ExamImportPreviewDto.QuestionDto
                        {
                            Number = i + 1,
                            Id = t.Id,
                            Type = TypeLabels.GetValueOrDefault(t.Type ?? string.Empty, t.Type ?? string.Empty),
                            Html = t.Html,
                            Image = t.Image,
                            Options = OptionCount(t.Type ?? string.Empty) is > 0 and var count
                                ? [.. Enumerable.Range(0, count).Select(o => new ExamImportPreviewDto.OptionDto
                                {
                                    Letter = Letter(o),
                                    Html = t.Options.ElementAtOrDefault(o),
                                    Image = t.OptionImages.ElementAtOrDefault(o),
                                    Correct = t.Correct == o + 1,
                                })]
                                : null,
                            AnswerHtml = string.IsNullOrWhiteSpace(t.AnswerHtml) ? null : t.AnswerHtml,
                            AnswerImage = t.AnswerImage,
                        })],
                        PreviewUrl = details.DraftUrl,
                    },
                };
            }
            catch (Exception exc)
            {
                return Failure<ExamImportPreviewDto>(exc);
            }
        }

        public async Task<ResultData<ExamImportDraftResultDto>> PublishAsync([NotNull] string token, long examId)
        {
            try
            {
                var draft = await GetDraftAsync(token, examId);
                if (draft.Data is null)
                {
                    return new(draft.OperationResult) { Errors = draft.Errors };
                }

                var count = draft.Data.QuestionIds.Count;
                if (count < MinQuestionsToPublish)
                {
                    return Invalid<ExamImportDraftResultDto>($"Gamatrain needs at least {MinQuestionsToPublish} question to publish; the draft has {count}.");
                }

                var published = await coreProvider.Value.PublishExamAsync(new() { SecretKey = token, Id = examId });
                return published.OperationResult is OperationResult.Succeeded
                    ? new(OperationResult.Succeeded) { Data = new() { ExamId = examId, Questions = count, ExamUrl = SiteUrl("Mcp:ExamUrl", examId) } }
                    : CoreFailure<ExamImportDraftResultDto>(published.Errors);
            }
            catch (Exception exc)
            {
                return Failure<ExamImportDraftResultDto>(exc);
            }
        }

        public async Task<ResultData<ExamImportDraftResultDto>> DiscardAsync([NotNull] string token, long examId, bool deleteQuestions)
        {
            try
            {
                // Only an unpublished draft of the caller's: a published exam is never deleted here.
                var draft = await GetDraftAsync(token, examId);
                if (draft.Data is null)
                {
                    return new(draft.OperationResult) { Errors = draft.Errors };
                }

                var questions = deleteQuestions ? await coreProvider.Value.GetExamQuestionsAsync(new() { SecretKey = token, Id = examId }) : new(OperationResult.Succeeded) { Data = [] };
                if (questions.OperationResult is not OperationResult.Succeeded)
                {
                    return CoreFailure<ExamImportDraftResultDto>(questions.Errors);
                }

                var deleted = await coreProvider.Value.DeleteExamAsync(new() { SecretKey = token, Id = examId });
                if (deleted.OperationResult is not OperationResult.Succeeded)
                {
                    return CoreFailure<ExamImportDraftResultDto>(deleted.Errors);
                }

                // Only the questions the caller made that aren't in the question bank yet: one taken from the bank stays there.
                List<long> deletedQuestions = [];
                List<string> errors = [];
                foreach (var id in (questions.Data ?? []).Where(t => t.Owner && t.Pending).Select(t => t.Id))
                {
                    var result = await coreProvider.Value.DeleteExamTestAsync(new() { SecretKey = token, Id = id });
                    if (result.OperationResult is OperationResult.Succeeded)
                    {
                        deletedQuestions.Add(id);
                    }
                    else
                    {
                        errors.Add($"Question {id}: {ErrorMessage(result.Errors)}");
                    }
                }

                return new(OperationResult.Succeeded)
                {
                    Data = new() { ExamId = examId, Deleted = true, DeletedQuestions = deletedQuestions, Errors = errors.Count > 0 ? errors : null },
                };
            }
            catch (Exception exc)
            {
                return Failure<ExamImportDraftResultDto>(exc);
            }
        }

        public async Task<ResultData<ExamImportDraftDto>> GetDraftAsync([NotNull] string token, long examId)
        {
            var exam = await GetExamAsync(token, examId);
            return exam.Data is { Status: not DraftStatus }
                ? Invalid<ExamImportDraftDto>($"Exam {examId} is not the user's unpublished draft.", "notDraft")
                : exam;
        }

        public async Task<ResultData<ExamImportDraftDto>> GetExamAsync([NotNull] string token, long examId)
        {
            try
            {
                var exam = await coreProvider.Value.GetExamAsync(new() { SecretKey = token, Id = examId });
                if (exam.OperationResult is not OperationResult.Succeeded || exam.Data is null)
                {
                    return CoreFailure<ExamImportDraftDto>(exam.Errors);
                }

                if (!exam.Data.Owner)
                {
                    return Invalid<ExamImportDraftDto>($"Exam {examId} is not the user's.", "notOwner");
                }

                Complete(exam.Data);
                return exam;
            }
            catch (Exception exc)
            {
                return Failure<ExamImportDraftDto>(exc);
            }
        }

        public async Task<ResultData<ListDataSource<ExamImportDraftDto>>> GetExamsAsync([NotNull] string token, [NotNull] string status, int page, int pageSize)
        {
            try
            {
                if (!ExamStatuses.TryGetValue(status, out var statuses))
                {
                    return Invalid<ListDataSource<ExamImportDraftDto>>("status must be draft, published or all.");
                }

                // gama-api lists everyone's exams to its staff without the caller's own id, so there is no list without it.
                var userId = await identityService.Value.GetLegacyJwtUserIdAsync(token);
                if (userId is not > 0)
                {
                    return Invalid<ListDataSource<ExamImportDraftDto>>("The Gamatrain sign-in doesn't say who the user is. Ask the user to reconnect the Gamatrain app (sign in again), then try again.", "signInExpired");
                }

                var result = await coreProvider.Value.GetExamsAsync(new()
                {
                    SecretKey = token,
                    UserId = userId.Value,
                    Statuses = statuses,
                    Page = Math.Max(page, 1),
                    PageSize = Math.Clamp(pageSize, 1, MaxPageSize),
                });
                if (result.OperationResult is not OperationResult.Succeeded)
                {
                    return CoreFailure<ListDataSource<ExamImportDraftDto>>(result.Errors);
                }

                // gama-api filters by status for its staff only: a teacher's page has every status.
                List<ExamImportDraftDto> exams = [.. (result.Data.List ?? []).Where(t => statuses.Length == 0 || statuses.Contains(t.Status))];
                exams.ForEach(Complete);
                return new(OperationResult.Succeeded) { Data = new() { List = exams, TotalRecordsCount = result.Data.TotalRecordsCount } };
            }
            catch (Exception exc)
            {
                return Failure<ListDataSource<ExamImportDraftDto>>(exc);
            }
        }

        private async Task<ResultData<ExamImportDraftDto>> ExistingDraftAsync(string token)
        {
            var current = await coreProvider.Value.GetCurrentExamIdAsync(new() { SecretKey = token });
            var draft = current.Data is { } examId ? await GetDraftAsync(token, examId) : default;
            return Invalid<ExamImportDraftDto>(
                "The user already has an unpublished draft exam on Gamatrain (only one is allowed): continue it, or discard it and create this one.",
                "existingDraft",
                draft.Data);
        }

        /// <summary>A required detail left out, or not valid under its parent: the user picks it from <paramref name="options"/>.</summary>
        private static ResultData<ExamImportDraftDto> Pick(string detail, int? given, List<ExamImportOptionDto> options) => Invalid<ExamImportDraftDto>(
            given is null ? $"The {detail} isn't set: the user picks it." : $"{detail} {given} is not one of Gamatrain's choices here: the user picks it.",
            $"pick{char.ToUpperInvariant(detail[0])}{detail[1..]}",
            options);

        /// <summary>What gama-api doesn't say about an exam: its session in words and its page on gamatrain.</summary>
        private void Complete(ExamImportDraftDto exam)
        {
            exam.Session = SessionLabel(exam);
            if (exam.Status == DraftStatus)
            {
                exam.DraftUrl = SiteUrl("Mcp:ExamDraftUrl", exam.Id);
            }
            else
            {
                exam.ExamUrl = SiteUrl("Mcp:ExamUrl", exam.Id);
            }
        }

        /// <summary>
        /// Adds <paramref name="ids"/> to the draft and returns how many questions it has. gama-api replaces the whole list,
        /// so a draft's list is changed by one request at a time (<see cref="LockDraftAsync"/>): two saves at once would
        /// otherwise each write the list they read, and the questions of the first would be lost.
        /// </summary>
        private async Task<ResultData<int>> AttachAsync(string token, long examId, IReadOnlyCollection<long> ids)
        {
            await using var draftLock = await LockDraftAsync(examId);
            if (draftLock is null)
            {
                return DraftBusy<int>();
            }

            var current = await coreProvider.Value.GetExamTestIdsAsync(new() { SecretKey = token, Id = examId });
            if (current.OperationResult is not OperationResult.Succeeded)
            {
                return CoreFailure<int>(current.Errors);
            }

            List<long> list = [.. current.Data ?? []];
            var missing = ids.Where(t => !list.Contains(t)).ToList();
            var set = missing.Count == 0 ? new(OperationResult.Succeeded) : await coreProvider.Value.SetExamTestsAsync(new() { SecretKey = token, ExamId = examId, TestIds = [.. list, .. missing] });
            return set.OperationResult is OperationResult.Succeeded ? new(OperationResult.Succeeded) { Data = list.Count + missing.Count } : CoreFailure<int>(set.Errors);
        }

        /// <summary>Takes a question off the draft, then deletes it when the caller made it and it isn't in the question bank yet.</summary>
        private async Task<ResultData<bool>> RemoveFromDraftAsync(string token, long examId, long questionId)
        {
            var questions = await coreProvider.Value.GetExamQuestionsAsync(new() { SecretKey = token, Id = examId });
            if (questions.OperationResult is not OperationResult.Succeeded)
            {
                return CoreFailure<bool>(questions.Errors);
            }

            await using (var draftLock = await LockDraftAsync(examId))
            {
                if (draftLock is null)
                {
                    return DraftBusy<bool>();
                }

                var current = await coreProvider.Value.GetExamTestIdsAsync(new() { SecretKey = token, Id = examId });
                var detached = current.OperationResult is OperationResult.Succeeded
                    ? await coreProvider.Value.SetExamTestsAsync(new() { SecretKey = token, ExamId = examId, TestIds = (current.Data ?? []).Where(t => t != questionId) })
                    : new(current.OperationResult) { Errors = current.Errors };
                if (detached.OperationResult is not OperationResult.Succeeded)
                {
                    return CoreFailure<bool>(detached.Errors);
                }
            }

            if (questions.Data?.FirstOrDefault(t => t.Id == questionId) is not { Owner: true, Pending: true })
            {
                return new(OperationResult.Succeeded) { Data = false };
            }

            var deleted = await coreProvider.Value.DeleteExamTestAsync(new() { SecretKey = token, Id = questionId });
            return deleted.OperationResult is OperationResult.Succeeded ? new(OperationResult.Succeeded) { Data = true } : CoreFailure<bool>(deleted.Errors);
        }

        /// <summary>The lock on a draft's question list, for every instance of the app; null when it stayed busy.</summary>
        private Task<IAsyncDisposable?> LockDraftAsync(long examId) =>
            cacheProvider.Value.LockAsync($"ExamImportDraft_{examId.ToString(CultureInfo.InvariantCulture)}", TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(20));

        private static ResultData<T> DraftBusy<T>() => new(OperationResult.Failed)
        {
            Errors = [new() { Message = "The draft's question list is being changed by another request. Try again in a moment.", Reference = "draftBusy" }],
        };

        /// <summary>gama-api's options of a kind under <paramref name="parentId"/>. <c>Errors</c> is null on success.</summary>
        private async Task<(List<ExamImportOptionDto> Options, IEnumerable<Error>? Errors)> OptionsAsync(string token, string kind, int? parentId = null, int? courseId = null)
        {
            var result = await coreProvider.Value.GetExamOptionsAsync(new() { SecretKey = token, Kind = kind, ParentId = parentId, CourseId = courseId });
            return result.OperationResult is OperationResult.Succeeded ? ([.. result.Data ?? []], null) : ([], result.Errors ?? []);
        }

        /// <summary>The caller is a gama-api admin or sub-admin, by the group in their gama-api token.</summary>
        private async Task<bool> IsStaffAsync(string token) => await identityService.Value.GetLegacyJwtGroupAsync(token) is { } group && StaffGroups.Contains(group);

        /// <summary>
        /// Puts gama-api's download link on each of the paper's files, one a second. Only files gama-api gives the caller for
        /// free (they manage the paper, or the file has no price or is paid for): for anyone else the download call is a
        /// purchase, which gama-api lets through from this API's address as one this API already charged for.
        /// </summary>
        private async Task AddFileLinksAsync(string token, ExamImportPastPaperDto paper)
        {
            var wait = TimeSpan.Zero;
            foreach (var file in paper.Files ?? [])
            {
                if (!paper.Managed && !file.Free)
                {
                    file.Error = "This file has a price on Gamatrain for this account, so it isn't fetched here.";
                    continue;
                }

                ResultData<Uri> link;
                var attempts = 0;
                do
                {
                    await Task.Delay(wait);
                    wait = DownloadInterval;
                    link = await coreProvider.Value.GetPastPaperFileUrlAsync(new() { SecretKey = token, PaperId = paper.Id, Type = file.Type, ExtraId = file.ExtraId });
                }
                while (link.OperationResult is not OperationResult.Succeeded && IsRateLimited(link.Errors?.FirstOrDefault() ?? default) && ++attempts < 3);

                file.Url = link.Data;
                file.Error = link.OperationResult is OperationResult.Succeeded ? null : $"Gamatrain refused it: {ErrorMessage(link.Errors)}";
            }
        }

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

        /// <summary>The words of a text, upper case: runs of letters and digits.</summary>
        private static string[] Words(string? text) =>
            new string([.. (text ?? string.Empty).ToUpperInvariant().Select(t => char.IsLetterOrDigit(t) ? t : ' ')]).Split(' ', StringSplitOptions.RemoveEmptyEntries);

        /// <summary>Every searched word is a word of the paper's title or classification, in any order: a number exactly,
        /// another word as its start (Math finds Mathematics). So 9709 paper 1 2024 finds Mathematics 9709/12 May June 2024,
        /// a Paper 1.</summary>
        private static bool HasWords(ExamImportPastPaperDto paper, string[] words)
        {
            var own = Words($"{paper.Title} {paper.Classification}");
            return Array.TrueForAll(words, word => Array.Exists(own, t => word.All(char.IsAsciiDigit) ? t == word : t.StartsWith(word, StringComparison.Ordinal)));
        }

        /// <summary>PNG or JPEG by their signature, the only images gama-api takes for a question.</summary>
        private static string? ImageContentType(byte[] content) => content switch
        {
            [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, ..] => "image/png",
            [0xFF, 0xD8, 0xFF, ..] => "image/jpeg",
            _ => null,
        };

        /// <summary>A 429, or gama-api's per-endpoint limit (<c>gone</c>).</summary>
        private static bool IsRateLimited(Error error) => error.Value is StatusCodes.Status429TooManyRequests || error.Reference == "gone";

        private static bool IsAuthError(Error error) => error.Value is StatusCodes.Status401Unauthorized || error.Reference is "accessDenied" or "unauthorized";

        private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

        private static string? ErrorMessage(IEnumerable<Error>? errors) => errors?.FirstOrDefault().Message;

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

        private Uri SiteUrl(string key, long examId) => new(string.Format(CultureInfo.InvariantCulture, configuration.Value.GetValue<string>(key)!, examId));

        /// <summary>The gama-api token in an upload link from <see cref="GetFigureUploadLink"/>, or null when it expired.</summary>
        private string? ReadLink(string link)
        {
            try
            {
                return dataProtectionProvider.Value.CreateProtector(UploadLinkPurpose).ToTimeLimitedDataProtector().Unprotect(link);
            }
            catch (CryptographicException)
            {
                return null;
            }
        }
    }
}
