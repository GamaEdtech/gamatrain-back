namespace GamaEdtech.Application.Service
{
    using System.Diagnostics.CodeAnalysis;
    using System.Globalization;
    using System.Net.Http;
    using System.Text;
    using System.Text.RegularExpressions;
    using System.Threading.Tasks;

    using AngleSharp.Html.Parser;

    using GamaEdtech.Application.Interface;
    using GamaEdtech.Common.Core;
    using GamaEdtech.Common.Data;
    using GamaEdtech.Common.DataAccess.UnitOfWork;
    using GamaEdtech.Common.Service;
    using GamaEdtech.Data.Dto.Game;
    using GamaEdtech.Domain.Enumeration;
    using GamaEdtech.Infrastructure.Interface;

    using HandlebarsDotNet;

    using Microsoft.AspNetCore.Hosting;
    using Microsoft.AspNetCore.Http;
    using Microsoft.Extensions.Localization;
    using Microsoft.Extensions.Logging;

    using static GamaEdtech.Common.Core.Constants;

    public partial class ExamSerivce(Lazy<IUnitOfWorkProvider> unitOfWorkProvider, Lazy<IHttpContextAccessor> httpContextAccessor,
        Lazy<IStringLocalizer<ExamSerivce>> localizer, Lazy<ILogger<ExamSerivce>> logger, Lazy<ICoreProvider> coreProvider
        , Lazy<IWebHostEnvironment> environment, Lazy<IHeadlessBrowserRenderProvider> headlessBrowserRenderProvider
        , Lazy<IHttpClientFactory> httpClientFactory)
        : LocalizableServiceBase<ExamSerivce>(unitOfWorkProvider, httpContextAccessor, localizer, logger), IExamService
    {
        public async Task<ResultData<ExportExamResponseDto>> ExportExamAsync([NotNull] ExportExamRequestDto requestDto)
        {
            try
            {
                var info = await coreProvider.Value.GetExamInformationAsync(new()
                {
                    ExamId = requestDto.ExamId,
                    SecretKey = requestDto.SecretKey,
                });
                if (info.OperationResult is not OperationResult.Succeeded)
                {
                    return new(info.OperationResult) { Errors = info.Errors };
                }

                if (info.Data is null)
                {
                    return new(OperationResult.Failed) { Errors = [new() { Message = Localizer.Value["ExamNotFound"] },] };
                }

                info.Data.Url = requestDto.Url;
                if (requestDto.Duration.HasValue)
                {
                    info.Data.Exam!.ExamTime = requestDto.Duration.ToString();
                }

                byte[]? content = null;
                if (requestDto.FileType == ExportFileType.Pdf)
                {
                    content = await ExportPdfAsync();
                }
                else if (requestDto.FileType == ExportFileType.Word)
                {
                    content = await ExportDocumentAsync();
                }
                else if (requestDto.FileType == ExportFileType.PowerPoint)
                {
                    content = await ExportPresentationAsync();
                }

                return new(OperationResult.Succeeded)
                {
                    Data = new()
                    {
                        Content = content,
                        FileName = BuildFileName(info.Data.Exam?.Title, requestDto.ExamId),
                    },
                };

                async Task<string> BuildRenderedHtmlAsync()
                {
                    // Core wraps single-paragraph rich text in a block-level <p> (e.g. answer_a comes back
                    // as "<p>(7, 7)</p>", confirmed against exam 2050's real data) -- placed next to the
                    // inline-block A/B/C/D badge, a <p> forces itself onto its own line regardless of any
                    // CSS, since it's block-level. Unwrap when a field is entirely one paragraph so it stays
                    // inline; leave genuinely multi-paragraph content (multi-part questions) untouched.
                    // Word doesn't need this at all -- ExamWordDocumentBuilder puts each option in its own
                    // table cell natively, so a wrapping <p> there is just "one paragraph in the cell", not
                    // a forced break next to inline content.
                    if (info.Data.Tests is not null)
                    {
                        foreach (var test in info.Data.Tests)
                        {
                            test.Question = UnwrapSingleParagraph(test.Question);
                            test.OptionA = UnwrapSingleParagraph(test.OptionA);
                            test.OptionB = UnwrapSingleParagraph(test.OptionB);
                            test.OptionC = UnwrapSingleParagraph(test.OptionC);
                            test.OptionD = UnwrapSingleParagraph(test.OptionD);
                        }
                    }

                    var file = Path.Combine(environment.Value.WebRootPath, "exam.word.html");
                    var templateContent = await File.ReadAllTextAsync(file);

                    var handlebars = Handlebars.Create();
                    handlebars.RegisterHelper("inc", (output, _, args) => output.Write((int)args[0] + 1));
                    handlebars.RegisterHelper("rowBg", (output, _, args) => output.Write((int)args[0] % 2 == 0 ? "#ffffff" : "#f4f7fb"));
                    var template = handlebars.Compile(templateContent);
                    var html = template(info.Data);

                    var formulaResult = await headlessBrowserRenderProvider.Value.RenderFormulasAsync(html);
                    if (formulaResult.OperationResult == OperationResult.Succeeded && formulaResult.Data is not null)
                    {
                        return formulaResult.Data;
                    }

                    Logger.Value.LogError("Formula rendering failed for exam {ExamId}: {Errors}", requestDto.ExamId,
                        string.Join(", ", formulaResult.Errors?.Select(t => t.Message) ?? []));
                    return html;
                }

                async Task<byte[]> ExportPdfAsync()
                {
                    var html = await BuildRenderedHtmlAsync();
                    if (!string.IsNullOrEmpty(requestDto.Watermark))
                    {
                        html = InjectWatermark(html, requestDto.Watermark);
                    }

                    // Chromium's own print engine, not a separate PDF library: same real-browser rendering
                    // (fonts, formula images, header colors) as the Word export, reusing the headless
                    // Chromium instance already required for formula rendering -- no new dependency.
                    // Header/footer repeat on every physical page via Chromium's own mechanism, rather than
                    // hardcoding a page break at a fixed question count (real exams vary in length).
                    var (headerHtml, footerHtml) = BuildPdfHeaderFooter(info.Data.Exam?.Title, requestDto.Url);
                    var pdfResult = await headlessBrowserRenderProvider.Value.RenderPdfAsync(html, headerHtml, footerHtml);
                    return pdfResult.OperationResult == OperationResult.Succeeded && pdfResult.Data is not null
                        ? pdfResult.Data
                        : throw new InvalidOperationException(string.Join(", ", pdfResult.Errors?.Select(t => t.Message) ?? ["PDF rendering failed"]));
                }

                async Task RenderFormulasForWordAsync()
                {
                    if (info.Data.Tests is null || info.Data.Tests.Count == 0)
                    {
                        return;
                    }

                    var builder = new StringBuilder();
                    var fields = new List<(int TestIndex, int Field)>();
                    void AddField(int testIndex, int field, string? value)
                    {
                        if (string.IsNullOrEmpty(value))
                        {
                            return;
                        }

                        fields.Add((testIndex, field));
                        _ = builder.Append("<div id=\"f").Append(fields.Count - 1).Append("\">").Append(value).Append("</div>");
                    }

                    for (var i = 0; i < info.Data.Tests.Count; i++)
                    {
                        var test = info.Data.Tests[i];
                        AddField(i, 0, test.Question);
                        AddField(i, 1, test.OptionA);
                        AddField(i, 2, test.OptionB);
                        AddField(i, 3, test.OptionC);
                        AddField(i, 4, test.OptionD);
                    }

                    if (fields.Count == 0)
                    {
                        return;
                    }

                    var formulaResult = await headlessBrowserRenderProvider.Value.RenderFormulasAsync(builder.ToString());
                    if (formulaResult.OperationResult != OperationResult.Succeeded || formulaResult.Data is null)
                    {
                        Logger.Value.LogError("Formula rendering failed for exam {ExamId}: {Errors}", requestDto.ExamId,
                            string.Join(", ", formulaResult.Errors?.Select(t => t.Message) ?? []));
                        return;
                    }

                    var document = await new HtmlParser().ParseDocumentAsync(formulaResult.Data);
                    for (var i = 0; i < fields.Count; i++)
                    {
                        var element = document.GetElementById($"f{i}");
                        if (element is null)
                        {
                            continue;
                        }

                        var (testIndex, field) = fields[i];
                        var test = info.Data.Tests[testIndex];
                        var html = element.InnerHtml;
                        switch (field)
                        {
                            case 0:
                                test.Question = html;
                                break;
                            case 1:
                                test.OptionA = html;
                                break;
                            case 2:
                                test.OptionB = html;
                                break;
                            case 3:
                                test.OptionC = html;
                                break;
                            case 4:
                                test.OptionD = html;
                                break;
                        }
                    }
                }

                async Task<byte[]> ExportDocumentAsync()
                {
                    await RenderFormulasForWordAsync();

                    var logoPath = Path.Combine(environment.Value.WebRootPath, "exam-header-logo.jpg");
                    var logoBytes = await File.ReadAllBytesAsync(logoPath);

                    var httpClient = new Lazy<HttpClient>(() => httpClientFactory.Value.CreateHttpClient());
                    return await ExamWordDocumentBuilder.BuildAsync(info.Data, logoBytes, requestDto.Watermark, httpClient);
                }

                async Task<byte[]> ExportPresentationAsync()
                {
                    var logoPath = Path.Combine(environment.Value.WebRootPath, "exam-header-logo.jpg");
                    var logoBytes = await File.ReadAllBytesAsync(logoPath);

                    var httpClient = new Lazy<HttpClient>(() => httpClientFactory.Value.CreateHttpClient());
                    return await ExamPresentationBuilder.BuildAsync(info.Data, logoBytes, httpClient);
                }
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return new(OperationResult.Failed) { Errors = [new() { Message = exc.Message },] };
            }
        }

        [GeneratedRegex(@"^\s*<p[^>]*>(.*)</p>\s*$", RegexOptions.Singleline)]
        private static partial Regex SingleParagraphRegex();

        /// <summary>
        /// Strips a single wrapping &lt;p&gt;...&lt;/p&gt; when that's the field's entire content, so it can
        /// sit inline next to something else (e.g. an option-letter badge) without forcing a block-level
        /// line break. Leaves multi-paragraph content untouched -- those breaks are meant to happen. Used by
        /// the Pdf/HTML path only -- Word's native OOXML builder doesn't need this (see comment above).
        /// </summary>
        private static string? UnwrapSingleParagraph(string? html)
        {
            if (string.IsNullOrEmpty(html))
            {
                return html;
            }

            var match = SingleParagraphRegex().Match(html);
            return match.Success ? match.Groups[1].Value : html;
        }

        /// <summary>
        /// Inserts a diagonal, semi-transparent watermark <c>&lt;div&gt;</c> right after the opening
        /// &lt;body&gt; tag. Uses <c>position:fixed</c> deliberately -- Chromium's print engine repeats a
        /// fixed-position element on every printed page, unlike <c>absolute</c> which only appears once.
        /// </summary>
        private static string InjectWatermark(string html, string watermarkText)
        {
            var encoded = System.Net.WebUtility.HtmlEncode(watermarkText);
            var watermarkHtml =
                "<div style=\"position:fixed;top:45%;left:15%;transform:rotate(-30deg);font-size:60px;" +
                "font-weight:bold;color:#172437;opacity:0.15;z-index:9999;pointer-events:none;white-space:nowrap;\">" +
                encoded + "</div>";

            var bodyIndex = html.IndexOf("<body", StringComparison.OrdinalIgnoreCase);
            if (bodyIndex < 0)
            {
                return watermarkHtml + html;
            }

            var bodyTagEnd = html.IndexOf('>', bodyIndex) + 1;
            return html[..bodyTagEnd] + watermarkHtml + html[bodyTagEnd..];
        }

        /// <summary>
        /// Builds the per-page header/footer templates for <see cref="IHeadlessBrowserRenderProvider.RenderPdfAsync"/>
        /// -- Chromium repeats these on every physical page automatically (via <c>pageNumber</c>/
        /// <c>totalPages</c> classes it injects), which is how a real page count/number is achieved without
        /// hardcoding a page break at a fixed question index.
        /// </summary>
        private static (string Header, string Footer) BuildPdfHeaderFooter(string? examTitle, string? baseUrl)
        {
            var title = System.Net.WebUtility.HtmlEncode(examTitle ?? string.Empty);
            var logoUrl = System.Net.WebUtility.HtmlEncode($"{baseUrl}/exam-header-logo.jpg");

            var header = "<div style=\"width:100%;font-size:9px;padding:6px 24px 4px 24px;box-sizing:border-box;" +
                "display:flex;align-items:center;justify-content:space-between;font-family:Arial,Helvetica,sans-serif;" +
                "color:#172033;border-bottom:2px solid #f6b500;\">" +
                $"<div style=\"display:flex;align-items:center;\"><img src=\"{logoUrl}\" width=\"16\" height=\"16\" style=\"display:block;margin-right:6px;\" /><span style=\"font-weight:bold;color:#172437;font-size:11px;\">gamatrain</span></div>" +
                $"<span style=\"font-weight:bold;font-size:10px;color:#21324a;\">{title}</span></div>";

            var footer = "<div style=\"width:100%;font-size:9px;padding:4px 24px 6px 24px;box-sizing:border-box;" +
                "display:flex;align-items:center;justify-content:space-between;font-family:Arial,Helvetica,sans-serif;" +
                "color:#5b6777;border-top:2px solid #f6b500;\">" +
                "<span>&copy; gamatrain</span>" +
                "<span style=\"color:#172033;font-weight:bold;\">gamatrain.com</span>" +
                "<span>Page <span class=\"pageNumber\"></span> of <span class=\"totalPages\"></span></span></div>";

            return (header, footer);
        }

        /// <summary>
        /// Builds a filesystem-safe download file name from the exam title, falling back to the exam id
        /// when the title is missing/blank or turns out empty after stripping invalid characters.
        /// </summary>
        private static string BuildFileName(string? title, long examId)
        {
            if (string.IsNullOrWhiteSpace(title))
            {
                return examId.ToString(CultureInfo.InvariantCulture);
            }

            var invalidChars = Path.GetInvalidFileNameChars();
            var sanitized = new string([.. title.Select(t => invalidChars.Contains(t) ? ' ' : t)]).Trim();
            sanitized = InvalidFileNameSpacingRegex().Replace(sanitized, " ").Trim();
            if (sanitized.Length > 100)
            {
                sanitized = sanitized[..100].Trim();
            }

            return sanitized.Length > 0 ? sanitized : examId.ToString(CultureInfo.InvariantCulture);
        }

        [GeneratedRegex(@"\s+")]
        private static partial Regex InvalidFileNameSpacingRegex();
    }
}
