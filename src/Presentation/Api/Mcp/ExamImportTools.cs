namespace GamaEdtech.Presentation.Api.Mcp
{
    using System.ComponentModel;
    using System.ComponentModel.DataAnnotations;
    using System.Security.Claims;
    using System.Text.Encodings.Web;
    using System.Text.Json;
    using System.Text.Json.Nodes;
    using System.Text.Json.Serialization;

    using GamaEdtech.Application.Interface;
    using GamaEdtech.Common.Data;
    using GamaEdtech.Data.Dto.ExamImport;
    using GamaEdtech.Presentation.ViewModel.Exam;

    using ModelContextProtocol.Protocol;
    using ModelContextProtocol.Server;

    using static GamaEdtech.Common.Core.Constants;

    /// <summary>
    /// The MCP tools an AI assistant (ChatGPT, Claude, Codex...) uses to import a past paper into gamatrain as questions
    /// and an online exam, served at <c>/mcp</c>. The AI reads the paper and the mark scheme itself and hands over what
    /// it extracted; these only take the caller's gama-api token from the MCP access token, call
    /// <see cref="IExamImportService"/> and answer JSON text for the AI. See docs/business/exams-and-content.md, "Exam import (MCP)".
    /// </summary>
    [McpServerToolType]
    public sealed class ExamImportTools(Lazy<IExamImportService> examImportService, Lazy<IHttpContextAccessor> httpContextAccessor)
    {
        public const string Instructions = """
            Imports a past paper (PDF or Word) into Gamatrain as questions and an online exam. Call get_import_guide first
            and follow it. You read the paper and the mark scheme yourself and extract the questions; these tools check
            what you extracted and save it straight into a draft exam on Gamatrain, which the user previews and publishes.
            Gamatrain staff can also start from a paper already on Gamatrain (list_recent_papers, load_paper). Guide the
            user one step at a time in plain language and ask before creating the draft and before publishing.
            """;

        private const int MaxImageBytes = 5 * 1024 * 1024;

        private static readonly Lazy<string> Guide = new(() => ReadResource("ExamImportGuide.md"));

        private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        };

        private ClaimsPrincipal User => httpContextAccessor.Value.HttpContext?.User ?? new();

        private string GamaToken => User.FindFirstValue(McpTokenAuthenticationHandler.GamaTokenClaim) ?? string.Empty;

        [McpServerTool(Name = "get_import_guide", Title = "Import guide", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
        [Description("Read this FIRST, once per conversation: the step-by-step flow for importing a paper into Gamatrain and the exact rules for extracting questions (types, multi-part questions, math, figures, answers).")]
        public static string ReadImportGuide() => Guide.Value;

        [McpServerTool(Name = "session_status", Title = "Import status", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = true)]
        [Description("The signed-in account (staff = a Gamatrain admin or sub-admin, who can also start from a paper on Gamatrain) and the user's unpublished draft exam on Gamatrain, if any: its examId, details and how many questions it has. Call it at the start of a conversation and to resume after an interruption.")]
        public async Task<string> SessionStatusAsync()
        {
            var result = await examImportService.Value.GetStatusAsync(GamaToken);
            if (result.Data is { } status)
            {
                status.User = User.Identity?.Name;
            }

            return Answer(result);
        }

        [McpServerTool(Name = "list_options", Title = "List exam detail options", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = true)]
        [Description("Gamatrain's choices for the exam details, with ids: board (e.g. Cambridge, Edexcel) → grade (parentId = board id, e.g. IGCSE, AS & A Level) → subject (parentId = grade id; courseId for boards with courses) → topic (parentId = subject id). course exists only for some boards (parentId = board id). paper = the exam type (Paper 1..6, Topical...). search ranks by similarity. Show the user a few suggestions to pick from.")]
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
        [Description("Create the DRAFT exam on Gamatrain with these details (not published; students can't see it), or change the details of a draft (examId). Give every detail each time; the ids (from list_options) are checked. Only after the user confirmed the details (details from load_paper need no confirmation). Returns the draft's examId, for every other tool, and the subject's topics: give every question a topicId from them.")]
        public async Task<string> SetExamDetailsAsync(ExamImportDetailsRequestDto details) =>
            Answer(await examImportService.Value.SetDetailsAsync(GamaToken, details));

        [McpServerTool(Name = "find_past_papers", Title = "Find the past paper", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = true)]
        [Description("Look for the matching past paper already on Gamatrain (same board, grade, subject, year and session) so the exam can be linked to it with set_exam_details(pastPaperId) when the draft is created. Ask the user before linking.")]
        public async Task<string> FindPastPapersAsync(
            [Description("Board id.")] int boardId,
            [Description("Grade id.")] int gradeId,
            [Description("Subject id.")] int subjectId,
            [Description("The paper's year.")] int? year = null,
            [Description("3 = February/March, 6 = May/June, 11 = October/November.")] int? sessionMonth = null) =>
            Answer(await examImportService.Value.FindPastPapersAsync(GamaToken, boardId, gradeId, subjectId, year, sessionMonth), t => new { papers = t });

        [McpServerTool(Name = "list_recent_papers", Title = "Latest papers on Gamatrain", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = true)]
        [Description("Staff only (session_status staff=true): the papers added to Gamatrain most recently, newest first, 20 a page, with their board, grade, subject, session and files. Show them as a numbered list right after the start so the user can pick the paper to make the exam from (load_paper).")]
        public async Task<string> ListRecentPapersAsync([Description("1 = the newest papers, 2 = the 20 before them...")] int page = 1) =>
            Answer(await examImportService.Value.GetRecentPapersAsync(GamaToken, page), t => new { page, papers = t });

        [McpServerTool(Name = "load_paper", Title = "Start from a Gamatrain paper", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = true)]
        [Description("Staff only: make the exam from a paper already on Gamatrain (its id, e.g. from list_recent_papers). Gives the paper's exam details (boardId, gradeId, courseId, subjectId, paperId, year, month = sessionMonth, id = pastPaperId) for set_exam_details, and a temporary download link (about 1 hour) to each of its files: the question paper (PDF and/or Word), the mark scheme and any extra files. Read ALL of them. Call it again for fresh links.")]
        public async Task<string> LoadPaperAsync([Description("The paper's id on Gamatrain.")] long paperId) =>
            Answer(await examImportService.Value.LoadPaperAsync(GamaToken, paperId), t => new { paper = t }, next: "If paper.examLinked is true, tell the user the paper already has an online exam and ask before going on. Read every file now: with a shell, download it (curl -L -o <name> <url>) and open it; otherwise open the link. If you can't open links, ask the user to download the files and attach them to the chat. Don't ask the user to confirm the exam details: they come from the paper. Read the duration and the component code from the question paper's cover, then call set_exam_details with the paper's details (pastPaperId = paper.id).");

        [McpServerTool(Name = "add_figure", Title = "Add a figure", ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = true)]
        [McpMeta("openai/fileParams", JsonValue = """["file"]""")]
        [Description("Upload one image (a diagram, graph, picture or table you cut from the paper or the mark scheme, PNG or JPEG) to Gamatrain and get its figure key for ONE question's figure, optionFigures or answerFigure (a key works once). Give ONE of: file = the image file in the chat; link = a public http(s) link to it; contentBase64 = its bytes (small images only). With a shell, upload many images faster through get_figure_upload_link. One image per question: combine several figures into one image first.")]
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
            [Description("The questions, in paper order.")] IReadOnlyList<ExamImportQuestionDto> questions) =>
            Answer(await examImportService.Value.SaveQuestionsAsync(GamaToken, examId, questions));

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
        [Description("Show every question on the draft as Gamatrain stores it (text, formulas, figures, options, the correct answer, answers): in ChatGPT as a card in the chat, and everywhere as the draft's page on Gamatrain (previewUrl).")]
        public async Task<CallToolResult> ShowPreviewAsync([Description("The draft's examId.")] long examId)
        {
            var result = await examImportService.Value.GetPreviewAsync(GamaToken, examId);
            return result.Data is { } preview ? PreviewResult(preview) : new() { Content = [new TextContentBlock { Text = Answer(result) }] };

            static CallToolResult PreviewResult(ExamImportPreviewDto preview) => new()
            {
                Content = [new TextContentBlock { Text = $"Preview of the {preview.Questions?.Count ?? 0} questions on the draft. Full page: {preview.PreviewUrl}" }],
                StructuredContent = JsonSerializer.SerializeToElement(new { questions = preview.Questions?.Count ?? 0, previewUrl = preview.PreviewUrl }, JsonOptions),
                Meta = new JsonObject { ["gamatrain/preview"] = JsonSerializer.SerializeToNode(preview, JsonOptions) },
            };
        }

        [McpServerTool(Name = "publish_exam", Title = "Publish the exam", ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = true)]
        [Description("Publish the draft exam so students can take it. Only after the user saw the draft and confirmed (confirmed=true).")]
        public async Task<string> PublishExamAsync(
            [Description("The draft's examId.")] long examId,
            [Description("True only after the user's final confirmation.")] bool confirmed) => confirmed
            ? Answer(await examImportService.Value.PublishAsync(GamaToken, examId))
            : Refuse("Ask the user for final confirmation, then call publish_exam(confirmed=true).");

        [McpServerTool(Name = "discard_draft", Title = "Discard the draft", ReadOnly = false, Destructive = true, Idempotent = false, OpenWorld = true)]
        [Description("Cancel: delete the draft exam on Gamatrain and (by default) the questions on it that this user made and that aren't in Gamatrain's question bank yet. A published exam is never deleted. Only with the user's explicit confirmation.")]
        public async Task<string> DiscardDraftAsync(
            [Description("The draft's examId.")] long examId,
            [Description("True only after the user explicitly agreed.")] bool confirmed,
            [Description("Also delete the questions on it.")] bool deleteQuestions = true) => confirmed
            ? Answer(await examImportService.Value.DiscardAsync(GamaToken, examId, deleteQuestions))
            : Refuse("Ask the user to confirm deleting the draft first.");

        internal static string ReadResource(string name)
        {
            using var stream = typeof(ExamImportTools).Assembly.GetManifestResourceStream(name) ?? throw new InvalidOperationException($"Missing embedded resource {name}.");
            using StreamReader reader = new(stream);
            return reader.ReadToEnd();
        }

        /// <summary>The service's result as JSON for the AI: <c>{"ok": true, ...}</c>, or <c>{"ok": false, "message", "code", "details"}</c>.</summary>
        private static string Answer<T>(ResultData<T> result, Func<T, object?>? map = null, string? next = null)
        {
            if (result.OperationResult is not OperationResult.Succeeded)
            {
                var error = result.Errors?.FirstOrDefault() ?? default;
                return Refuse(error.Message ?? "Something went wrong. Try again.", error.Reference, error.Value);
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

        private static string Refuse(string message, string? code = null, object? details = null) => new JsonObject
        {
            ["ok"] = false,
            ["message"] = message,
            ["code"] = code,
            ["details"] = details is null ? null : JsonSerializer.SerializeToNode(details, JsonOptions),
        }.ToJsonString(JsonOptions);

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
