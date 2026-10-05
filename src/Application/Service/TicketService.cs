namespace GamaEdtech.Application.Service
{
    using System;
    using System.Diagnostics.CodeAnalysis;
    using System.IO;
    using System.Net.Mail;
    using System.Text.RegularExpressions;

    using GamaEdtech.Application.Interface;
    using GamaEdtech.Common.Core;
    using GamaEdtech.Common.Core.Extensions.Linq;
    using GamaEdtech.Common.Data;
    using GamaEdtech.Common.DataAccess.Specification;
    using GamaEdtech.Common.DataAccess.UnitOfWork;
    using GamaEdtech.Common.Security;
    using GamaEdtech.Common.Service;
    using GamaEdtech.Data.Dto.ApplicationSettings;
    using GamaEdtech.Data.Dto.Email;
    using GamaEdtech.Data.Dto.Ticket;
    using GamaEdtech.Domain.Entity;
    using GamaEdtech.Domain.Enumeration;
    using GamaEdtech.Domain.Specification.Identity;

    using Microsoft.AspNetCore.Http;
    using Microsoft.EntityFrameworkCore;
    using Microsoft.Extensions.Localization;
    using Microsoft.Extensions.Logging;

    using static GamaEdtech.Common.Core.Constants;

    public partial class TicketService(Lazy<IUnitOfWorkProvider> unitOfWorkProvider, Lazy<IHttpContextAccessor> httpContextAccessor, Lazy<IStringLocalizer<TicketService>> localizer
        , Lazy<ILogger<TicketService>> logger, Lazy<IFileService> fileService, Lazy<IEmailService> emailService, Lazy<IIdentityService> identityService, Lazy<IApplicationSettingsService> applicationSettingsService)
        : LocalizableServiceBase<TicketService>(unitOfWorkProvider, httpContextAccessor, localizer, logger), ITicketService
    {
        public async Task<ResultData<ListDataSource<TicketsDto>>> GetTicketsAsync(ListRequestDto<Ticket>? requestDto = null)
        {
            try
            {
                var uow = UnitOfWorkProvider.Value.CreateUnitOfWork();
                // Unread tickets first, then by latest activity, so the ticket with the freshest message is on
                // top. LastActivityDate is stamped on create/reply and indexed with IsReadByAdmin, so this is an
                // index-ordered read. The previous key (has-unread-reply, then CreationDate) ordered replied
                // tickets by when the ticket was opened, not when the reply arrived - see
                // docs/business/support-and-social.md.
                var result = await uow.GetRepository<Ticket>().GetManyQueryable(requestDto?.Specification)
                    .OrderBy(t => t.IsReadByAdmin)
                    .ThenByDescending(t => t.LastActivityDate)
                    .FilterListAsync(WithoutSort(requestDto?.PagingDto));
                var users = await result.List.Select(t => new TicketsDto
                {
                    Id = t.Id,
                    FullName = t.FullName,
                    Email = t.Email,
                    IsReadByAdmin = t.IsReadByAdmin,
                    HasNewReply = t.TicketReplys.Any(r => !r.IsReadByAdmin),
                    Subject = t.Subject,
                    CreationDate = t.CreationDate,
                    LastActivityDate = t.LastActivityDate,
                    Receivers = t.Receivers,
                }).ToListAsync();
                return new(OperationResult.Succeeded) { Data = new() { List = users, TotalRecordsCount = result.TotalRecordsCount } };
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return new(OperationResult.Failed) { Errors = [new() { Message = exc.Message },] };
            }
        }

        public async Task<ResultData<ListDataSource<TicketsDto>>> GetUserTicketsAsync(ListRequestDto<Ticket>? requestDto = null)
        {
            try
            {
                var uow = UnitOfWorkProvider.Value.CreateUnitOfWork();
                // A ticket with a reply the customer hasn't read yet first (OrderByDescending: true before false), then
                // by latest activity, same as the admin list. The per-row Any() is fine here: the list is one user's.
                var result = await uow.GetRepository<Ticket>().GetManyQueryable(requestDto?.Specification)
                    .OrderByDescending(t => t.TicketReplys.Any(r => !r.IsRead)).ThenByDescending(t => t.LastActivityDate).FilterListAsync(WithoutSort(requestDto?.PagingDto));
                var users = await result.List.Select(t => new TicketsDto
                {
                    Id = t.Id,
                    FullName = t.FullName,
                    Email = t.Email,
                    Subject = t.Subject,
                    CreationDate = t.CreationDate,
                    Receivers = t.Receivers,
                }).ToListAsync();
                return new(OperationResult.Succeeded) { Data = new() { List = users, TotalRecordsCount = result.TotalRecordsCount } };
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return new(OperationResult.Failed) { Errors = [new() { Message = exc.Message },] };
            }
        }

        public async Task<ResultData<TicketDto>> GetTicketAsync([NotNull] ISpecification<Ticket> specification)
        {
            try
            {
                var uow = UnitOfWorkProvider.Value.CreateUnitOfWork();
                var repository = uow.GetRepository<Ticket>();
                var ticket = await repository.GetManyQueryable(specification).Select(t => new
                {
                    t.Id,
                    t.FullName,
                    CreationUser = t.User == null ? null : (t.User.FirstName + " " + t.User.LastName),
                    t.CreationDate,
                    t.Email,
                    t.Subject,
                    t.Body,
                    t.FileId,
                    t.Receivers,
                }).FirstOrDefaultAsync();
                if (ticket is null)
                {
                    return new(OperationResult.NotFound)
                    {
                        Errors = [new() { Message = Localizer.Value["TicketNotFound"] },],
                    };
                }

                TicketDto result = new()
                {
                    Id = ticket.Id,
                    FullName = ticket.FullName,
                    CreationUser = ticket.CreationUser,
                    CreationDate = ticket.CreationDate,
                    Email = ticket.Email,
                    Subject = ticket.Subject,
                    Body = ticket.Body,
                    Receivers = ticket.Receivers,
                    FileUri = fileService.Value.GetStaticFileUrl(new()
                    {
                        FileId = ticket.FileId,
                        ContainerType = ContainerType.Ticket,
                    }),
                };

                return new(OperationResult.Succeeded) { Data = result };
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return new(OperationResult.Failed) { Errors = [new() { Message = exc.Message },] };
            }
        }

        public async Task<ResultData<bool>> ExistsTicketAsync([NotNull] ISpecification<Ticket> specification)
        {
            try
            {
                var uow = UnitOfWorkProvider.Value.CreateUnitOfWork();
                var exists = await uow.GetRepository<Ticket>().AnyAsync(specification);

                return new(OperationResult.Succeeded) { Data = exists };
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return new(OperationResult.Failed) { Errors = [new() { Message = exc.Message },] };
            }
        }

        public async Task<ResultData<long>> CreateTicketAsync([NotNull] CreateTicketRequestDto requestDto)
        {
            try
            {
                var uow = UnitOfWorkProvider.Value.CreateUnitOfWork();
                var repository = uow.GetRepository<Ticket>();

                var (fileId, errors) = requestDto.File is not null
                    ? await SaveFileAsync(requestDto.File)
                    : await SaveAttachmentAsync(requestDto.Attachments);
                if (errors is not null)
                {
                    return new(OperationResult.Failed)
                    {
                        Errors = errors,
                    };
                }

                var now = DateTimeOffset.UtcNow;
                var ticket = new Ticket
                {
                    FullName = requestDto.FullName.SanitizePlainText(),
                    Body = requestDto.Body.SanitizeHtml(),
                    Email = requestDto.Email?.ToLowerInvariant(),
                    Subject = requestDto.Subject.SanitizePlainText(),
                    CreationDate = now,
                    LastActivityDate = now,
                    IsReadByAdmin = false,
                    UserId = requestDto.UserId,
                    FileId = fileId,
                    Receivers = requestDto.Receivers,
                };
                repository.Add(ticket);
                _ = await uow.SaveChangesAsync();

                return new(OperationResult.Succeeded) { Data = ticket.Id };
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return new(OperationResult.Failed) { Errors = [new() { Message = exc.Message, }] };
            }
        }

        public async Task<ResultData<Common.Data.Void>> SendTicketConfirmationAsync([NotNull] SendTicketConfirmationRequestDto requestDto)
        {
            try
            {
                var template = await applicationSettingsService.Value.GetSettingAsync<string?>(nameof(ApplicationSettingsDto.TicketConfirmationEmailTemplate));

                return await emailService.Value.SendEmailAsync(new()
                {
                    Subject = GenerateSubject(requestDto.TicketId, requestDto.Subject),
                    // Both values come from the sender (an anonymous form or an inbound email whose From can be forged)
                    // and go into an HTML email sent from our domain, so they are sanitized like the stored ticket is.
                    Body = template.Data!
                        .Replace("[RECEIVER_NAME]", requestDto.ReceiverName.SanitizePlainText(), StringComparison.OrdinalIgnoreCase)
                        .Replace("[BODY]", requestDto.Body.SanitizeHtml(), StringComparison.OrdinalIgnoreCase),
                    EmailAddresses = [requestDto.ReceiverEmail!],
                    From = requestDto.From,
                });
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return new(OperationResult.Failed) { Errors = [new() { Message = exc.Message, }] };
            }
        }

        public async Task<ResultData<IEnumerable<TicketReplyDto>>> GetTicketReplysAsync([NotNull] ISpecification<TicketReply> specification)
        {
            try
            {
                var uow = UnitOfWorkProvider.Value.CreateUnitOfWork();
                var lst = await uow.GetRepository<TicketReply>().GetManyQueryable(specification)
                    .OrderBy(t => t.CreationDate).ThenBy(t => t.Id)
                    .Select(t => new
                    {
                        t.Id,
                        t.CreationDate,
                        t.Body,
                        CreationUser = t.CreationUser == null ? null : t.CreationUser.FirstName + " " + t.CreationUser.LastName,
                        t.FileId,
                        t.Receivers,
                    }).ToListAsync();

                List<TicketReplyDto> result = new(lst.Count);
                for (var i = 0; i < lst.Count; i++)
                {
                    result.Add(new()
                    {
                        Id = lst[i].Id,
                        Body = lst[i].Body,
                        CreationDate = lst[i].CreationDate,
                        CreationUser = lst[i].CreationUser,
                        Receivers = lst[i].Receivers,
                        FileUri = fileService.Value.GetStaticFileUrl(new()
                        {
                            FileId = lst[i].FileId,
                            ContainerType = ContainerType.Ticket,
                        }),
                    });
                }

                return new(OperationResult.Succeeded) { Data = result };
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return new(OperationResult.Failed)
                {
                    Errors = [new() { Message = exc.Message, }],
                };
            }
        }

        public async Task<ResultData<long>> ReplyTicketAsync([NotNull] ReplyTicketRequestDto requestDto)
        {
            try
            {
                var uow = UnitOfWorkProvider.Value.CreateUnitOfWork();
                var ticket = await uow.GetRepository<Ticket>().GetAsync(t => t.Id == requestDto.TicketId);
                if (ticket is null)
                {
                    return new(OperationResult.NotFound)
                    {
                        Errors = [new() { Message = Localizer.Value["TicketNotFound"] },],
                    };
                }

                var (fileId, errors) = requestDto.File is not null
                    ? await SaveFileAsync(requestDto.File)
                    : await SaveAttachmentAsync(requestDto.Attachments);
                if (errors is not null)
                {
                    return new(OperationResult.Failed)
                    {
                        Errors = errors,
                    };
                }

                var reply = new TicketReply
                {
                    TicketId = requestDto.TicketId,
                    Body = requestDto.Body.SanitizeHtml() ?? string.Empty,
                    CreationDate = DateTimeOffset.UtcNow,
                    // IsRead means "read by the customer", IsReadByAdmin "read by admin": whoever wrote the reply has
                    // read it, the other side hasn't, so the two are always opposites (fixed 2026-09-09, they used to be
                    // equal) - see docs/business/support-and-social.md.
                    IsRead = !requestDto.ReplyByAdmin,
                    IsReadByAdmin = requestDto.ReplyByAdmin,
                    CreationUserId = requestDto.CreationUserId,
                    FileId = fileId,
                    Receivers = requestDto.Receivers,
                };
                uow.GetRepository<TicketReply>().Add(reply);

                // Every reply moves the ticket's LastActivityDate (both lists sort on it). A non-admin reply (customer,
                // or an inbound email matched to the ticket by ProccessInboundEmailAsync) is new correspondence admin
                // hasn't seen, so it also reopens the ticket as unread even if an admin had marked it read. The ticket
                // is tracked, so the reply and these changes are saved by one SaveChanges, in one transaction.
                ticket.LastActivityDate = reply.CreationDate;
                if (!requestDto.ReplyByAdmin)
                {
                    ticket.IsReadByAdmin = false;
                }

                _ = await uow.SaveChangesAsync();

                if (requestDto.ReplyByAdmin)
                {
                    _ = await emailService.Value.SendEmailAsync(new()
                    {
                        Body = requestDto.Body,
                        Subject = GenerateSubject(requestDto.TicketId, ticket.Subject),
                        EmailAddresses = [ticket.Email!],
                        From = requestDto.From,
                    });
                }

                return new(OperationResult.Succeeded) { Data = reply.Id };
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return new(OperationResult.Failed) { Errors = [new() { Message = exc.Message, }] };
            }
        }

        public async Task<ResultData<bool>> SetReplysAsReadedByAdminAsync([NotNull] ISpecification<TicketReply> specification)
        {
            try
            {
                // Only the replies still unread are written, so opening a thread again is a no-op. No rows means
                // there was nothing unread, which is not an error.
                var uow = UnitOfWorkProvider.Value.CreateUnitOfWork();
                var rowAffected = await uow.GetRepository<TicketReply>().GetManyQueryable(specification)
                    .Where(t => !t.IsReadByAdmin)
                    .ExecuteUpdateAsync(t => t.SetProperty(p => p.IsReadByAdmin, true));

                return new(OperationResult.Succeeded) { Data = rowAffected > 0 };
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return new(OperationResult.Failed) { Errors = [new() { Message = exc.Message, },] };
            }
        }

        public async Task<ResultData<bool>> SetReplysAsReadedByUserAsync([NotNull] ISpecification<TicketReply> specification)
        {
            try
            {
                // Only the replies still unread are written, so opening a thread again is a no-op. No rows means
                // there was nothing unread, which is not an error.
                var uow = UnitOfWorkProvider.Value.CreateUnitOfWork();
                var rowAffected = await uow.GetRepository<TicketReply>().GetManyQueryable(specification)
                    .Where(t => !t.IsRead)
                    .ExecuteUpdateAsync(t => t.SetProperty(p => p.IsRead, true));

                return new(OperationResult.Succeeded) { Data = rowAffected > 0 };
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return new(OperationResult.Failed) { Errors = [new() { Message = exc.Message, },] };
            }
        }

        public async Task<ResultData<bool>> ToggleIsReadByAdminAsync([NotNull] ISpecification<Ticket> specification)
        {
            try
            {
                var uow = UnitOfWorkProvider.Value.CreateUnitOfWork();
                var rowAffected = await uow.GetRepository<Ticket>().GetManyQueryable(specification)
                    .ExecuteUpdateAsync(t => t.SetProperty(p => p.IsReadByAdmin, p => !p.IsReadByAdmin));

                return rowAffected == 0
                    ? new(OperationResult.NotFound)
                    {
                        Errors = [new() { Message = Localizer.Value["TicketNotFound"] },],
                    }
                    : new(OperationResult.Succeeded) { Data = true };
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return new(OperationResult.Failed) { Errors = [new() { Message = exc.Message, },] };
            }
        }

        public async Task<ResultData<bool>> SetAsReadByAdminAsync([NotNull] ISpecification<Ticket> specification)
        {
            try
            {
                // Opening a ticket marks it read; it never flips a read ticket back to unread (ToggleIsReadByAdminAsync,
                // used by the explicit toggle endpoint, does that). Already-read rows are skipped.
                var uow = UnitOfWorkProvider.Value.CreateUnitOfWork();
                var rowAffected = await uow.GetRepository<Ticket>().GetManyQueryable(specification)
                    .Where(t => !t.IsReadByAdmin)
                    .ExecuteUpdateAsync(t => t.SetProperty(p => p.IsReadByAdmin, true));

                return new(OperationResult.Succeeded) { Data = rowAffected > 0 };
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return new(OperationResult.Failed) { Errors = [new() { Message = exc.Message, },] };
            }
        }

        public async Task<ResultData<bool>> RemoveTicketAsync([NotNull] ISpecification<Ticket> specification)
        {
            try
            {
                var uow = UnitOfWorkProvider.Value.CreateUnitOfWork();
                var repository = uow.GetRepository<Ticket>();
                var ticket = await repository.GetAsync(specification);
                if (ticket is null)
                {
                    return new(OperationResult.NotFound)
                    {
                        Errors = [new() { Message = Localizer.Value["TicketNotFound"] },],
                    };
                }

                // The replies are deleted by the database (cascade), but their files are not, so collect them first.
                var replyFileIds = await uow.GetRepository<TicketReply>().GetManyQueryable(t => t.TicketId == ticket.Id && t.FileId != null)
                    .Select(t => t.FileId).ToListAsync();

                repository.Remove(ticket);
                _ = await uow.SaveChangesAsync();

                // Files go only after the rows are gone, so a failed delete never leaves a ticket pointing at a missing file.
                foreach (var fileId in replyFileIds.Prepend(ticket.FileId).Where(t => !string.IsNullOrEmpty(t)))
                {
                    _ = await fileService.Value.RemoveFileAsync(new()
                    {
                        ContainerType = ContainerType.Ticket,
                        FileId = fileId,
                    });
                }

                return new(OperationResult.Succeeded) { Data = true };
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return new(OperationResult.Failed) { Errors = [new() { Message = exc.Message, },] };
            }
        }

        public async Task ProccessInboundEmailAsync(HttpRequest request)
        {
            var result = await emailService.Value.ProccessInboundEmailAsync(request);
            if (result.OperationResult is not OperationResult.Succeeded)
            {
                // Fixed 2026-09-09: this used to fall through to the null-data early return just below
                // with zero logging - combined with InboundWebHook always answering Resend/Svix with 200
                // regardless (so it never retries a dropped email), a failure here left no trace anywhere.
                // ResendEmailProvider's own failure branches already log their specific cause; this just
                // makes the fact that a reply/ticket was NOT created from this webhook visible from the
                // ticket side too. See docs/business/support-and-social.md.
                Logger.Value.LogWarning("ProccessInboundEmailAsync: inbound email webhook processing failed - {Errors}",
                    string.Join("; ", result.Errors?.Select(t => t.Message) ?? []));
            }

            if (result.Data is null)
            {
                return;
            }

            var match = TicketRegex().Match(result.Data.Subject!);
            if (match.Success)
            {
                var ticketId = match.Groups[1].Value.ValueOf<long?>();
                if (ticketId.HasValue)
                {
                    var uow = UnitOfWorkProvider.Value.CreateUnitOfWork();
                    var info = await uow.GetRepository<Ticket>().GetManyQueryable(t => t.Id == ticketId).Select(t => new
                    {
                        t.Email,
                        t.UserId,
                    }).FirstOrDefaultAsync();
                    // The sender's address must equal the ticket's email exactly. This used to be a substring check, so
                    // "victim@x.com.evil.io" could reply into victim@x.com's ticket.
                    if (info is not null && string.Equals(ParseEmailAddress(result.Data.From), info.Email, StringComparison.OrdinalIgnoreCase))
                    {
                        _ = await ReplyTicketAsync(new()
                        {
                            Body = result.Data.Body!,
                            TicketId = ticketId.Value,
                            ReplyByAdmin = false,
                            CreationUserId = info.UserId,
                            Receivers = result.Data.To?.ToList(),
                            Attachments = result.Data.Attachments,
                        });
                        return;
                    }
                }
            }

            var fromEmail = ParseEmailAddress(result.Data.From) ?? result.Data.From;
            var data = await identityService.Value.GetUserFullNameAsync(new EmailEqualsSpecification(fromEmail!));
            var name = data.Data?.FullName ?? "Customer";
            var ticket = await CreateTicketAsync(new()
            {
                Body = result.Data.Body,
                Subject = result.Data.Subject,
                Email = fromEmail,
                FullName = name,
                UserId = data.Data?.Id,
                Receivers = result.Data.To?.ToList(),
                Attachments = result.Data.Attachments,
            });
            if (ticket.OperationResult is OperationResult.Succeeded)
            {
                var supportEmail = emailService.Value.GetSupportEmail();
                if (result.Data.To is null || !result.Data.To.Any(t => supportEmail.Contains(t!, StringComparison.OrdinalIgnoreCase)))
                {
                    return;
                }

                _ = await SendTicketConfirmationAsync(new()
                {
                    Body = result.Data.Body,
                    Subject = result.Data.Subject,
                    TicketId = ticket.Data,
                    ReceiverEmail = fromEmail,
                    ReceiverName = name,
                });
            }
        }

        public string GenerateSubject(long ticketId, string? subject) => $"[Ticket-{ticketId}] {subject}";

        // Both ticket lists have a fixed business order. A client SortFilter would replace it in FilterListAsync (a second
        // OrderBy discards the first), so it is dropped; paging and search filters are kept.
        private static PagingDto? WithoutSort(PagingDto? pagingDto) => pagingDto is null
            ? null
            : new() { PageFilter = pagingDto.PageFilter, SearchFilter = pagingDto.SearchFilter };

        // Same limits as a web upload (CreateTicketRequestViewModel / Reply*RequestViewModel.File).
        private const int MaxAttachmentSize = 1024 * 1024 * 2;
        private static readonly string[] AllowedAttachmentExtensions = (ValidWebExtensions + "," + ValidImageExtensions).Split(',');

        private async Task<(string? FileId, IEnumerable<Error>? Errors)> SaveFileAsync(IFormFile? file)
        {
            if (file is null)
            {
                return (null, null);
            }

            var fileId = await fileService.Value.CreateFileAsync(new()
            {
                File = file,
                ContainerType = ContainerType.Ticket,
            });

            return fileId.OperationResult is OperationResult.Succeeded
                ? ((string? ImageId, IEnumerable<Error>? Errors))(fileId.Data, null)
                : new(null, fileId.Errors);
        }

        /// <summary>
        /// Stores the first inbound-email attachment that passes the same checks as a web upload (allowed extension, at
        /// most <see cref="MaxAttachmentSize"/>). A ticket or reply holds one file, so any others are logged and skipped.
        /// A file that fails the checks is skipped too, never failing the whole email.
        /// </summary>
        private async Task<(string? FileId, IEnumerable<Error>? Errors)> SaveAttachmentAsync(IEnumerable<EmailDto.AttachmentDto>? attachments)
        {
            if (attachments is null)
            {
                return (null, null);
            }

            string? fileId = null;
            foreach (var attachment in attachments)
            {
                var extension = Path.GetExtension(attachment.Filename)?.TrimStart('.');
                var allowed = attachment.File is { Length: > 0 and <= MaxAttachmentSize }
                    && !string.IsNullOrEmpty(extension)
                    && AllowedAttachmentExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase);
                if (fileId is not null || !allowed)
                {
                    Logger.Value.LogWarning("Inbound ticket email: attachment '{Filename}' was not stored ({Reason})",
                        attachment.Filename, fileId is not null ? "only one file per ticket/reply" : "type or size not allowed");
                    continue;
                }

                using var stream = new MemoryStream(attachment.File);
                var formFile = new FormFile(stream, 0, stream.Length, "File", Path.GetFileName(attachment.Filename!))
                {
                    Headers = new HeaderDictionary(),
                    ContentType = attachment.ContentType ?? "application/octet-stream",
                };
                var (id, errors) = await SaveFileAsync(formFile);
                if (errors is not null)
                {
                    Logger.Value.LogWarning("Inbound ticket email: attachment '{Filename}' could not be stored - {Errors}",
                        attachment.Filename, string.Join("; ", errors.Select(t => t.Message)));
                    continue;
                }

                fileId = id;
            }

            return (fileId, null);
        }

        /// <summary>The bare address of an email header value such as <c>"Jane Doe" &lt;jane@x.com&gt;</c>, lower-cased; null if it doesn't parse.</summary>
        private static string? ParseEmailAddress(string? value) =>
            MailAddress.TryCreate(value, out var address) ? address.Address.ToLowerInvariant() : null;

        [GeneratedRegex("\\[Ticket-(\\d*)]")]
        private static partial Regex TicketRegex();
    }
}
