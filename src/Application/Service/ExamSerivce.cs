namespace GamaEdtech.Application.Service
{
    using System.Diagnostics.CodeAnalysis;
    using System.Drawing;
    using System.Globalization;
    using System.Text.RegularExpressions;
    using System.Threading.Tasks;

    using DocumentFormat.OpenXml.Packaging;

    using GamaEdtech.Application.Interface;
    using GamaEdtech.Common.Core;
    using GamaEdtech.Common.Data;
    using GamaEdtech.Common.DataAccess.UnitOfWork;
    using GamaEdtech.Common.Service;
    using GamaEdtech.Data.Dto.Game;
    using GamaEdtech.Domain.Enumeration;
    using GamaEdtech.Infrastructure.Interface;

    using HandlebarsDotNet;

    using HtmlToOpenXml;

    using Microsoft.AspNetCore.Hosting;
    using Microsoft.AspNetCore.Http;
    using Microsoft.Extensions.Localization;
    using Microsoft.Extensions.Logging;

    using Spire.Presentation;

    using Ooxml = DocumentFormat.OpenXml.Wordprocessing;
    using Vml = DocumentFormat.OpenXml.Vml;

    using static GamaEdtech.Common.Core.Constants;

    public partial class ExamSerivce(Lazy<IUnitOfWorkProvider> unitOfWorkProvider, Lazy<IHttpContextAccessor> httpContextAccessor,
        Lazy<IStringLocalizer<ExamSerivce>> localizer, Lazy<ILogger<ExamSerivce>> logger, Lazy<ICoreProvider> coreProvider
        , Lazy<IWebHostEnvironment> environment, Lazy<IMathFormulaRenderProvider> mathFormulaRenderProvider)
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
                    // Pdf/PowerPoint still render through Spire's naive AppendHTML, which can't cope with
                    // inline markup inside a paragraph, so their template needs this flattened/stripped form.
                    // Word (below) uses the real HTML directly instead of this legacy transform.
                    FlattenTestsForLegacyRender();
                    content = await ExportPdfAsync();
                }
                else if (requestDto.FileType == ExportFileType.Word)
                {
                    content = await ExportDocumentAsync();
                }
                else if (requestDto.FileType == ExportFileType.PowerPoint)
                {
                    FlattenTestsForLegacyRender();
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

                void FlattenTestsForLegacyRender()
                {
                    if (info.Data.Tests is null)
                    {
                        return;
                    }

                    for (var i = 0; i < info.Data.Tests.Count; i++)
                    {
                        var test = info.Data.Tests[i];
                        test.Question = $"<span style=\"color:#2b8cb1\">{i + 1}-</span> {string.Join("<br>", TextRegex().Matches(test.Question!).Select(t => t.Groups.Values.LastOrDefault()))}";
                        test.OptionA = string.Join("<br>", TextRegex().Matches(test.OptionA!).Select(t => t.Groups.Values.LastOrDefault()));
                        test.OptionB = string.Join("<br>", TextRegex().Matches(test.OptionB!).Select(t => t.Groups.Values.LastOrDefault()));
                        test.OptionC = string.Join("<br>", TextRegex().Matches(test.OptionC!).Select(t => t.Groups.Values.LastOrDefault()));
                        test.OptionD = string.Join("<br>", TextRegex().Matches(test.OptionD!).Select(t => t.Groups.Values.LastOrDefault()));
                    }
                }

                async Task<byte[]> ExportPdfAsync()
                {
                    var file = Path.Combine(environment.Value.WebRootPath, "exam.docx.html");
                    var templateContent = await File.ReadAllTextAsync(file);

                    var template = Handlebars.Compile(templateContent);
                    var html = template(info.Data);

                    using var doc = new Spire.Doc.Document();
                    var section = doc.AddSection();
                    var paragraph = section.AddParagraph();
                    paragraph.AppendHTML(html);

                    if (!string.IsNullOrEmpty(requestDto.Watermark))
                    {
                        foreach (Spire.Doc.Section item in doc.Sections)
                        {
                            item.Document.Watermark = new Spire.Doc.TextWatermark
                            {
                                Text = requestDto.Watermark,
                                FontSize = 50,
                                Color = Color.Blue,
                                Layout = Spire.Doc.Documents.WatermarkLayout.Diagonal,
                            };
                        }
                    }

                    using MemoryStream stream = new();
                    doc.SaveToStream(stream, Spire.Doc.FileFormat.PDF);
                    return stream.ToArray();
                }

                async Task<byte[]> ExportDocumentAsync()
                {
                    var file = Path.Combine(environment.Value.WebRootPath, "exam.word.html");
                    var templateContent = await File.ReadAllTextAsync(file);

                    var handlebars = Handlebars.Create();
                    handlebars.RegisterHelper("inc", (output, _, args) => output.Write((int)args[0] + 1));
                    var template = handlebars.Compile(templateContent);
                    var html = template(info.Data);

                    var formulaResult = await mathFormulaRenderProvider.Value.RenderFormulasAsync(html);
                    if (formulaResult.OperationResult == OperationResult.Succeeded && formulaResult.Data is not null)
                    {
                        html = formulaResult.Data;
                    }
                    else
                    {
                        Logger.Value.LogError("Formula rendering failed for exam {ExamId}: {Errors}", requestDto.ExamId,
                            string.Join(", ", formulaResult.Errors?.Select(t => t.Message) ?? []));
                    }

                    using MemoryStream stream = new();
                    using (var wordDocument = WordprocessingDocument.Create(stream, DocumentFormat.OpenXml.WordprocessingDocumentType.Document))
                    {
                        var mainPart = wordDocument.AddMainDocumentPart();
                        var body = new Ooxml.Body();
                        var document = new Ooxml.Document();
                        _ = document.AppendChild(body);
                        mainPart.Document = document;

                        var converter = new HtmlConverter(mainPart);
                        var paragraphs = await converter.ParseAsync(html, CancellationToken.None);
                        foreach (var paragraph in paragraphs)
                        {
                            _ = body.AppendChild(paragraph);
                        }

                        // HtmlToOpenXml assigns every table the built-in "TableGrid" style regardless of CSS,
                        // which bakes in visible borders at the style layer; LibreOffice doesn't fully let our
                        // explicit border:none direct-formatting override that style layer. Since these tables
                        // are for layout only (no CSS asked for a style, only for no borders), drop the style
                        // reference entirely so there's nothing left to draw a border.
                        RemoveDefaultTableStyle(body);

                        // Each question is laid out as one table row (number + text + options grid); without
                        // this, Word is free to split that row's content across a page boundary, separating a
                        // question from its own answer choices.
                        PreventRowsSplittingAcrossPages(body);

                        // Explicit page size/margins so the document looks the same regardless of the
                        // opening app/locale's own default (which otherwise governs an OOXML body with no
                        // section properties at all).
                        _ = body.AppendChild(new Ooxml.SectionProperties());
                        var sectionProperties = body.Elements<Ooxml.SectionProperties>().Single();
                        _ = sectionProperties.AppendChild(new Ooxml.PageSize { Width = 11906, Height = 16838 });
                        _ = sectionProperties.AppendChild(new Ooxml.PageMargin
                        {
                            Top = 1440,
                            Right = 1440,
                            Bottom = 1440,
                            Left = 1440,
                            Header = 720,
                            Footer = 720,
                            Gutter = 0,
                        });

                        if (!string.IsNullOrEmpty(requestDto.Watermark))
                        {
                            AddWatermark(mainPart, requestDto.Watermark);
                        }

                        mainPart.Document.Save();
                    }

                    return stream.ToArray();
                }

                async Task<byte[]> ExportPresentationAsync()
                {
                    using var presentation = new Presentation();

                    var header = Path.Combine(environment.Value.WebRootPath, "exam.header.html");
                    var headerContent = await File.ReadAllTextAsync(header);

                    var headerTemplate = Handlebars.Compile(headerContent);
                    var headerHtml = headerTemplate(info.Data);

                    var shapes = presentation.Slides[0].Shapes;
                    shapes.AddFromHtml(headerHtml);

                    if (info.Data.Tests is not null)
                    {
                        var item = Path.Combine(environment.Value.WebRootPath, "exam.item.html");
                        var itemContent = await File.ReadAllTextAsync(item);

                        var itemTemplate = Handlebars.Compile(itemContent);

                        foreach (var test in info.Data.Tests)
                        {
                            var slide = presentation.Slides.Append();
                            var itemHtml = itemTemplate(test);

                            slide.Shapes.AddFromHtml(itemHtml);
                        }
                    }

                    using MemoryStream stream = new();
                    presentation.SaveToFile(stream, FileFormat.Pptx2019);
                    return stream.ToArray();
                }
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return new(OperationResult.Failed) { Errors = [new() { Message = exc.Message },] };
            }
        }

        [GeneratedRegex("<p>([^<]*)<\\/p>")]
        private static partial Regex TextRegex();

        /// <summary>
        /// Strips the "TableGrid" style HtmlToOpenXml assigns every table by default -- its baked-in
        /// borders can outrank our explicit border:none direct formatting in some renderers (LibreOffice).
        /// </summary>
        private static void RemoveDefaultTableStyle(Ooxml.Body body)
        {
            foreach (var table in body.Descendants<Ooxml.Table>())
            {
                table.GetFirstChild<Ooxml.TableProperties>()?.GetFirstChild<Ooxml.TableStyle>()?.Remove();
            }
        }

        /// <summary>
        /// Marks every table row as non-splittable across a page break -- each question is laid out as one
        /// row (number + text + options grid), so without this Word can separate a question from its own
        /// answer choices when the page runs out of room mid-question.
        /// </summary>
        private static void PreventRowsSplittingAcrossPages(Ooxml.Body body)
        {
            foreach (var row in body.Descendants<Ooxml.TableRow>())
            {
                var properties = row.Elements<Ooxml.TableRowProperties>().FirstOrDefault();
                if (properties is null)
                {
                    properties = new Ooxml.TableRowProperties();
                    _ = row.PrependChild(properties);
                }

                if (!properties.Elements<Ooxml.CantSplit>().Any())
                {
                    _ = properties.AppendChild(new Ooxml.CantSplit());
                }
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

        /// <summary>
        /// Adds a diagonal text watermark to every page using the classic VML <c>v:textpath</c> shape --
        /// the same mechanism Word itself uses -- since OOXML has no simpler native watermark element.
        /// </summary>
        private static void AddWatermark([NotNull] MainDocumentPart mainPart, string text)
        {
            var shape = new Vml.Shape(
                new Vml.Fill { Opacity = "0.5" },
                new Vml.TextPath
                {
                    Style = "font-family:'Calibri';font-size:1pt",
                    String = text,
                })
            {
                Style = "position:absolute;left:0;top:0;width:415pt;height:207.5pt;rotation:315;z-index:-251658752;" +
                    "mso-position-horizontal:center;mso-position-horizontal-relative:margin;mso-position-vertical:center;mso-position-vertical-relative:margin",
                FillColor = "#4472c4",
                Stroked = false,
            };

            var picture = new Ooxml.Picture();
            _ = picture.AppendChild(shape);
            var run = new Ooxml.Run();
            _ = run.AppendChild(picture);
            var paragraph = new Ooxml.Paragraph();
            _ = paragraph.AppendChild(run);
            var header = new Ooxml.Header();
            _ = header.AppendChild(paragraph);

            var headerPart = mainPart.AddNewPart<HeaderPart>();
            headerPart.Header = header;
            headerPart.Header.Save();

            var headerPartId = mainPart.GetIdOfPart(headerPart);
            var body = mainPart.Document!.Body!;
            var sectionProperties = body.Elements<Ooxml.SectionProperties>().FirstOrDefault();
            if (sectionProperties is null)
            {
                sectionProperties = new Ooxml.SectionProperties();
                _ = body.AppendChild(sectionProperties);
            }

            _ = sectionProperties.PrependChild(new Ooxml.HeaderReference { Type = Ooxml.HeaderFooterValues.Default, Id = headerPartId });
        }
    }
}
