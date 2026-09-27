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
    using GamaEdtech.Domain.Entity;
    using GamaEdtech.Domain.Entity.Identity;
    using GamaEdtech.Domain.Enumeration;
    using GamaEdtech.Infrastructure.Interface;

    using Microsoft.AspNetCore.Hosting;
    using Microsoft.EntityFrameworkCore;
    using Microsoft.AspNetCore.Http;
    using Microsoft.Extensions.Localization;
    using Microsoft.Extensions.Logging;

    using SkiaSharp;

    using static GamaEdtech.Common.Core.Constants;

    public partial class ExamSerivce(Lazy<IUnitOfWorkProvider> unitOfWorkProvider, Lazy<IHttpContextAccessor> httpContextAccessor,
        Lazy<IStringLocalizer<ExamSerivce>> localizer, Lazy<ILogger<ExamSerivce>> logger, Lazy<ICoreProvider> coreProvider
        , Lazy<IWebHostEnvironment> environment, Lazy<IHeadlessBrowserRenderProvider> headlessBrowserRenderProvider
        , Lazy<IHttpClientFactory> httpClientFactory, Lazy<IFileService> fileService
        , Lazy<IGameService> gameService, Lazy<IApplicationSettingsService> applicationSettingsService)
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

                var authorAvatar = await ApplyLocalAuthorAsync(info.Data.Exam);

                // Charged before the (slow) generation, refunded if it then fails; recorded as a purchase only once
                // the file exists (see ChargeExportAsync).
                var charge = await ChargeExportAsync(requestDto, info.Data.Tests?.Count ?? 0);
                if (charge.Failure is { } refused)
                {
                    return refused;
                }

                byte[]? content = null;
                try
                {
                    if (requestDto.FileType == ExportFileType.Pdf)
                    {
                        content = await ExportPdfAsync();
                    }
                    else if (requestDto.FileType == ExportFileType.Word)
                    {
                        content = await ExportDocumentAsync();
                    }
                    else if (requestDto.FileType == ExportFileType.Thumbnail)
                    {
                        content = await ExportThumbnailAsync();
                    }
                    else if (requestDto.FileType == ExportFileType.PowerPoint)
                    {
                        content = await ExportPresentationAsync();
                    }
                }
                catch
                {
                    await RefundExportAsync(requestDto, charge);
                    throw;
                }

                if (content is null)
                {
                    await RefundExportAsync(requestDto, charge);
                    return new(OperationResult.Failed) { Errors = [new() { Message = Localizer.Value["GeneralError"] },] };
                }

                await RecordExportPurchaseAsync(requestDto, charge);
                return new(OperationResult.Succeeded)
                {
                    Data = new()
                    {
                        Content = content,
                        FileName = BuildFileName(info.Data.Exam?.Title, requestDto.ExamId),
                        Points = charge.Spend is null ? 0 : charge.Points,
                        AlreadyPurchased = charge.AlreadyPurchased,
                        Charge = charge.Spend,
                    },
                };

                async Task<byte[]> ExportPdfAsync()
                {
                    var (page, body) = await BuildPdfPageAsync();
                    return await PrintPdfAsync(page, body);
                }

                // Same layout as the Word export (ExamPdfHtmlBuilder reuses ExamWordDocumentBuilder's own
                // measurements, layout rules and header shapes), printed by Chromium. Formulas are MathJax
                // images here rather than Word's native equations -- rendered on the body alone, since the
                // render returns a fragment, then wrapped into the full page document.
                async Task<(ExamPdfHtmlBuilder.PdfPage Page, string Body)> BuildPdfPageAsync()
                {
                    var page = await ExamPdfHtmlBuilder.BuildAsync(info.Data, await LoadBrandAssetsAsync(), requestDto.Watermark);
                    var formulaResult = await headlessBrowserRenderProvider.Value.RenderFormulasAsync(page.BodyHtml);
                    if (formulaResult.OperationResult == OperationResult.Succeeded && formulaResult.Data is not null)
                    {
                        return (page, formulaResult.Data);
                    }

                    Logger.Value.LogError("Formula rendering failed for exam {ExamId}: {Errors}", requestDto.ExamId,
                        string.Join(", ", formulaResult.Errors?.Select(t => t.Message) ?? []));
                    return (page, page.BodyHtml);
                }

                async Task<byte[]> PrintPdfAsync(ExamPdfHtmlBuilder.PdfPage page, string body)
                {
                    var pdfResult = await headlessBrowserRenderProvider.Value.RenderPdfAsync(
                        ExamPdfHtmlBuilder.WrapDocument(body), page.HeaderTemplate, page.FooterTemplate, page.MarginTop, page.MarginBottom, page.MarginSide);
                    return pdfResult.OperationResult == OperationResult.Succeeded && pdfResult.Data is not null
                        ? pdfResult.Data
                        : throw new InvalidOperationException(string.Join(", ", pdfResult.Errors?.Select(t => t.Message) ?? ["PDF rendering failed"]));
                }

                // The Pdf export's first page as a 496x792 WebP. The PDF itself is printed too, only to get the real
                // page count for the footer's "1 / N"; the first page is then screenshotted as its own document.
                async Task<byte[]> ExportThumbnailAsync()
                {
                    var (page, body) = await BuildPdfPageAsync();
                    var pageCount = CountPdfPages(await PrintPdfAsync(page, body));
                    var screenshot = await headlessBrowserRenderProvider.Value.RenderScreenshotAsync(
                        ExamPdfHtmlBuilder.BuildThumbnailDocument(page, body, pageCount),
                        ExamPdfHtmlBuilder.ThumbnailPageWidthPx, ExamPdfHtmlBuilder.ThumbnailPageHeightPx, ThumbnailRenderScale);
                    return screenshot.OperationResult == OperationResult.Succeeded && screenshot.Data is not null
                        ? ToThumbnailWebp(screenshot.Data)
                        : throw new InvalidOperationException(string.Join(", ", screenshot.Errors?.Select(t => t.Message) ?? ["Thumbnail rendering failed"]));
                }

                async Task<HeaderBrandAssets> LoadBrandAssetsAsync() => new(
                    GamaWordmark: await File.ReadAllBytesAsync(Path.Combine(environment.Value.WebRootPath, "exam-gama-wordmark.png")),
                    GamaWordmarkSvg: await File.ReadAllBytesAsync(Path.Combine(environment.Value.WebRootPath, "exam-gama-wordmark.svg")),
                    ProfilePlaceholder: authorAvatar ?? await File.ReadAllBytesAsync(Path.Combine(environment.Value.WebRootPath, "exam-profile-placeholder.png")),
                    FooterWave: await File.ReadAllBytesAsync(Path.Combine(environment.Value.WebRootPath, "exam-footer-wave.png")),
                    FooterLogo: await File.ReadAllBytesAsync(Path.Combine(environment.Value.WebRootPath, "exam-footer-logo.png")),
                    FooterLogoSvg: await File.ReadAllBytesAsync(Path.Combine(environment.Value.WebRootPath, "exam-footer-logo.svg")),
                    LevelIcons: new Dictionary<string, HeaderIcon>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["Easy"] = await LoadHeaderIconAsync("exam-level-easy"),
                        ["Medium"] = await LoadHeaderIconAsync("exam-level-medium"),
                        ["Hard"] = await LoadHeaderIconAsync("exam-level-hard"),
                    },
                    NameIcon: await LoadHeaderIconAsync("exam-icon-name"),
                    QuestionsIcon: await LoadHeaderIconAsync("exam-icon-questions"),
                    TimeIcon: await LoadHeaderIconAsync("exam-icon-time"));

                async Task<HeaderIcon> LoadHeaderIconAsync(string name) => new(
                    Png: await File.ReadAllBytesAsync(Path.Combine(environment.Value.WebRootPath, $"{name}.png")),
                    Svg: await File.ReadAllBytesAsync(Path.Combine(environment.Value.WebRootPath, $"{name}.svg")));

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
                        AddField(i, 5, test.AnswerHtml);
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
                            case 5:
                                test.AnswerHtml = html;
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

                    var brandAssets = await LoadBrandAssetsAsync();
                    var httpClient = new Lazy<HttpClient>(() => httpClientFactory.Value.CreateHttpClient());
                    return await ExamPresentationBuilder.BuildAsync(info.Data, new(brandAssets.GamaWordmark, brandAssets.FooterLogo, brandAssets.QuestionsIcon.Png, brandAssets.TimeIcon.Png, ExamWordDocumentBuilder.LevelIconFor(brandAssets, info.Data.Exam?.Level)?.Png), httpClient);
                }
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return new(OperationResult.Failed) { Errors = [new() { Message = exc.Message },] };
            }
        }

        public async Task<ResultData<ExportPricesResponseDto>> GetExportPricesAsync(long userId, long examId, string? secretKey)
        {
            try
            {
                var count = await coreProvider.Value.GetExamQuestionCountAsync(new() { ExamId = examId, SecretKey = secretKey });
                if (count.OperationResult is not OperationResult.Succeeded)
                {
                    return new(count.OperationResult) { Errors = count.Errors };
                }

                var settings = await applicationSettingsService.Value.GetApplicationSettingsAsync();
                if (settings.Data is null)
                {
                    return new(settings.OperationResult) { Errors = settings.Errors };
                }

                var uow = UnitOfWorkProvider.Value.CreateUnitOfWork();
                var purchased = await uow.GetRepository<ExamExportPurchase>()
                    .GetManyQueryable(t => t.UserId == userId && t.ExamId == examId)
                    .Select(t => t.FileType)
                    .ToListAsync();

                return new(OperationResult.Succeeded)
                {
                    Data = new()
                    {
                        QuestionCount = count.Data,
                        Items = [.. new[] { ExportFileType.Pdf, ExportFileType.Word, ExportFileType.PowerPoint }.Select(fileType => new ExportPricesResponseDto.ItemDto
                        {
                            FileType = fileType,
                            Points = ExamExportPricing.Price(settings.Data, fileType, count.Data),
                            Purchased = purchased.Contains(fileType),
                        })],
                    },
                };
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return new(OperationResult.Failed) { Errors = [new() { Message = exc.Message },] };
            }
        }

        /// <summary>What <see cref="ChargeExportAsync"/> did: the price, the successful charge (<see langword="null"/>
        /// when nothing was charged), or the response to return when the charge was refused.</summary>
        private readonly record struct ExportCharge(long Points, SpendPointsResponseDto? Spend, ResultData<ExportExamResponseDto>? Failure, bool AlreadyPurchased);

        /// <summary>
        /// Charges an exam export (2026-09-27): <see cref="ExamExportPricing.Price"/> -- the exam's question count x the
        /// format's admin-set multiplier -- through <see cref="IGameService.SpendPointsAsync"/> with
        /// <see cref="ContentType.Exam"/>, i.e. the <c>ExamDownload</c> subscription quota first (whether the plan has
        /// it alone or in a feature group), then points. Free, with no charge, for the thumbnail, an exam without
        /// questions, and an exam+format this user already bought (<see cref="ExamExportPurchase"/>). No owner
        /// commission: the export is our own generated product, priced by size, not the author's own price.
        /// </summary>
        private async Task<ExportCharge> ChargeExportAsync(ExportExamRequestDto requestDto, int questionCount)
        {
            var settings = await applicationSettingsService.Value.GetApplicationSettingsAsync();
            if (settings.Data is null)
            {
                return new(0, null, new(settings.OperationResult) { Errors = settings.Errors }, false);
            }

            var points = ExamExportPricing.Price(settings.Data, requestDto.FileType, questionCount);
            if (points == 0)
            {
                return new(0, null, null, false);
            }

            var uow = UnitOfWorkProvider.Value.CreateUnitOfWork();
            var alreadyPurchased = await uow.GetRepository<ExamExportPurchase>()
                .GetManyQueryable(t => t.UserId == requestDto.UserId && t.ExamId == requestDto.ExamId && t.FileType == requestDto.FileType)
                .AnyAsync();
            if (alreadyPurchased)
            {
                return new(points, null, null, true);
            }

            var spend = await gameService.Value.SpendPointsAsync(new()
            {
                UserId = requestDto.UserId,
                Points = points,
                QuotaAmount = (int)Math.Min(points, int.MaxValue),
                IdentifierId = requestDto.ExamId,
                ContentType = ContentType.Exam,
            });
            if (spend.OperationResult is OperationResult.Succeeded && spend.Data?.Spent is true)
            {
                return new(points, spend.Data, null, false);
            }

            var refusedResult = spend.OperationResult is OperationResult.Succeeded ? OperationResult.Failed : spend.OperationResult;
            return new(points, null, new(refusedResult)
            {
                Errors = spend.Errors,
                Data = new() { Points = points, Charge = spend.Data },
            }, false);
        }

        /// <summary>Reverses a charge whose export then failed. Best effort: a failed refund is logged, never thrown --
        /// the export's own failure is what gets reported.</summary>
        private async Task RefundExportAsync(ExportExamRequestDto requestDto, ExportCharge charge)
        {
            if (charge.Spend?.PaidBy is not SpendSource paidBy)
            {
                return;
            }

            try
            {
                _ = await gameService.Value.RefundPointsAsync(new()
                {
                    UserId = requestDto.UserId,
                    Points = charge.Points,
                    QuotaAmount = (int)Math.Min(charge.Points, int.MaxValue),
                    IdentifierId = requestDto.ExamId,
                    ContentType = ContentType.Exam,
                    PaidBy = paidBy,
                    UserSubscriptionId = charge.Spend.CurrentSubscriptionId,
                });
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
            }
        }

        /// <summary>Records a charged export as bought, so the same exam+format is free next time. Two concurrent first
        /// exports can both pass the "already bought?" check and both be charged; the unique (UserId, ExamId, FileType)
        /// index then rejects the second row, and that request refunds its own charge -- the user still gets the file,
        /// paid once.</summary>
        private async Task RecordExportPurchaseAsync(ExportExamRequestDto requestDto, ExportCharge charge)
        {
            if (charge.Spend?.PaidBy is not SpendSource paidBy)
            {
                return;
            }

            try
            {
                var uow = UnitOfWorkProvider.Value.CreateUnitOfWork();
                uow.GetRepository<ExamExportPurchase>().Add(new()
                {
                    UserId = requestDto.UserId,
                    ExamId = requestDto.ExamId,
                    FileType = requestDto.FileType,
                    Points = charge.Points,
                    PaidBy = paidBy,
                    CreationDate = DateTimeOffset.UtcNow,
                });
                _ = await uow.SaveChangesAsync();
            }
            catch (DbUpdateException exc)
            {
                Logger.Value.LogException(exc);
                await RefundExportAsync(requestDto, charge);
            }
        }

        /// <summary>
        /// The header's "By:" author comes from our own user whose <c>CoreId</c> is the exam author's gama-api user
        /// id: their first/last name replaces gama-api's, and their avatar (downloaded, cropped to a circle) replaces
        /// the placeholder portrait -- returned for the header, or <see langword="null"/> to keep the placeholder.
        /// Best effort: no linked account, no avatar, or a failed download just keeps gama-api's name/the
        /// placeholder rather than failing the export.
        /// </summary>
        private async Task<byte[]?> ApplyLocalAuthorAsync(ExamInformationResponseDto.ExamDto? exam)
        {
            if (exam?.AuthorCoreId is not { } coreId)
            {
                return null;
            }

            try
            {
                var author = await UnitOfWorkProvider.Value.CreateUnitOfWork().GetRepository<ApplicationUser>()
                    .GetManyQueryable(t => t.CoreId == coreId)
                    .Select(t => new { t.FirstName, t.LastName, t.AvatarId })
                    .FirstOrDefaultAsync();
                if (author is null)
                {
                    return null;
                }

                var name = string.Join(' ', new[] { author.FirstName, author.LastName }.Where(t => !string.IsNullOrWhiteSpace(t)));
                if (name.Length > 0)
                {
                    exam.Author = name;
                }

                var avatarUrl = fileService.Value.GetStaticFileUrl(new() { FileId = author.AvatarId, ContainerType = ContainerType.User });
                if (avatarUrl is null || !Uri.TryCreate(avatarUrl, UriKind.Absolute, out var avatarUri))
                {
                    return null;
                }

                using var httpClient = httpClientFactory.Value.CreateHttpClient();
                var bytes = await httpClient.GetByteArrayAsync(avatarUri);
                return ToCircularAvatar(bytes);
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return null;
            }
        }

        /// <summary>Center-crops an avatar to a square and masks it to a circle (transparent corners), so a real
        /// photo sits in the header's round portrait slot like the placeholder does instead of being stretched to
        /// its square box. <see langword="null"/> if the bytes aren't a readable image.</summary>
        private static byte[]? ToCircularAvatar(byte[] bytes)
        {
            using var source = SKBitmap.Decode(bytes);
            if (source is null)
            {
                return null;
            }

            const int size = 256;
            var side = Math.Min(source.Width, source.Height);
            var crop = new SKRectI((source.Width - side) / 2, (source.Height - side) / 2, ((source.Width - side) / 2) + side, ((source.Height - side) / 2) + side);

            using var surface = SKSurface.Create(new SKImageInfo(size, size, SKColorType.Rgba8888, SKAlphaType.Premul));
            var canvas = surface.Canvas;
            canvas.Clear(SKColors.Transparent);
            using var clip = new SKPath();
            clip.AddCircle(size / 2f, size / 2f, size / 2f);
            canvas.ClipPath(clip, antialias: true);
            using var image = SKImage.FromBitmap(source);
            canvas.DrawImage(image, crop, new SKRect(0, 0, size, size), new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear));
            using var snapshot = surface.Snapshot();
            using var png = snapshot.Encode(SKEncodedImageFormat.Png, 100);
            return png.ToArray();
        }

        private const int ThumbnailWidthPx = 496;
        private const int ThumbnailHeightPx = 792;

        /// <summary>The page is screenshotted at 1.5x and scaled down to the thumbnail, for smoother text than
        /// rendering straight at the (smaller) thumbnail size.</summary>
        private const double ThumbnailRenderScale = 1.5;

        /// <summary>
        /// Scales the A4 page screenshot to the thumbnail's 792px height and crops it to 496px wide, centered. A4 is
        /// wider than 496:792, and at this scale the page's own side margins are ~34px each, so the ~32px trimmed
        /// from each side is blank margin only -- the whole page content stays visible.
        /// </summary>
        private static byte[] ToThumbnailWebp(byte[] png)
        {
            using var source = SKBitmap.Decode(png) ?? throw new InvalidOperationException("Thumbnail screenshot could not be decoded");
            var scale = ThumbnailHeightPx / (double)source.Height;
            var sourceCropWidth = (float)(ThumbnailWidthPx / scale);
            var sourceLeft = (source.Width - sourceCropWidth) / 2f;

            using var surface = SKSurface.Create(new SKImageInfo(ThumbnailWidthPx, ThumbnailHeightPx, SKColorType.Rgba8888, SKAlphaType.Premul));
            surface.Canvas.Clear(SKColors.White);
            using var image = SKImage.FromBitmap(source);
            surface.Canvas.DrawImage(image, new SKRect(sourceLeft, 0, sourceLeft + sourceCropWidth, source.Height),
                new SKRect(0, 0, ThumbnailWidthPx, ThumbnailHeightPx), new SKSamplingOptions(SKCubicResampler.Mitchell));
            using var snapshot = surface.Snapshot();
            using var webp = snapshot.Encode(SKEncodedImageFormat.Webp, 90);
            return webp.ToArray();
        }

        /// <summary>Page count of a Chromium-printed PDF: its page objects (<c>/Type /Page</c>, not <c>/Pages</c>).</summary>
        private static int CountPdfPages(byte[] pdf) =>
            PdfPageObjectRegex().Count(System.Text.Encoding.Latin1.GetString(pdf));

        [GeneratedRegex(@"/Type\s*/Page(?![a-zA-Z])")]
        private static partial Regex PdfPageObjectRegex();

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
