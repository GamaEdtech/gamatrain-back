namespace GamaEdtech.Application.Service
{
    using System;
    using System.Diagnostics.CodeAnalysis;
    using System.Globalization;
    using System.Security.Cryptography;

    using GamaEdtech.Application.Interface;
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
        Lazy<IConfiguration> configuration, Lazy<IDataProtectionProvider> dataProtectionProvider, Lazy<IIdentityService> identityService)
        : LocalizableServiceBase<ExamImportService>(unitOfWorkProvider, httpContextAccessor, localizer, logger), IExamImportService
    {
        private const string UploadLinkPurpose = "GamaEdtech.ExamImport.FigureUploadLink";
        private const string StaffOnlyMessage = "Only Gamatrain staff (admins and sub-admins) can make an exam from a paper on Gamatrain. Import the paper's file instead.";

        /// <summary>gama-api's status of an unpublished draft exam.</summary>
        private const int DraftStatus = 6;

        private const int MaxFigureBytes = 5 * 1024 * 1024;

        /// <summary>A tool call has about a minute (ChatGPT): a batch of questions must fit in it.</summary>
        private const int MaxQuestionsPerSave = 40;

        private const int RecentPapersPerPage = 20;

        /// <summary>gama-api gives one download link a second per address, and every user of this API shares its address.</summary>
        private static readonly TimeSpan DownloadInterval = TimeSpan.FromSeconds(1.2);

        /// <summary>gama-api's admin (1) and sub-admin (7) groups: gamatrain's staff.</summary>
        private static readonly int[] StaffGroups = [1, 7];

        private static readonly TimeSpan UploadLinkLifetime = TimeSpan.FromHours(2);

        private static readonly Dictionary<string, (string Type, string? ParentFilter, string? Parent)> OptionKinds = new(StringComparer.Ordinal)
        {
            ["board"] = ("section", null, null),
            ["grade"] = ("base", "section_id", "board"),
            ["course"] = ("course", "section_id", "board"),
            ["subject"] = ("lesson", "base_id", "grade"),
            ["topic"] = ("topic", "lesson_id", "subject"),
            ["paper"] = ("exam_type", null, null),
        };

        public async Task<ResultData<ExamImportStatusDto>> GetStatusAsync([NotNull] string token)
        {
            try
            {
                var current = await coreProvider.Value.GetCurrentExamIdAsync(token);
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
                if (!OptionKinds.TryGetValue(kind, out var option))
                {
                    return Invalid<IEnumerable<ExamImportOptionDto>>("kind must be board, grade, course, subject, topic or paper.");
                }

                if (option.ParentFilter is not null && parentId is not > 0)
                {
                    return Invalid<IEnumerable<ExamImportOptionDto>>($"Listing {kind}s needs parentId (the {option.Parent} id).");
                }

                var (options, errors) = await TypesAsync(token, option.Type, (option.ParentFilter, parentId), (kind == "subject" ? "course_id" : null, courseId));
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

                if (requestDto.ExamId is { } examId && await GetDraftAsync(token, examId) is { Data: null } notDraft)
                {
                    return notDraft;
                }

                // Every id is checked under its parent, so a board, grade or course that changed can't keep the subject chosen before.
                var (boards, errors) = await TypesAsync(token, "section");
                if (errors is not null)
                {
                    return CoreFailure<ExamImportDraftDto>(errors);
                }

                if (boards.Find(t => t.Id == requestDto.BoardId) is not { } board)
                {
                    return Invalid<ExamImportDraftDto>($"Board {requestDto.BoardId} does not exist. Use list_options(kind=board).");
                }

                (var grades, errors) = await TypesAsync(token, "base", ("section_id", board.Id));
                if (errors is not null)
                {
                    return CoreFailure<ExamImportDraftDto>(errors);
                }

                if (grades.Find(t => t.Id == requestDto.GradeId) is not { } grade)
                {
                    return Invalid<ExamImportDraftDto>($"Grade {requestDto.GradeId} is not under {board.Title}. Use list_options(kind=grade, parentId={board.Id}).");
                }

                (var courses, errors) = await TypesAsync(token, "course", ("section_id", board.Id));
                if (errors is not null)
                {
                    return CoreFailure<ExamImportDraftDto>(errors);
                }

                invalid = requestDto.CourseId switch
                {
                    null when courses.Count > 0 => $"{board.Title} needs a course. Use list_options(kind=course, parentId={board.Id}).",
                    { } courseId when courses.TrueForAll(t => t.Id != courseId) => $"Course {courseId} is not under {board.Title}.",
                    _ => null,
                };
                if (invalid is not null)
                {
                    return Invalid<ExamImportDraftDto>(invalid);
                }

                (var subjects, errors) = await TypesAsync(token, "lesson", ("base_id", grade.Id), ("course_id", requestDto.CourseId));
                if (errors is not null)
                {
                    return CoreFailure<ExamImportDraftDto>(errors);
                }

                if (subjects.Find(t => t.Id == requestDto.SubjectId) is not { } subject)
                {
                    return Invalid<ExamImportDraftDto>($"Subject {requestDto.SubjectId} is not under {grade.Title}. Use list_options(kind=subject, parentId={grade.Id}).");
                }

                (var papers, errors) = await TypesAsync(token, "exam_type");
                if (errors is not null)
                {
                    return CoreFailure<ExamImportDraftDto>(errors);
                }

                if (papers.Find(t => t.Id == requestDto.PaperId) is not { } paper)
                {
                    return Invalid<ExamImportDraftDto>($"Paper type {requestDto.PaperId} does not exist. Use list_options(kind=paper).");
                }

                (var topics, errors) = await TypesAsync(token, "topic", ("lesson_id", subject.Id));
                if (errors is not null)
                {
                    return CoreFailure<ExamImportDraftDto>(errors);
                }

                ExamImportDraftDto details = new()
                {
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
                    DurationMinutes = requestDto.DurationMinutes,
                    Title = Clean(requestDto.Title),
                    Level = requestDto.Level,
                    NegativeMarking = requestDto.NegativeMarking,
                    PastPaperId = requestDto.PastPaperId > 0 ? requestDto.PastPaperId : null,
                };
                var form = ExamForm(details);
                long draftId;
                if (requestDto.ExamId is { } id)
                {
                    var updated = await coreProvider.Value.UpdateExamAsync(token, id, form);
                    if (updated.OperationResult is not OperationResult.Succeeded)
                    {
                        return CoreFailure<ExamImportDraftDto>(updated.Errors);
                    }

                    draftId = id;
                }
                else
                {
                    var created = await coreProvider.Value.CreateExamAsync(token, form);
                    if (created.OperationResult is not OperationResult.Succeeded)
                    {
                        // gama-api allows a teacher one unpublished draft.
                        return created.Errors?.FirstOrDefault().Info == "alreadyInProgress"
                            ? await ExistingDraftAsync(token)
                            : CoreFailure<ExamImportDraftDto>(created.Errors);
                    }

                    draftId = created.Data;
                }

                var draft = await GetDraftAsync(token, draftId);
                if (draft.Data is { } saved)
                {
                    saved.Topics = topics;
                }

                return draft;
            }
            catch (Exception exc)
            {
                return Failure<ExamImportDraftDto>(exc);
            }
        }

        public async Task<ResultData<IEnumerable<ExamImportPastPaperDto>>> FindPastPapersAsync([NotNull] string token, int boardId, int gradeId, int subjectId, int? year, int? sessionMonth)
        {
            try
            {
                var result = await coreProvider.Value.GetPastPapersAsync(token, new Dictionary<string, string?>(StringComparer.Ordinal)
                {
                    ["section"] = Invariant(boardId),
                    ["base"] = Invariant(gradeId),
                    ["lesson"] = Invariant(subjectId),
                    ["edu_year"] = Invariant(year),
                    ["edu_month"] = Invariant(sessionMonth),
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

        public async Task<ResultData<IEnumerable<ExamImportPastPaperDto>>> GetRecentPapersAsync([NotNull] string token, int page)
        {
            try
            {
                if (!await IsStaffAsync(token))
                {
                    return Invalid<IEnumerable<ExamImportPastPaperDto>>(StaffOnlyMessage, "staffOnly");
                }

                var result = await coreProvider.Value.GetPastPapersAsync(token, new Dictionary<string, string?>(StringComparer.Ordinal)
                {
                    ["sortby"] = "subdatedesc",
                    ["page"] = Invariant(Math.Max(page, 1)),
                    ["perpage"] = Invariant(RecentPapersPerPage),
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

        public async Task<ResultData<ExamImportPastPaperDto>> LoadPaperAsync([NotNull] string token, long paperId)
        {
            try
            {
                if (!await IsStaffAsync(token))
                {
                    return Invalid<ExamImportPastPaperDto>(StaffOnlyMessage, "staffOnly");
                }

                var paper = await coreProvider.Value.GetPastPaperAsync(token, paperId);
                if (paper.OperationResult is not OperationResult.Succeeded || paper.Data is null)
                {
                    return CoreFailure<ExamImportPastPaperDto>(paper.Errors);
                }

                // The exam's paper type is gama-api's exam_type with the title of the paper's classification (Paper 1..6).
                var (paperTypes, errors) = await TypesAsync(token, "exam_type");
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

        public async Task<ResultData<ExamImportFigureDto>> AddFigureAsync([NotNull] string token, [NotNull] AddExamImportFigureRequestDto requestDto)
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

                // gama-api checks the extension (jpg, jpeg, png).
                var uploaded = await coreProvider.Value.UploadFileAsync(token, contentType == "image/png" ? "figure.png" : "figure.jpg", contentType!, content);
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

        public async Task<ResultData<ExamImportFigureDto>> AddFigureByLinkAsync([NotNull] string link, [NotNull] AddExamImportFigureRequestDto requestDto) =>
            ReadLink(link) is { } token
                ? await AddFigureAsync(token, requestDto)
                : Invalid<ExamImportFigureDto>("This upload link has expired. Ask the assistant for a new one.", "linkExpired");

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

                var (topics, errors) = await TypesAsync(token, "topic", ("lesson_id", draft.Data.SubjectId));
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

                    var form = QuestionForm(question, draft.Data);
                    if (question.Id is { } existingId)
                    {
                        var updated = await coreProvider.Value.UpdateExamTestAsync(token, existingId, form);
                        (row.Status, row.Error) = updated.OperationResult is OperationResult.Succeeded ? (row.Status, null) : (FailedStatus, ErrorMessage(updated.Errors));
                        continue;
                    }

                    var added = await coreProvider.Value.CreateExamTestAsync(token, form);
                    if (added.OperationResult is OperationResult.Succeeded)
                    {
                        row.Id = added.Data;
                        created.Add(added.Data);
                    }
                    else
                    {
                        (row.Status, row.Error) = (FailedStatus, ErrorMessage(added.Errors));
                    }
                }

                var attached = await AttachAsync(token, examId, created);
                if (attached.OperationResult is not OperationResult.Succeeded)
                {
                    // Don't leave questions that aren't on the draft behind: the batch can simply be saved again.
                    foreach (var id in created)
                    {
                        _ = await coreProvider.Value.DeleteExamTestAsync(token, id);
                    }

                    return CoreFailure<ExamImportReportDto>(attached.Errors);
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
                    : CoreFailure<ExamImportReportDto>(removed.Errors);
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

                var questions = await coreProvider.Value.GetExamQuestionsAsync(token, examId);
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

                var published = await coreProvider.Value.PublishExamAsync(token, examId);
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

                var questions = deleteQuestions ? await coreProvider.Value.GetExamQuestionsAsync(token, examId) : new(OperationResult.Succeeded) { Data = [] };
                if (questions.OperationResult is not OperationResult.Succeeded)
                {
                    return CoreFailure<ExamImportDraftResultDto>(questions.Errors);
                }

                var deleted = await coreProvider.Value.DeleteExamAsync(token, examId);
                if (deleted.OperationResult is not OperationResult.Succeeded)
                {
                    return CoreFailure<ExamImportDraftResultDto>(deleted.Errors);
                }

                // Only the questions the caller made that aren't in the question bank yet: one taken from the bank stays there.
                List<long> deletedQuestions = [];
                List<string> errors = [];
                foreach (var id in (questions.Data ?? []).Where(t => t.Owner && t.Pending).Select(t => t.Id))
                {
                    var result = await coreProvider.Value.DeleteExamTestAsync(token, id);
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

        /// <summary>The caller's unpublished draft <paramref name="examId"/>; anything else is refused.</summary>
        private async Task<ResultData<ExamImportDraftDto>> GetDraftAsync(string token, long examId)
        {
            var exam = await coreProvider.Value.GetExamAsync(token, examId);
            if (exam.OperationResult is not OperationResult.Succeeded || exam.Data is null)
            {
                return CoreFailure<ExamImportDraftDto>(exam.Errors);
            }

            if (!exam.Data.Owner || exam.Data.Status != DraftStatus)
            {
                return Invalid<ExamImportDraftDto>($"Exam {examId} is not the user's unpublished draft.", "notDraft");
            }

            exam.Data.DraftUrl = SiteUrl("Mcp:ExamDraftUrl", examId);
            return exam;
        }

        private async Task<ResultData<ExamImportDraftDto>> ExistingDraftAsync(string token)
        {
            var current = await coreProvider.Value.GetCurrentExamIdAsync(token);
            var draft = current.Data is { } examId ? await GetDraftAsync(token, examId) : default;
            return Invalid<ExamImportDraftDto>(
                "The user already has an unpublished draft exam on Gamatrain (only one is allowed). Ask whether to continue it (set_exam_details with its examId; its questions stay) or delete it (discard_draft), then call set_exam_details again.",
                "existingDraft",
                draft.Data is { } existing ? new { examId = existing.Id, title = existing.Title, questions = existing.QuestionIds.Count, draftUrl = existing.DraftUrl } : null);
        }

        /// <summary>
        /// Adds <paramref name="ids"/> to the draft and returns how many questions it has. gama-api replaces the whole list,
        /// so the list is read just before, and read again after to make sure a concurrent save didn't drop them.
        /// </summary>
        private async Task<ResultData<int>> AttachAsync(string token, long examId, IReadOnlyCollection<long> ids)
        {
            for (var attempt = 0; attempt < 3; attempt++)
            {
                var current = await coreProvider.Value.GetExamTestIdsAsync(token, examId);
                if (current.OperationResult is not OperationResult.Succeeded)
                {
                    return new(current.OperationResult) { Errors = current.Errors };
                }

                List<long> list = [.. current.Data ?? []];
                var missing = ids.Where(t => !list.Contains(t)).ToList();
                if (missing.Count == 0)
                {
                    return new(OperationResult.Succeeded) { Data = list.Count };
                }

                var set = await coreProvider.Value.SetExamTestsAsync(token, examId, [.. list, .. missing]);
                if (set.OperationResult is not OperationResult.Succeeded)
                {
                    return new(set.OperationResult) { Errors = set.Errors };
                }
            }

            return new(OperationResult.Failed) { Errors = [new() { Message = "The draft kept changing while the questions were added to it. Save them again." }] };
        }

        /// <summary>Takes a question off the draft, then deletes it when the caller made it and it isn't in the question bank yet.</summary>
        private async Task<ResultData<bool>> RemoveFromDraftAsync(string token, long examId, long questionId)
        {
            var questions = await coreProvider.Value.GetExamQuestionsAsync(token, examId);
            var current = questions.OperationResult is OperationResult.Succeeded ? await coreProvider.Value.GetExamTestIdsAsync(token, examId) : new(questions.OperationResult) { Errors = questions.Errors };
            if (current.OperationResult is not OperationResult.Succeeded)
            {
                return new(current.OperationResult) { Errors = current.Errors };
            }

            var detached = await coreProvider.Value.SetExamTestsAsync(token, examId, (current.Data ?? []).Where(t => t != questionId));
            if (detached.OperationResult is not OperationResult.Succeeded)
            {
                return new(detached.OperationResult) { Errors = detached.Errors };
            }

            if (questions.Data?.FirstOrDefault(t => t.Id == questionId) is not { Owner: true, Pending: true })
            {
                return new(OperationResult.Succeeded) { Data = false };
            }

            var deleted = await coreProvider.Value.DeleteExamTestAsync(token, questionId);
            return deleted.OperationResult is OperationResult.Succeeded ? new(OperationResult.Succeeded) { Data = true } : new(deleted.OperationResult) { Errors = deleted.Errors };
        }

        /// <summary>gama-api's options of a type; a filter without a value is left out. <c>Errors</c> is null on success.</summary>
        private async Task<(List<ExamImportOptionDto> Options, IEnumerable<Error>? Errors)> TypesAsync(string token, string type, params (string? Name, int? Value)[] filters)
        {
            var result = await coreProvider.Value.GetTypesAsync(token, type, filters
                .Where(t => t.Name is not null && t.Value is > 0)
                .ToDictionary(t => t.Name!, t => Invariant(t.Value), StringComparer.Ordinal));
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
                    link = await coreProvider.Value.GetPastPaperFileUrlAsync(token, paper.Id, file.Type, file.ExtraId);
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

        private static string? Invariant(long? value) => value?.ToString(CultureInfo.InvariantCulture);

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
