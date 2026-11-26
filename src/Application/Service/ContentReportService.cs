namespace GamaEdtech.Application.Service
{
    using System.Diagnostics.CodeAnalysis;
    using System.Linq;
    using System.Net;

    using GamaEdtech.Application.Interface;
    using GamaEdtech.Common.Core;
    using GamaEdtech.Common.Data;
    using GamaEdtech.Common.DataAccess.UnitOfWork;
    using GamaEdtech.Common.Service;
    using GamaEdtech.Data.Dto.ApplicationSettings;
    using GamaEdtech.Data.Dto.ContentReport;
    using GamaEdtech.Domain.Entity;
    using GamaEdtech.Domain.Enumeration;

    using Microsoft.AspNetCore.Http;
    using Microsoft.EntityFrameworkCore;
    using Microsoft.Extensions.Configuration;
    using Microsoft.Extensions.Logging;

    using static GamaEdtech.Common.Core.Constants;

    /// <inheritdoc cref="IContentReportService"/>
    public sealed class ContentReportService(Lazy<IUnitOfWorkProvider> unitOfWorkProvider, Lazy<IHttpContextAccessor> httpContextAccessor,
        Lazy<ILogger<ContentReportService>> logger, Lazy<IApplicationSettingsService> applicationSettingsService, Lazy<IEmailService> emailService,
        Lazy<IJobScheduler> jobScheduler, Lazy<IConfiguration> configuration)
        : ServiceBase<ContentReportService>(unitOfWorkProvider, httpContextAccessor, logger), IContentReportService
    {
        private const int MaxPageSize = 100;
        private const byte MaxReportType = 20;

        public async Task<ResultData<long>> CreateAsync([NotNull] CreateContentReportRequestDto requestDto)
        {
            try
            {
                var message = requestDto.Message?.Trim() ?? string.Empty;
                if (message.Length is < ContentReport.MinMessageLength or > ContentReport.MaxMessageLength)
                {
                    return Invalid<long>("message", $"The description is {ContentReport.MinMessageLength} to {ContentReport.MaxMessageLength} characters.");
                }

                if (requestDto.ReportType is < 1 or > MaxReportType || requestDto.ContentId <= 0)
                {
                    return Invalid<long>("reportType", "Pick the kind of problem.");
                }

                var uow = UnitOfWorkProvider.Value.CreateUnitOfWork();
                var repository = uow.GetRepository<ContentReport>();

                // One open report per person and item: a second click (or a stuck button) doesn't flood the list.
                if (await repository.AnyAsync(t => t.UserId == requestDto.UserId && t.ContentType == requestDto.ContentType
                    && t.ContentId == requestDto.ContentId && t.Status == ContentReportStatus.Open))
                {
                    return Invalid<long>("alreadyReported", "You already reported this; we'll look at it.");
                }

                ContentReport report = new()
                {
                    UserId = requestDto.UserId,
                    ContentType = requestDto.ContentType,
                    ContentId = requestDto.ContentId,
                    ReportType = requestDto.ReportType,
                    Message = message,
                    CreationDate = DateTimeOffset.UtcNow,
                };
                repository.Add(report);
                _ = await uow.SaveChangesAsync();
                return new(OperationResult.Succeeded) { Data = report.Id };
            }
            catch (Exception exc)
            {
                return Error<long>(exc);
            }
        }

        public async Task<ResultData<ContentReportListDto>> GetReportsAsync([NotNull] ContentReportListRequestDto requestDto)
        {
            try
            {
                var uow = UnitOfWorkProvider.Value.CreateUnitOfWork();
                var query = uow.GetRepository<ContentReport>().GetManyQueryable(t => true);
                if (requestDto.Status is { } status)
                {
                    query = query.Where(t => t.Status == status);
                }

                if (requestDto.ContentType is { } type)
                {
                    query = query.Where(t => t.ContentType == type);
                }

                if (requestDto.ContentId is long contentId)
                {
                    query = query.Where(t => t.ContentId == contentId);
                }

                var size = Math.Clamp(requestDto.PageSize, 1, MaxPageSize);
                var total = await query.CountAsync();
                var items = await query.OrderByDescending(t => t.CreationDate).ThenByDescending(t => t.Id)
                    .Skip((Math.Max(requestDto.Page, 1) - 1) * size).Take(size)
                    .Select(t => new ContentReportItemDto
                    {
                        Id = t.Id,
                        ContentType = t.ContentType,
                        ContentId = t.ContentId,
                        ReportType = t.ReportType,
                        Message = t.Message,
                        UserName = (t.User!.FirstName + " " + t.User.LastName).Trim(),
                        Status = t.Status,
                        AdminNote = t.AdminNote,
                        CreationDate = t.CreationDate,
                        ClosedDate = t.ClosedDate,
                    })
                    .ToListAsync();
                await AddTitlesAsync(uow, items);
                return new(OperationResult.Succeeded) { Data = new() { Total = total, Items = items } };
            }
            catch (Exception exc)
            {
                return Error<ContentReportListDto>(exc);
            }
        }

        public async Task<ResultData<bool>> SetStatusAsync(long id, [NotNull] ContentReportStatus status, string? note, long userId)
        {
            try
            {
                var uow = UnitOfWorkProvider.Value.CreateUnitOfWork();
                var report = await uow.GetRepository<ContentReport>().GetManyQueryable(t => t.Id == id, tracking: true).FirstOrDefaultAsync();
                if (report is null)
                {
                    return Invalid<bool>("notFound", "No such report.");
                }

                var wasOpen = report.Status == ContentReportStatus.Open;
                report.Status = status;
                var trimmed = note?.Trim();
                report.AdminNote = string.IsNullOrEmpty(trimmed) ? report.AdminNote : trimmed[..Math.Min(trimmed.Length, 500)];
                var open = status == ContentReportStatus.Open;
                report.ClosedDate = open ? null : DateTimeOffset.UtcNow;
                report.ClosedByUserId = open ? null : userId;
                _ = await uow.SaveChangesAsync();

                // Tell the reporter the outcome, once per close: off the request, so a slow or failed email never fails the action.
                if (wasOpen && !open)
                {
                    jobScheduler.Value.Schedule<IContentReportService>(t => t.SendClosedEmailAsync(id), TimeSpan.Zero);
                }

                return new(OperationResult.Succeeded) { Data = true };
            }
            catch (Exception exc)
            {
                return Error<bool>(exc);
            }
        }

        public async Task SendClosedEmailAsync(long id)
        {
            try
            {
                var uow = UnitOfWorkProvider.Value.CreateUnitOfWork();
                var report = await uow.GetRepository<ContentReport>().GetManyQueryable(t => t.Id == id).Select(t => new
                {
                    t.Status,
                    t.ContentType,
                    t.ContentId,
                    t.User!.Email,
                    t.User.FirstName,
                    t.User.LastName,
                }).FirstOrDefaultAsync();

                // Reopened again before the job ran, or no address: nothing to say.
                if (report is null || report.Status == ContentReportStatus.Open || string.IsNullOrEmpty(report.Email))
                {
                    return;
                }

                var resolved = report.Status == ContentReportStatus.Resolved;
                var key = resolved ? nameof(ApplicationSettingsDto.ContentReportResolvedEmailTemplate) : nameof(ApplicationSettingsDto.ContentReportDismissedEmailTemplate);
                var template = (await applicationSettingsService.Value.GetSettingAsync<string?>(key)).Data;
                if (string.IsNullOrEmpty(template))
                {
                    return;
                }

                List<ContentReportItemDto> item = [new() { ContentType = report.ContentType, ContentId = report.ContentId, Status = report.Status }];
                await AddTitlesAsync(uow, item);
                var title = string.IsNullOrWhiteSpace(item[0].ContentTitle) ? "the page you reported" : item[0].ContentTitle!;
                var name = $"{report.FirstName} {report.LastName}".Trim();

                var body = template
                    .Replace("[RECEIVER_NAME]", WebUtility.HtmlEncode(string.IsNullOrEmpty(name) ? "there" : name), StringComparison.OrdinalIgnoreCase)
                    .Replace("[CONTENT_TITLE]", WebUtility.HtmlEncode(title), StringComparison.OrdinalIgnoreCase)
                    .Replace("[CONTENT_LINK]", WebUtility.HtmlEncode(ContentLink(report.ContentType, report.ContentId, item[0].ContentTitle)), StringComparison.OrdinalIgnoreCase);

                _ = await emailService.Value.SendEmailAsync(new()
                {
                    Subject = resolved ? "Gamatrain - The problem you reported is fixed" : "Gamatrain - About the problem you reported",
                    Body = body,
                    EmailAddresses = [report.Email],
                    From = emailService.Value.GetNoReplyEmail(),
                });
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
            }
        }

        /// <summary>
        /// The reported item's page, as the sitemap writes it (<c>/{paper|exam|tutorial}/{id}/{title_url}</c>); the site's
        /// home for kinds with no page of their own here (an exam question, multimedia, Q&amp;A). Internal for the tests.
        /// </summary>
        internal string ContentLink(ReportedContentType type, long id, string? title)
        {
            var site = (configuration.Value.GetValue<string>("ContentReports:SiteUrl") ?? ExamWordDocumentBuilder.GamatrainWebsiteUrl).TrimEnd('/');
            string? segment = null;
            if (type == ReportedContentType.Paper)
            {
                segment = ItemType.Paper.Identifier;
            }
            else if (type == ReportedContentType.Exam)
            {
                segment = ItemType.Exam.Identifier;
            }
            else if (type == ReportedContentType.Tutorial)
            {
                segment = ItemType.Tutorial.Identifier;
            }

            if (segment is null)
            {
                return site;
            }

            var slug = PastPaperOutput.TitleUrl(title);
            return slug.Length == 0 ? $"{site}/{segment}/{id}" : $"{site}/{segment}/{id}/{Uri.EscapeDataString(slug)}";
        }

        /// <summary>The titles of the items that live here (papers, exams, tutorials, a question's code), one query per kind.</summary>
        private static async Task AddTitlesAsync(IUnitOfWork uow, List<ContentReportItemDto> items)
        {
            async Task FillAsync(ReportedContentType type, Func<List<long>, Task<Dictionary<long, string?>>> titles)
            {
                List<long> ids = [.. items.Where(t => t.ContentType == type).Select(t => t.ContentId).Distinct()];
                if (ids.Count == 0)
                {
                    return;
                }

                var found = await titles(ids);
                items.Where(t => t.ContentType == type).ToList().ForEach(t => t.ContentTitle = found.GetValueOrDefault(t.ContentId));
            }

            await FillAsync(ReportedContentType.Paper, ids => uow.GetRepository<PastPaper>().GetManyQueryable(t => ids.Contains(t.Id)).ToDictionaryAsync(t => t.Id, t => t.Title));
            await FillAsync(ReportedContentType.Exam, ids => uow.GetRepository<Exam>().GetManyQueryable(t => ids.Contains(t.Id)).ToDictionaryAsync(t => t.Id, t => t.Title));
            await FillAsync(ReportedContentType.Tutorial, ids => uow.GetRepository<Tutorial>().GetManyQueryable(t => ids.Contains(t.Id)).ToDictionaryAsync(t => t.Id, t => t.Title));
            await FillAsync(ReportedContentType.ExamQuestion, ids => uow.GetRepository<ExamQuestion>().GetManyQueryable(t => ids.Contains(t.Id)).ToDictionaryAsync(t => t.Id, t => t.Code));
        }

        private static ResultData<T> Invalid<T>(string reference, string message) => new(OperationResult.NotValid) { Errors = [new() { Message = message, Reference = reference }] };

        private ResultData<T> Error<T>(Exception exc)
        {
            Logger.Value.LogException(exc);
            return new(OperationResult.Failed) { Errors = [new() { Message = exc.Message }] };
        }
    }
}
