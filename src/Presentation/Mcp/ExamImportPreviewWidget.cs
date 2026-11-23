namespace GamaEdtech.Presentation.Mcp
{
    using System.ComponentModel;
    using System.Text.Json.Nodes;

    using GamaEdtech.Application.Interface;

    using Microsoft.Extensions.Configuration;

    using ModelContextProtocol.Protocol;
    using ModelContextProtocol.Server;

    /// <summary>
    /// The in-chat preview card (an MCP Apps widget) the <c>show_preview</c> tool's result is shown in: ChatGPT renders it
    /// with the tool result's <c>_meta["gamatrain/preview"]</c>. Its <c>_meta</c> has the MCP Apps keys and ChatGPT's older
    /// <c>openai/*</c> aliases; the question images load from this backend's CDN (<c>Core:Cdn</c>) or gama-api's (<c>Core:Exam</c>), MathJax from cdnjs.
    /// </summary>
    [McpServerResourceType]
    public sealed class ExamImportPreviewWidget(Lazy<IMcpAuthorizationService> authorizationService, Lazy<IConfiguration> configuration)
    {
#pragma warning disable S1075 // an MCP resource URI, not an endpoint
        public const string ResourceUri = "ui://gamatrain/exam-preview.html";
#pragma warning restore S1075

        private const string MimeType = "text/html;profile=mcp-app";
        private const string Summary = "Preview of the extracted exam questions with formulas, figures and answers.";

        private static readonly Lazy<string> Html = new(() => ExamImportTools.ReadResource("ExamImportWidget.html"));

        [McpServerResource(UriTemplate = ResourceUri, Name = "exam-preview", Title = "Gamatrain exam preview", MimeType = MimeType)]
        [Description(Summary)]
        public ReadResourceResult Read()
        {
            var origin = authorizationService.Value.GetMetadata().Issuer;
            // Question images: this backend's CDN (Core:Cdn, the ExamImage container) since exams are written here, and gama-api's
            // (Core:Exam) for an older question's image link.
            string[] images = [.. new[] { configuration.Value.GetValue<string>("Core:Cdn"), configuration.Value.GetValue<string>("Core:Exam") }
                .Where(t => Uri.TryCreate(t, UriKind.Absolute, out _)).Select(t => new Uri(t!).GetLeftPart(UriPartial.Authority)).Distinct()];
#pragma warning disable S1075 // the CDN the widget loads MathJax from, fixed in its HTML too
            JsonArray Domains() => ["https://cdnjs.cloudflare.com", .. images.Select(t => JsonValue.Create(t))];
#pragma warning restore S1075
            return new()
            {
                Contents =
                [
                    new TextResourceContents
                    {
                        Uri = ResourceUri,
                        MimeType = MimeType,
                        Text = Html.Value,
                        Meta = new JsonObject
                        {
                            ["ui"] = new JsonObject
                            {
                                ["csp"] = new JsonObject { ["resourceDomains"] = Domains(), ["connectDomains"] = new JsonArray() },
                                ["domain"] = origin,
                                ["prefersBorder"] = true,
                            },
                            ["openai/widgetCSP"] = new JsonObject { ["resource_domains"] = Domains(), ["connect_domains"] = new JsonArray() },
                            ["openai/widgetDomain"] = origin,
                            ["openai/widgetPrefersBorder"] = true,
                            ["openai/widgetDescription"] = Summary,
                        },
                    },
                ],
            };
        }
    }
}
