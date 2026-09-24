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

                async Task<byte[]> ExportPdfAsync()
                {
                    // Same layout as the Word export (ExamPdfHtmlBuilder reuses ExamWordDocumentBuilder's own
                    // measurements, layout rules and header shapes), printed by Chromium. Formulas are MathJax
                    // images here rather than Word's native equations -- rendered on the body alone, since the
                    // render returns a fragment, then wrapped into the full page document.
                    var page = await ExamPdfHtmlBuilder.BuildAsync(info.Data, await LoadBrandAssetsAsync(), requestDto.Watermark);
                    var body = page.BodyHtml;
                    var formulaResult = await headlessBrowserRenderProvider.Value.RenderFormulasAsync(body);
                    if (formulaResult.OperationResult == OperationResult.Succeeded && formulaResult.Data is not null)
                    {
                        body = formulaResult.Data;
                    }
                    else
                    {
                        Logger.Value.LogError("Formula rendering failed for exam {ExamId}: {Errors}", requestDto.ExamId,
                            string.Join(", ", formulaResult.Errors?.Select(t => t.Message) ?? []));
                    }

                    var pdfResult = await headlessBrowserRenderProvider.Value.RenderPdfAsync(
                        ExamPdfHtmlBuilder.WrapDocument(body), page.HeaderTemplate, page.FooterTemplate, page.MarginTop, page.MarginBottom, page.MarginSide);
                    return pdfResult.OperationResult == OperationResult.Succeeded && pdfResult.Data is not null
                        ? pdfResult.Data
                        : throw new InvalidOperationException(string.Join(", ", pdfResult.Errors?.Select(t => t.Message) ?? ["PDF rendering failed"]));
                }

                async Task<HeaderBrandAssets> LoadBrandAssetsAsync() => new(
                    GamaWordmark: await File.ReadAllBytesAsync(Path.Combine(environment.Value.WebRootPath, "exam-gama-wordmark.png")),
                    ProfilePlaceholder: await File.ReadAllBytesAsync(Path.Combine(environment.Value.WebRootPath, "exam-profile-placeholder.png")),
                    FooterWave: await File.ReadAllBytesAsync(Path.Combine(environment.Value.WebRootPath, "exam-footer-wave.png")),
                    FooterGlobe: await File.ReadAllBytesAsync(Path.Combine(environment.Value.WebRootPath, "exam-footer-globe.png")));

                async Task RenderFormulasToOmmlInPlaceAsync()
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

                    var formulaResult = await headlessBrowserRenderProvider.Value.RenderFormulasToOmmlAsync(builder.ToString());
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
                    await RenderFormulasToOmmlInPlaceAsync();

                    var brandAssets = await LoadBrandAssetsAsync();

                    var httpClient = new Lazy<HttpClient>(() => httpClientFactory.Value.CreateHttpClient());
                    var document = await ExamWordDocumentBuilder.BuildAsync(info.Data, brandAssets, requestDto.Watermark, httpClient, requestDto.GoogleDocsCompatible);
                    return requestDto.GoogleDocsCompatible ? GoogleDocsDocxSanitizer.StripUnsupportedShapes(document) : document;
                }

                async Task<byte[]> ExportPresentationAsync()
                {
                    await RenderFormulasToOmmlInPlaceAsync();

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
