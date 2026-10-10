namespace GamaEdtech.Presentation.Mcp
{
    using System.ComponentModel;
    using System.ComponentModel.DataAnnotations;
    using System.Diagnostics.CodeAnalysis;
    using System.Security.Claims;
    using System.Text.Json;
    using System.Text.Json.Nodes;

    using GamaEdtech.Application.Interface;
    using GamaEdtech.Common.Data;
    using GamaEdtech.Common.Identity;
    using GamaEdtech.Data.Dto.ExamImport;
    using GamaEdtech.Presentation.ViewModel.Exam;
    using GamaEdtech.Presentation.ViewModel.ExamImport;

    using Microsoft.AspNetCore.Http;

    using ModelContextProtocol.Protocol;
    using ModelContextProtocol.Server;

    using static GamaEdtech.Common.Core.Constants;
    using static GamaEdtech.Presentation.Mcp.ExamImportFlow;

    /// <summary>
    /// The MCP tools an AI assistant (ChatGPT, Claude, Codex...) uses for "gamatrain exams", served at <c>/mcp</c>: importing
    /// a past paper into gamatrain as questions and an online exam, and the user's exams. The connector runs the
    /// conversation: each answer ends with the next step from <see cref="ExamImportFlow"/>. The AI reads the paper and the
    /// mark scheme itself and hands over what it extracted; these only take the caller's gama-api token from the MCP access
    /// token, call <see cref="IExamImportService"/> and answer JSON text for the AI. See docs/business/exams-and-content.md,
    /// "Exam import (MCP)".
    /// </summary>
    [McpServerToolType]
    [McpServerPromptType]
    public sealed class ExamImportTools(Lazy<IExamImportService> examImportService, Lazy<IMcpAuthorizationService> authorizationService, Lazy<IHttpContextAccessor> httpContextAccessor)
    {
        public const string Instructions = """
            Gamatrain exams: make an online exam on Gamatrain from a past paper (PDF or Word), and manage the user's exams.
            When the user types "gamatrain exams", or asks for this in other words, call open_exams. Every answer ends with
            the next step: an ask (show its question and options word for word and in order, with your own picker when you
            have one, otherwise as a numbered list, then do the chosen option's next) or a next alone (your own work, which
            ends with the tool call it names). Never add, drop or reword options, and never ask the user anything the
            connector didn't send. Read get_import_guide before you read a paper.
            """;

        private const int MaxImageBytes = 5 * 1024 * 1024;

        private static readonly Lazy<string> Guide = new(() => ReadResource("ExamImportGuide.md"));

        private ClaimsPrincipal User => httpContextAccessor.Value.HttpContext?.User ?? new();

        private string GamaToken => User.FindFirstValue(McpTokenAuthenticationHandler.GamaTokenClaim) ?? string.Empty;

        /// <summary>The slash command where the assistant has them (in Claude Code <c>/mcp__gamatrain__exams</c>).</summary>
        [McpServerPrompt(Name = "exams", Title = "Gamatrain exams")]
        [Description("Make an online exam from a past paper, or manage your exams on Gamatrain.")]
#pragma warning disable S3400 // an MCP prompt is a method
        public static string ExamsPrompt() => "gamatrain exams";
#pragma warning restore S3400

        [McpServerTool(Name = "get_import_guide", Title = "Import guide", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
        [Description("Read this once per conversation, before you read a paper: how the conversation runs (the asks), your own steps, and the exact rules for extracting questions (types, multi-part questions, math, figures, answers).")]
        public static string ReadImportGuide() => Guide.Value;

        [McpServerTool(Name = "open_exams", Title = "Gamatrain exams", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = true)]
        [Description("START HERE when the user types \"gamatrain exams\" or wants to make an exam from a paper or see their exams; also Home and Back. Checks the sign-in, whether the account is staff (a Gamatrain admin or sub-admin) and the user's draft, then asks Home's question: show its ask.")]
        public async Task<string> OpenExamsAsync()
        {
            var result = await examImportService.Value.GetStatusAsync(GamaToken);
            if (result.Data is { } status)
            {
                status.User = User.Identity?.Name;
            }

            return Answer(result, t => new { t.User, t.Staff, t.Draft, ask = Home(t) });
        }

        [McpServerTool(Name = "list_options", Title = "List exam detail options", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = true)]
        [Description("Gamatrain's choices for the exam details, with ids, to find the ids of what you read on the paper: board (e.g. Cambridge, Edexcel) → grade (parentId = board id, e.g. IGCSE, AS & A Level) → subject (parentId = grade id; courseId for boards with courses) → topic (parentId = subject id). course exists only for some boards (parentId = board id). paper = the exam type (Paper 1..6, Topical...). search ranks by similarity.")]
        public async Task<string> ListOptionsAsync(
            [Description("board, grade, course, subject, topic or paper.")][AllowedValues("board", "grade", "course", "subject", "topic", "paper")] string kind,
            [Description("The parent's id: the board for grade/course, the grade for subject, the subject for topic.")] int? parentId = null,
            [Description("For subject, when the board has courses: the course id.")] int? courseId = null,
            [Description("What you read on the paper, to rank the options.")] string? search = null)
        {
            var result = await examImportService.Value.GetOptionsAsync(GamaToken, kind, parentId, courseId, search);
            return Answer(result, t => new { kind, options = t });
        }

        [McpServerTool(Name = "set_exam_details", Title = "Set exam details", ReadOnly = false, Destructive = false, Idempotent = true, OpenWorld = true)]
        [Description("Give every exam detail each time; the ids (from list_options) are checked. confirmed=false checks them and asks the user with the details card (create the draft, or change a detail); a detail left out or not valid comes back as a question to pick it. confirmed=true, only from that card's option, creates the DRAFT exam on Gamatrain (not published; students can't see it) or changes the details of draft examId, and returns the draft's examId, for every other tool, and the subject's topics: give every question a topicId from them.")]
        public async Task<string> SetExamDetailsAsync(
            [NotNull] ExamImportDetailsViewModel details,
            [Description("True only from the details card's option.")] bool confirmed = false)
        {
            var result = await examImportService.Value.SetDetailsAsync(GamaToken, new()
            {
                ExamId = details.ExamId,
                BoardId = details.BoardId,
                GradeId = details.GradeId,
                CourseId = details.CourseId,
                SubjectId = details.SubjectId,
                PaperId = details.PaperId,
                DurationMinutes = details.DurationMinutes,
                Component = details.Component,
                SessionMonth = details.SessionMonth,
                Year = details.Year,
                Title = details.Title,
                Level = details.Level,
                NegativeMarking = details.NegativeMarking,
                PastPaperId = details.PastPaperId,
                Confirmed = confirmed,
            });
            return Answer(result, t => t.Details switch
            {
                null => new { t.Pick, t.Options, t.ExistingDraft, ask = Choice(t) },
                { } saved when confirmed => new { draft = saved, next = AfterDetails(saved.Id, created: details.ExamId is null) },
                { } card => new { details = card, ask = Details(card) },
            });
        }

        [McpServerTool(Name = "find_past_papers", Title = "Find the past paper", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = true)]
        [Description("Find the past paper on Gamatrain that the user's file is (same board, grade, subject, paper type, year and session), 15 a page, so the exam is linked to it: pick the one whose classification and title (component/variant) match the file and pass its id to set_exam_details (pastPaperId).")]
        public async Task<string> FindPastPapersAsync(
            [Description("Board id.")] int boardId,
            [Description("Grade id.")] int gradeId,
            [Description("Subject id.")] int subjectId,
            [Description("The paper's year.")] int? year = null,
            [Description("3 = February/March, 6 = May/June, 11 = October/November.")] int? sessionMonth = null,
            [Description("Paper type id (list_options kind=paper), e.g. Paper 2: only papers of that type.")] int? paperId = null,
            [Description("1 = the first 15 papers, 2 = the next 15...")] int page = 1) =>
            Answer(await examImportService.Value.FindPastPapersAsync(GamaToken, boardId, gradeId, subjectId, year, sessionMonth, paperId, page), t => new { page, papers = t });

        [McpServerTool(Name = "search_papers", Title = "Gamatrain directory", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = true)]
        [Description("Staff only: the Gamatrain directory. Without query it asks the user for a paper ID or title. A number opens that paper (as load_paper); words find the papers whose title has them all, in any order (e.g. 9709 paper 1 2024), 15 a page, and ask the user to pick one.")]
        public async Task<string> SearchPapersAsync(
            [Description("What the user typed: a paper ID, or words of the title. Leave it out to ask the user.")] string? query = null,
            [Description("1 = the first 15 papers found, 2 = the next 15...")] int page = 1)
        {
            if (string.IsNullOrWhiteSpace(query))
            {
                return Answer(Directory());
            }

            // A search that can't run (too broad, or refused) asks for the paper again.
            var result = await examImportService.Value.SearchPapersAsync(GamaToken, query, page, PapersPerPage);
            return Answer(result, t => t.Paper is { } paper
                ? PaperStep(paper)
                : new { query, page, found = t.Papers.TotalRecordsCount, papers = t.Papers.List, ask = SearchResults(query, page, t.Papers) }, failure: _ => Directory());
        }

        [McpServerTool(Name = "list_recent_papers", Title = "Latest papers on Gamatrain", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = true)]
        [Description("Staff only: the papers added to Gamatrain most recently, newest first, 15 a page, with their board, grade, subject, session and files, and asks the user to pick one.")]
        public async Task<string> ListRecentPapersAsync([Description("1 = the newest papers, 2 = the 15 before them...")] int page = 1) =>
            Answer(await examImportService.Value.GetRecentPapersAsync(GamaToken, page, PapersPerPage), t => new { page, papers = t, ask = LatestPapers(page, [.. t]) });

        [McpServerTool(Name = "load_paper", Title = "Start from a Gamatrain paper", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = true)]
        [Description("Staff only: make the exam from a paper already on Gamatrain (its id, picked in the Gamatrain directory). Gives the paper's exam details (boardId, gradeId, courseId, subjectId, paperId, year, month = sessionMonth, id = pastPaperId) for set_exam_details, and a temporary download link (about 1 hour) to each of its files: the question paper (PDF and/or Word), the mark scheme and any extra files. Read ALL of them. Call it again for fresh links.")]
        public async Task<string> LoadPaperAsync([Description("The paper's id on Gamatrain.")] long paperId) =>
            Answer(await examImportService.Value.LoadPaperAsync(GamaToken, paperId), PaperStep);

        [McpServerTool(Name = "add_figure", Title = "Add a figure", ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = true)]
        [McpMeta("openai/fileParams", JsonValue = """["file"]""")]
        [Description("Upload one image (a diagram, graph, picture or table you cut from the paper or the mark scheme, or a figure you drew for a worked solution, PNG or JPEG, prepared by the guide's figure rules: text and watermarks out, the paper's common scale) to Gamatrain and get its figure key for ONE question's figure, optionFigures or answerFigure (a key works once). Give ONE of: file = the image file in the chat; link = a public http(s) link to it; contentBase64 = its bytes (small images only). With a shell, upload many images faster through get_figure_upload_link. One image per question: combine several figures into one image first.")]
        public async Task<string> AddFigureAsync(
            [Description("The image file, attached in the chat.")] McpFileViewModel? file = null,
            [Description("A public http(s) link to the image.")] string? link = null,
            [Description("The image bytes in base64 (a data: URL works too). Small images only.")] string? contentBase64 = null,
            [Description("A name for the image, e.g. q3-graph.png.")] string? fileName = null,
            CancellationToken cancellationToken = default)
        {
            byte[]? content = null;
            Uri? url = null;
            link = file?.DownloadUrl ?? link;
            if (!string.IsNullOrWhiteSpace(contentBase64))
            {
                content = FromBase64(contentBase64);
                if (content is null)
                {
                    return Refuse("contentBase64 is not valid base64, or the image is larger than 5 MB.");
                }
            }
            else if (!Uri.TryCreate(link, UriKind.Absolute, out url))
            {
                return Refuse("Give the image as file, link (an http(s) URL) or contentBase64.");
            }

            var name = fileName ?? file?.FileName ?? (url is null ? null : Path.GetFileName(url.AbsolutePath));
            return Answer(await examImportService.Value.AddFigureAsync(GamaToken, new() { Content = content, Url = url, Name = name }, cancellationToken));
        }

        [McpServerTool(Name = "get_figure_upload_link", Title = "Figure upload link", ReadOnly = true, Destructive = false, Idempotent = false, OpenWorld = false)]
        [Description("For assistants that can run shell commands (Claude Code, Codex): a link to upload figure images to Gamatrain straight from the shell instead of through the chat. POST each PNG or JPEG as the multipart field file, e.g. curl -F file=@q3.png <uploadUrl>; each answer is JSON with the figure key. The link works for 2 hours.")]
        public string GetFigureUploadLink() =>
            Answer(examImportService.Value.GetFigureUploadLink(GamaToken), t => new { uploadUrl = t, example = $"curl -F file=@q3-graph.png {t}" });

        [McpServerTool(Name = "save_questions", Title = "Save questions", ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = true)]
        [Description("Save the questions you extracted into the draft exam on Gamatrain, in paper order, a few pages at a time (at most 40). A question without id is added; to change one, save it again with the id from an earlier result; skip=true with an id takes it off the draft. The result gives each question's id and status: saved, review (saved, a human should look), blocked (not saved: fix it and save it again), skipped, removed or failed (Gamatrain refused it).")]
        public async Task<string> SaveQuestionsAsync(
            [Description("The draft's examId.")] long examId,
            [Description("The questions, in paper order.")][NotNull] IReadOnlyList<ExamImportQuestionViewModel> questions) =>
            Answer(await examImportService.Value.SaveQuestionsAsync(GamaToken, examId, questions.Select(t => new ExamImportQuestionDto
            {
                Id = t.Id,
                Number = t.Number,
                Type = t.Type,
                Text = t.Text,
                Options = t.Options,
                Correct = t.Correct,
                Answer = t.Answer,
                AnswerSource = t.AnswerSource,
                Marks = t.Marks,
                TopicId = t.TopicId,
                Level = t.Level,
                NeedsFigure = t.NeedsFigure,
                Figure = t.Figure,
                OptionFigures = t.OptionFigures,
                AnswerFigure = t.AnswerFigure,
                ReviewNotes = t.ReviewNotes,
                Skip = t.Skip,
            })), next: AfterSave(examId));

        [McpServerTool(Name = "open_review", Title = "Review the draft", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = true)]
        [Description("The review, after every question of the paper is saved (or to continue a draft): how the draft stands, then one question that needs the user's decision at a time. flagged = the save_questions result rows whose status isn't saved, passed as they came (later, the list an option gives).")]
        public async Task<string> OpenReviewAsync(
            [Description("The draft's examId.")] long examId,
            [Description("The save_questions result rows that weren't simply saved (review, blocked or failed), as they came.")] IReadOnlyList<ExamImportReviewQuestionViewModel>? flagged = null,
            [Description("How many AI-written answers the review already counted: only as an option gives it.")] int aiAnswers = 0) =>
            Answer(await examImportService.Value.GetDraftAsync(GamaToken, examId), t => new { examId, t.Title, questions = t.QuestionIds.Count, ask = Review(t, flagged ?? [], Math.Max(aiAnswers, 0)) });

        [McpServerTool(Name = "remove_question", Title = "Remove a question", ReadOnly = false, Destructive = true, Idempotent = true, OpenWorld = true)]
        [Description("Take a question off the draft exam (it was not a real question, or a duplicate); it is deleted when it isn't in Gamatrain's question bank yet.")]
        public async Task<string> RemoveQuestionAsync(
            [Description("The draft's examId.")] long examId,
            [Description("The question's id.")] long questionId) =>
            Answer(await examImportService.Value.RemoveQuestionAsync(GamaToken, examId, questionId));

        [McpServerTool(Name = "show_preview", Title = "Preview the exam", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = true)]
        [McpMeta("ui", JsonValue = $$"""{"resourceUri":"{{ExamImportPreviewWidget.ResourceUri}}"}""")]
        [McpMeta("ui/resourceUri", ExamImportPreviewWidget.ResourceUri)]
        [McpMeta("openai/outputTemplate", ExamImportPreviewWidget.ResourceUri)]
        [McpMeta("openai/toolInvocation/invoking", "Building the preview…")]
        [McpMeta("openai/toolInvocation/invoked", "Preview ready")]
        [Description("Show every question on the draft as Gamatrain stores it (text, formulas, figures, options, the correct answer, answers): in ChatGPT as a card in the chat, and everywhere as the draft's page on Gamatrain (previewUrl), with the question ids in order. Then asks what next (publish, change a question, keep it as a draft, discard it).")]
        public async Task<CallToolResult> ShowPreviewAsync([Description("The draft's examId.")] long examId)
        {
            var result = await examImportService.Value.GetPreviewAsync(GamaToken, examId);
            return new()
            {
                Content = [new TextContentBlock { Text = Answer(result, t => new { examId, questions = t.Questions?.Count ?? 0, questionIds = t.Questions?.Select(q => q.Id), t.PreviewUrl, ask = Preview(examId) }) }],
                StructuredContent = result.Data is { } preview ? JsonSerializer.SerializeToElement(new { questions = preview.Questions?.Count ?? 0, previewUrl = preview.PreviewUrl }, JsonOptions) : null,
                Meta = result.Data is { } data ? new JsonObject { ["gamatrain/preview"] = JsonSerializer.SerializeToNode(data, JsonOptions) } : null,
            };
        }

        [McpServerTool(Name = "publish_exam", Title = "Publish the exam", ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = true)]
        [Description("Publish the draft exam so students can take it. Without confirmed it asks the user; confirmed=true only from that question's option. Answers the exam's link.")]
        public async Task<string> PublishExamAsync(
            [Description("The draft's examId.")] long examId,
            [Description("True only from the publish question's option.")] bool confirmed = false) => confirmed
            ? Answer(await examImportService.Value.PublishAsync(GamaToken, examId), t => new { t.ExamId, t.Questions, t.ExamUrl, ask = Published(t.ExamUrl) })
            : Answer(Publish(examId));

        [McpServerTool(Name = "discard_draft", Title = "Discard the draft", ReadOnly = false, Destructive = true, Idempotent = false, OpenWorld = true)]
        [Description("Cancel: delete the draft exam on Gamatrain and (by default) the questions on it that this user made and that aren't in Gamatrain's question bank yet. A published exam is never deleted. Without confirmed it asks the user; confirmed=true only from that question's option (or an option that says so).")]
        public async Task<string> DiscardDraftAsync(
            [Description("The draft's examId.")] long examId,
            [Description("True only from the delete question's option.")] bool confirmed = false,
            [Description("Also delete the questions on it.")] bool deleteQuestions = true) => confirmed
            ? Answer(await examImportService.Value.DiscardAsync(GamaToken, examId, deleteQuestions), t => new { t.ExamId, t.Deleted, t.DeletedQuestions, t.Errors, ask = Discarded() })
            : Answer(Discard(examId));

        [McpServerTool(Name = "list_my_exams", Title = "My exams", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = true)]
        [Description("The user's own exams on Gamatrain. Without arguments it asks which ones; with status it lists them, 20 a page, to pick one; with examId it asks what to do with that exam (open, preview, continue or discard a draft).")]
        public async Task<string> ListMyExamsAsync(
            [Description("draft, published or all.")] string? status = null,
            [Description("One of the user's exams.")] long? examId = null,
            [Description("1 = the newest 20, 2 = the 20 before them...")] int page = 1) => (examId, status) switch
            {
                ({ } id, _) => Answer(await examImportService.Value.GetExamAsync(GamaToken, id), t => new { exam = t, ask = Exam(t) }),
                (_, null) => Answer(MyExams()),
                _ => Answer(await examImportService.Value.GetExamsAsync(GamaToken, status, page, ExamsPerPage), t => new { status, page, exams = t.List, ask = Exams(status, page, t) }),
            };

        [McpServerTool(Name = "sign_out", Title = "Sign out", ReadOnly = false, Destructive = true, Idempotent = true, OpenWorld = true)]
        [Description("Sign the user out of Gamatrain in this assistant: the connection stops working, so the next start asks them to sign in again; their drafts stay on Gamatrain. Without confirmed it asks the user; confirmed=true only from that question's option.")]
        public async Task<string> SignOutAsync([Description("True only from the sign-out question's option.")] bool confirmed = false)
        {
            if (!confirmed)
            {
                return Answer(SignOut(User.Identity?.Name));
            }

            var accessToken = httpContextAccessor.Value.HttpContext is { } context ? TokenAuthenticationHandler.GetTokenFromHeader(context.Request) : null;
            return accessToken is null
                ? Refuse("The sign-in of this request couldn't be read.")
                : Answer(await authorizationService.Value.SignOutAsync(accessToken), _ => new { signedOut = true },
                    next: "Tell the user they are signed out of Gamatrain in this assistant: the next time they start, they sign in again, and their drafts stay on Gamatrain. Call no more Gamatrain tools now.");
        }

        internal static string ReadResource(string name)
        {
            using var stream = typeof(ExamImportTools).Assembly.GetManifestResourceStream(name) ?? throw new InvalidOperationException($"Missing embedded resource {name}.");
            using StreamReader reader = new(stream);
            return reader.ReadToEnd();
        }

        /// <summary>
        /// The service's result as JSON for the AI: <c>{"ok": true, ...}</c>, or <c>{"ok": false, "message", "code",
        /// "details"}</c>. <paramref name="failure"/> turns a failure that is the user's to answer into the question to ask.
        /// </summary>
        private static string Answer<T>(ResultData<T> result, Func<T, object?>? map = null, string? next = null, Func<Error, Ask?>? failure = null)
        {
            if (result.OperationResult is not OperationResult.Succeeded)
            {
                var error = result.Errors?.FirstOrDefault() ?? default;
                var ask = failure?.Invoke(error);
                return Refuse(error.Message ?? "Something went wrong. Try again.", error.Reference, ask is null ? error.Value : null, ask);
            }

            JsonObject answer = new() { ["ok"] = true };
            var value = result.Data is { } payload && map is not null ? map(payload) : result.Data;
            var data = value is null ? null : JsonSerializer.SerializeToNode(value, JsonOptions);
            if (data is JsonObject fields)
            {
                foreach (var (name, node) in fields.ToList())
                {
                    _ = fields.Remove(name);
                    answer[name] = node;
                }
            }

            if (next is not null)
            {
                answer["next"] = next;
            }

            return answer.ToJsonString(JsonOptions);
        }

        /// <summary>A question for the user with nothing to look up first.</summary>
        private static string Answer(Ask ask) => Answer(new ResultData<Ask>(OperationResult.Succeeded) { Data = ask }, t => new { ask = t });

        /// <summary>A paper to make the exam from, and the next step: read its files, or first ask when it already has an online exam.</summary>
        private static object PaperStep(ExamImportPastPaperDto paper) => paper.ExamLinked == true
            ? new { paper, ask = ExamLinked(paper) }
            : new { paper, next = ReadPaper(paper) };

        private static string Refuse(string message, string? code = null, object? details = null, Ask? ask = null)
        {
            JsonObject answer = new()
            {
                ["ok"] = false,
                ["message"] = message,
                ["code"] = code,
                ["details"] = details is null ? null : JsonSerializer.SerializeToNode(details, JsonOptions),
            };
            if (ask is not null)
            {
                answer["ask"] = JsonSerializer.SerializeToNode(ask, JsonOptions);
            }

            return answer.ToJsonString(JsonOptions);
        }

        private static byte[]? FromBase64(string value)
        {
            var base64 = value.Trim();
            var comma = base64.StartsWith("data:", StringComparison.OrdinalIgnoreCase) ? base64.IndexOf(',', StringComparison.Ordinal) : -1;
            base64 = comma >= 0 ? base64[(comma + 1)..] : base64;
            var buffer = new byte[(base64.Length * 3 / 4) + 3];
            return base64.Length <= (MaxImageBytes / 3 * 4) + 4 && Convert.TryFromBase64String(base64, buffer, out var length) ? buffer[..length] : null;
        }
    }
}
