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
    using GamaEdtech.Common.Core;
    using GamaEdtech.Common.Data;
    using GamaEdtech.Data.Dto.ExamImport;

    using ModelContextProtocol.Server;

    using static GamaEdtech.Common.Core.Constants;

    /// <summary>
    /// The MCP tools an AI assistant (ChatGPT, Claude, Codex...) uses to import a past paper into gamatrain as questions
    /// and an online exam, served at <c>/mcp</c>. The AI reads the paper and the mark scheme itself and hands over what
    /// it extracted; these only take the caller from the MCP access token, call <see cref="IExamImportService"/> and
    /// answer JSON text for the AI. See docs/business/exams-and-content.md, "Exam import (MCP)".
    /// </summary>
    [McpServerToolType]
    public sealed class ExamImportTools(Lazy<IExamImportService> examImportService, Lazy<IHttpContextAccessor> httpContextAccessor)
    {
        public const string Instructions = """
            Imports a past paper (PDF or Word) into Gamatrain as questions and an online exam. Call get_import_guide first
            and follow it. You read the paper and the mark scheme yourself and extract the questions; these tools store
            what you extracted, check it, preview it and upload it. Guide the user one step at a time in plain language
            and ask before anything is written to Gamatrain.
            """;

        private static readonly Lazy<string> Guide = new(() => ReadResource("ExamImportGuide.md"));

        private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        };

        private ClaimsPrincipal User => httpContextAccessor.Value.HttpContext?.User ?? new();

        private long UserId => User.UserId();

        private string GamaToken => User.FindFirstValue(McpTokenAuthenticationHandler.GamaTokenClaim) ?? string.Empty;

        [McpServerTool(Name = "get_import_guide", Title = "Import guide", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
        [Description("Read this FIRST, once per conversation: the step-by-step flow for importing a paper into Gamatrain and the exact rules for extracting questions (types, multi-part questions, math, figures, answers).")]
        public static string ReadImportGuide() => Guide.Value;

        [McpServerTool(Name = "session_status", Title = "Import status", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
        [Description("Where the import stands: the signed-in account, exam details, question counts, figures and upload progress. Call it at the start of a conversation and to resume after an interruption.")]
        public async Task<string> SessionStatusAsync()
        {
            var result = await examImportService.Value.GetStatusAsync(UserId);
            if (result.Data is { } status)
            {
                status.User = User.Identity?.Name;
            }

            return Answer(result);
        }

        [McpServerTool(Name = "start_new_import", Title = "Start a new import", ReadOnly = false, Destructive = true, Idempotent = true, OpenWorld = false)]
        [Description("Forget the current import (exam details, extracted questions, figures, upload progress) to start a new paper. Nothing on Gamatrain changes. Ask the user first when there is unfinished work.")]
        public async Task<string> StartNewImportAsync([Description("True only after the user agreed.")] bool confirmed) => confirmed
            ? Answer(await examImportService.Value.StartNewAsync(UserId), next: "A new import has started.")
            : Refuse("Ask the user to confirm starting over, then call start_new_import(confirmed=true).");

        [McpServerTool(Name = "list_options", Title = "List exam detail options", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = true)]
        [Description("Gamatrain's choices for the exam details, with ids: board (e.g. Cambridge, Edexcel) → grade (parentId = board id, e.g. IGCSE, AS & A Level) → subject (parentId = grade id) → topic (parentId = subject id). course exists only for some boards (parentId = board id). paper = the exam type (Paper 1..6, Topical...). search ranks by similarity. Show the user a few suggestions to pick from.")]
        public async Task<string> ListOptionsAsync(
            [Description("board, grade, course, subject, topic or paper.")][AllowedValues("board", "grade", "course", "subject", "topic", "paper")] string kind,
            [Description("The parent's id: the board for grade/course, the grade for subject, the subject for topic.")] int? parentId = null,
            [Description("What you read on the paper, to rank the options.")] string? search = null)
        {
            var result = await examImportService.Value.GetOptionsAsync(UserId, GamaToken, kind, parentId, search);
            return Answer(result, t => new { kind, options = t });
        }

        [McpServerTool(Name = "set_exam_details", Title = "Set exam details", ReadOnly = false, Destructive = false, Idempotent = true, OpenWorld = true)]
        [Description("Save the exam details; only the given fields change, and the ids (from list_options) are checked. When the subject is set its topics are returned: give every question a topicId from them.")]
        public async Task<string> SetExamDetailsAsync(ExamImportDetailsRequestDto details) =>
            Answer(await examImportService.Value.SetDetailsAsync(UserId, GamaToken, details));

        [McpServerTool(Name = "find_past_papers", Title = "Find the past paper", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = true)]
        [Description("Look for the matching past paper already on Gamatrain (same board, grade, subject, year and session) so the exam can be linked to it with set_exam_details(pastPaperId). Ask the user before linking.")]
        public async Task<string> FindPastPapersAsync() =>
            Answer(await examImportService.Value.FindPastPapersAsync(UserId, GamaToken), t => new { papers = t });

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
    }
}
