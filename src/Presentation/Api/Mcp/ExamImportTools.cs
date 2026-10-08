namespace GamaEdtech.Presentation.Api.Mcp
{
    using System.ComponentModel;
    using System.ComponentModel.DataAnnotations;
    using System.Net;
    using System.Net.Sockets;
    using System.Security.Claims;
    using System.Text.Encodings.Web;
    using System.Text.Json;
    using System.Text.Json.Nodes;
    using System.Text.Json.Serialization;

    using GamaEdtech.Application.Interface;
    using GamaEdtech.Common.Core;
    using GamaEdtech.Common.Data;
    using GamaEdtech.Data.Dto.ExamImport;
    using GamaEdtech.Presentation.ViewModel.Exam;

    using ModelContextProtocol.Protocol;
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

        private const int MaxImageBytes = 5 * 1024 * 1024;

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

        [McpServerTool(Name = "add_figure", Title = "Add a figure", ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = true)]
        [McpMeta("openai/fileParams", JsonValue = """["file"]""")]
        [Description("Store one image (a diagram, graph, picture or table you cut from the paper or the mark scheme, PNG or JPEG) and get its figureId for a question's figure, optionFigures or answerFigure. Give ONE of: file = the image file in the chat; link = a public http(s) link to it; contentBase64 = its bytes (small images only). With a shell, upload many images faster through get_figure_upload_link. One image per question: combine several figures into one image first.")]
        public async Task<string> AddFigureAsync(
            [Description("The image file, attached in the chat.")] McpFileViewModel? file = null,
            [Description("A public http(s) link to the image.")] string? link = null,
            [Description("The image bytes in base64 (a data: URL works too). Small images only.")] string? contentBase64 = null,
            [Description("A name for the image, e.g. q3-graph.png.")] string? fileName = null,
            CancellationToken cancellationToken = default)
        {
            byte[]? content;
            link = file?.DownloadUrl ?? link;
            if (!string.IsNullOrWhiteSpace(contentBase64))
            {
                content = FromBase64(contentBase64);
                if (content is null)
                {
                    return Refuse("contentBase64 is not valid base64, or the image is larger than 5 MB.");
                }
            }
            else if (!string.IsNullOrWhiteSpace(link))
            {
                (content, var error) = await DownloadImageAsync(link, cancellationToken);
                if (content is null)
                {
                    return Refuse(error ?? "The image could not be downloaded.");
                }
            }
            else
            {
                return Refuse("Give the image as file, link or contentBase64.");
            }

            var name = fileName ?? file?.FileName ?? (Uri.TryCreate(link, UriKind.Absolute, out var uri) ? Path.GetFileName(uri.AbsolutePath) : null);
            return Answer(await examImportService.Value.AddFigureAsync(new() { UserId = UserId, Content = content, Name = name }));
        }

        [McpServerTool(Name = "get_figure_upload_link", Title = "Figure upload link", ReadOnly = true, Destructive = false, Idempotent = false, OpenWorld = false)]
        [Description("For assistants that can run shell commands (Claude Code, Codex): a link to upload figure images straight to the server instead of through the chat. POST each PNG or JPEG as the multipart field file, e.g. curl -F file=@q3.png <uploadUrl>; each answer is JSON with the figureId. The link works for 2 hours.")]
        public string GetFigureUploadLink() =>
            Answer(examImportService.Value.GetFigureUploadLink(UserId), t => new { uploadUrl = t, example = $"curl -F file=@q3-graph.png {t}" });

        [McpServerTool(Name = "save_questions", Title = "Save questions", ReadOnly = false, Destructive = false, Idempotent = true, OpenWorld = false)]
        [Description("Store the questions you extracted, in paper order. The same number replaces the earlier one, so to change a question save it again (skip=true leaves it out of the upload). Save in batches, a few pages at a time. The result lists problems per question: error = cannot be uploaded until fixed or skipped, review = a human should look. replaceAll=true clears the list first.")]
        public async Task<string> SaveQuestionsAsync(
            [Description("The questions, in paper order.")] IReadOnlyList<ExamImportQuestionDto> questions,
            [Description("Clear the saved questions first.")] bool replaceAll = false) =>
            Answer(await examImportService.Value.SaveQuestionsAsync(UserId, questions, replaceAll));

        [McpServerTool(Name = "remove_question", Title = "Remove a question", ReadOnly = false, Destructive = true, Idempotent = true, OpenWorld = false)]
        [Description("Delete a question from the import (it was not a real question, or a duplicate).")]
        public async Task<string> RemoveQuestionAsync([Description("The question's number.")] string number) =>
            Answer(await examImportService.Value.RemoveQuestionAsync(UserId, number));

        [McpServerTool(Name = "review_summary", Title = "Review summary", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
        [Description("Totals for the review step: questions found, by type, ready / needs review / must fix / skipped, missing answers, missing figures and missing exam details. details=true adds every problem.")]
        public async Task<string> ReviewSummaryAsync([Description("List every problem too.")] bool details = true) =>
            Answer(await examImportService.Value.GetReviewAsync(UserId, details));

        [McpServerTool(Name = "show_preview", Title = "Preview the exam", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
        [McpMeta("ui", JsonValue = $$"""{"resourceUri":"{{ExamImportPreviewWidget.ResourceUri}}"}""")]
        [McpMeta("ui/resourceUri", ExamImportPreviewWidget.ResourceUri)]
        [McpMeta("openai/outputTemplate", ExamImportPreviewWidget.ResourceUri)]
        [McpMeta("openai/toolInvocation/invoking", "Building the preview…")]
        [McpMeta("openai/toolInvocation/invoked", "Preview ready")]
        [Description("Show every question as it will look (text, formulas, figures, options, the correct answer, answers, flags): in ChatGPT as a card in the chat, and everywhere as a full page (previewUrl). Nothing is uploaded.")]
        public async Task<CallToolResult> ShowPreviewAsync()
        {
            var result = await examImportService.Value.GetPreviewAsync(UserId);
            return result.Data is { Summary: { } summary } preview ? PreviewResult(summary, preview) : new() { Content = [new TextContentBlock { Text = Answer(result) }] };

            static CallToolResult PreviewResult(ExamImportSummaryDto summary, ExamImportPreviewDto preview) => new()
            {
                Content =
                [
                    new TextContentBlock
                    {
                        Text = $"Preview of {summary.QuestionsFound} questions: {summary.Ready.Count} ready, {summary.NeedsReview.Count} need review, "
                            + $"{summary.Blocked.Count} must be fixed, {summary.Skipped.Count} skipped. Full page: {preview.PreviewUrl}",
                    },
                ],
                StructuredContent = JsonSerializer.SerializeToElement(new { summary, previewUrl = preview.PreviewUrl }, JsonOptions),
                Meta = new JsonObject { ["gamatrain/preview"] = JsonSerializer.SerializeToNode(preview, JsonOptions) },
            };
        }

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

        /// <summary>
        /// Downloads an image from a link the AI gave (a ChatGPT file link or a public URL). The connection is made only
        /// to a public address, checked on the address actually connected to (so a DNS answer or a redirect can't reach
        /// this server's own network), and the size is capped.
        /// </summary>
        private static async Task<(byte[]? Content, string? Error)> DownloadImageAsync(string link, CancellationToken cancellationToken)
        {
            if (!Uri.TryCreate(link, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
            {
                return (null, "The link must be an http(s) URL.");
            }

            using SocketsHttpHandler handler = new()
            {
                UseProxy = false,
                MaxAutomaticRedirections = 3,
                ConnectTimeout = TimeSpan.FromSeconds(10),
                ConnectCallback = ConnectToPublicAddressAsync,
            };
            using HttpClient client = new(handler) { Timeout = TimeSpan.FromSeconds(30) };
            try
            {
                using var response = await client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                if (!response.IsSuccessStatusCode)
                {
                    return (null, $"The link answered HTTP {(int)response.StatusCode}.");
                }

                await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
                using MemoryStream content = new();
                var chunk = new byte[81920];
                int read;
                while ((read = await stream.ReadAsync(chunk, cancellationToken)) > 0)
                {
                    await content.WriteAsync(chunk.AsMemory(0, read), cancellationToken);
                    if (content.Length > MaxImageBytes)
                    {
                        return (null, "The image is larger than 5 MB.");
                    }
                }

                return (content.ToArray(), null);
            }
            catch (HttpRequestException exc)
            {
                return (null, $"The image could not be downloaded: {exc.Message}");
            }
            catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                return (null, "Downloading the image timed out.");
            }
        }

        private static async ValueTask<Stream> ConnectToPublicAddressAsync(SocketsHttpConnectionContext context, CancellationToken cancellationToken)
        {
            var addresses = await Dns.GetHostAddressesAsync(context.DnsEndPoint.Host, cancellationToken);
            var address = Array.Find(addresses, IsPublicAddress) ?? throw new HttpRequestException("The link points to a private network address.");
#pragma warning disable CA2000 // Dispose objects before losing scope: the NetworkStream owns it, and it is disposed below on failure
            Socket socket = new(SocketType.Stream, ProtocolType.Tcp);
#pragma warning restore CA2000 // Dispose objects before losing scope
            try
            {
                socket.NoDelay = true;
                await socket.ConnectAsync(address, context.DnsEndPoint.Port, cancellationToken);
                return new NetworkStream(socket, ownsSocket: true);
            }
            catch (Exception)
            {
                socket.Dispose();
                throw;
            }
        }

        private static bool IsPublicAddress(IPAddress address)
        {
            var ip = address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address;
            if (IPAddress.IsLoopback(ip) || ip.Equals(IPAddress.Any) || ip.Equals(IPAddress.IPv6Any))
            {
                return false;
            }

            if (ip.AddressFamily == AddressFamily.InterNetworkV6)
            {
                return !(ip.IsIPv6LinkLocal || ip.IsIPv6SiteLocal || ip.IsIPv6UniqueLocal || ip.IsIPv6Multicast);
            }

            var bytes = ip.GetAddressBytes();
            return bytes[0] switch
            {
                0 or 10 or 127 or >= 224 => false,
                100 => bytes[1] is < 64 or > 127,
                169 => bytes[1] != 254,
                172 => bytes[1] is < 16 or > 31,
                192 => bytes[1] != 168 && !(bytes[1] == 0 && bytes[2] is 0 or 2),
                198 => bytes[1] is not (18 or 19),
                _ => true,
            };
        }
    }
}
