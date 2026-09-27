namespace GamaEdtech.Application.Service
{
    using System;
    using System.Diagnostics.CodeAnalysis;
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
                // ThenByDescending, not ThenBy (fixed 2026-09-09): within each IsReadByAdmin bucket, a
                // ticket with a fresh unread reply (true) must sort BEFORE one with none (false) - the
                // whole point of this key is to surface new correspondence to admin. The previous
                // ascending ThenBy sank exactly the tickets it was meant to highlight to the bottom of
                // their bucket instead - see docs/business/support-and-social.md.
                var result = await uow.GetRepository<Ticket>().GetManyQueryable(requestDto?.Specification)
                    .OrderBy(t => t.IsReadByAdmin).ThenByDescending(t => t.TicketReplys.Any(r => !r.IsReadByAdmin)).ThenByDescending(t => t.CreationDate).FilterListAsync(requestDto?.PagingDto);
                var users = await result.List.Select(t => new TicketsDto
                {
                    Id = t.Id,
                    FullName = t.FullName,
                    Email = t.Email,
                    IsReadByAdmin = t.IsReadByAdmin,
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

        public async Task<ResultData<ListDataSource<TicketsDto>>> GetUserTicketsAsync(ListRequestDto<Ticket>? requestDto = null)
        {
            try
            {
                var uow = UnitOfWorkProvider.Value.CreateUnitOfWork();
                // OrderByDescending, not OrderBy (fixed 2026-09-09, same bug as GetTicketsAsync's admin
                // sort above): a ticket with a fresh unread-by-the-customer reply (true) must sort
                // BEFORE one with none (false), not after.
                var result = await uow.GetRepository<Ticket>().GetManyQueryable(requestDto?.Specification)
                    .OrderByDescending(t => t.TicketReplys.Any(r => !r.IsRead)).ThenByDescending(t => t.CreationDate).FilterListAsync(requestDto?.PagingDto);
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

        public async Task<ResultData<long>> CreateTicketAsync([NotNull] CreateTicketRequestDto requestDto)
        {
            try
            {
                var uow = UnitOfWorkProvider.Value.CreateUnitOfWork();
                var repository = uow.GetRepository<Ticket>();

                var (fileId, errors) = await SaveFileAsync(requestDto.File);
                if (errors is not null)
                {
                    return new(OperationResult.Failed)
                    {
                        Errors = errors,
                    };
                }

                var ticket = new Ticket
                {
                    FullName = requestDto.FullName.SanitizePlainText(),
                    Body = requestDto.Body.SanitizeHtml(),
                    Email = requestDto.Email?.ToLowerInvariant(),
                    Subject = requestDto.Subject.SanitizePlainText(),
                    CreationDate = DateTimeOffset.UtcNow,
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
                    Body = template.Data!
                        .Replace("[RECEIVER_NAME]", requestDto.ReceiverName, StringComparison.OrdinalIgnoreCase)
                        .Replace("[BODY]", requestDto.Body, StringComparison.OrdinalIgnoreCase),
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
                var lst = await uow.GetRepository<TicketReply>().GetManyQueryable(specification).Select(t => new
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
                var repository = uow.GetRepository<TicketReply>();

                var (fileId, errors) = await SaveFileAsync(requestDto.File);
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
                    // Bug fixed 2026-09-09: these were both `!requestDto.ReplyByAdmin` - identical
                    // expressions for two fields that track different audiences. IsRead means "read
                    // by the customer" (true when the customer themselves just wrote it), IsReadByAdmin
                    // means "read by admin" (true when admin themselves just wrote it) - the two must be
                    // opposites of each other for a given reply, not the same value. The old code
                    // stamped a fresh customer reply as already IsReadByAdmin=true, which silently broke
                    // every "has an unread reply" signal used to surface it in the admin ticket list -
                    // see GetTicketsAsync's sort below and docs/business/support-and-social.md.
                    IsRead = !requestDto.ReplyByAdmin,
                    IsReadByAdmin = requestDto.ReplyByAdmin,
                    CreationUserId = requestDto.CreationUserId,
                    FileId = fileId,
                    Receivers = requestDto.Receivers,
                };
                repository.Add(reply);

                if (!requestDto.ReplyByAdmin)
                {
                    // A non-admin reply (customer, or an inbound email matched to an existing ticket via
                    // ProccessInboundEmailAsync) means there's new correspondence admin hasn't seen yet -
                    // reopen the ticket itself as unread, even if an admin had previously marked it read.
                    // Without this, a ticket an admin already opened once stays in GetTicketsAsync's
                    // "already read" bucket forever, regardless of new replies arriving on it.
                    _ = await uow.GetRepository<Ticket>().GetManyQueryable(t => t.Id == requestDto.TicketId)
                        .ExecuteUpdateAsync(t => t.SetProperty(p => p.IsReadByAdmin, false));
                }

                _ = await uow.SaveChangesAsync();

                if (requestDto.ReplyByAdmin)
                {
                    var data = await uow.GetRepository<Ticket>().GetManyQueryable(t => t.Id == requestDto.TicketId).Select(t => new
                    {
                        t.Email,
                        t.Subject,
                    }).FirstOrDefaultAsync();

                    _ = await emailService.Value.SendEmailAsync(new()
                    {
                        Body = requestDto.Body,
                        Subject = GenerateSubject(requestDto.TicketId, data!.Subject),
                        EmailAddresses = [data.Email!],
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
                var uow = UnitOfWorkProvider.Value.CreateUnitOfWork();
                var rowAffected = await uow.GetRepository<TicketReply>().GetManyQueryable(specification)
                    .ExecuteUpdateAsync(t => t.SetProperty(p => p.IsReadByAdmin, true));

                return rowAffected == 0
                    ? new(OperationResult.NotFound)
                    {
                        Errors = [new() { Message = Localizer.Value["TicketReplyNotFound"] },],
                    }
                    : new(OperationResult.Succeeded) { Data = true };
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
                var uow = UnitOfWorkProvider.Value.CreateUnitOfWork();
                var rowAffected = await uow.GetRepository<TicketReply>().GetManyQueryable(specification)
                    .ExecuteUpdateAsync(t => t.SetProperty(p => p.IsRead, true));

                return rowAffected == 0
                    ? new(OperationResult.NotFound)
                    {
                        Errors = [new() { Message = Localizer.Value["TicketReplyNotFound"] },],
                    }
                    : new(OperationResult.Succeeded) { Data = true };
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

                repository.Remove(ticket);
                _ = uow.SaveChangesAsync();

                _ = await fileService.Value.RemoveFileAsync(new()
                {
                    ContainerType = ContainerType.Ticket,
                    FileId = ticket.FileId,
                });

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
                    if (info is not null && result.Data.From!.Contains(info.Email!, StringComparison.OrdinalIgnoreCase))
                    {
                        _ = await ReplyTicketAsync(new()
                        {
                            Body = result.Data.Body!,
                            TicketId = ticketId.Value,
                            ReplyByAdmin = false,
                            CreationUserId = info.UserId,
                            Receivers = result.Data.To?.ToList(),
                        });
                        return;
                    }
                }
            }

            var data = await identityService.Value.GetUserFullNameAsync(new EmailEqualsSpecification(result.Data.From!));
            var name = data.Data?.FullName ?? "Customer";
            var ticket = await CreateTicketAsync(new()
            {
                Body = result.Data.Body,
                Subject = result.Data.Subject,
                Email = result.Data.From,
                FullName = name,
                UserId = data.Data?.Id,
                Receivers = result.Data.To?.ToList(),
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
                    ReceiverEmail = result.Data.From,
                    ReceiverName = name,
                });
            }
        }

        public string GenerateSubject(long ticketId, string? subject) => $"[Ticket-{ticketId}] {subject}";

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

        [GeneratedRegex("\\[Ticket-(\\d*)]")]
        private static partial Regex TicketRegex();
    }
}
