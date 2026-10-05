namespace GamaEdtech.Presentation.Api.Controllers
{
    using System.Diagnostics.CodeAnalysis;
    using System.Security.Claims;

    using Asp.Versioning;

    using GamaEdtech.Application.Interface;
    using GamaEdtech.Common.Core;
    using GamaEdtech.Common.Data;
    using GamaEdtech.Common.DataAccess.Specification;
    using GamaEdtech.Common.DataAccess.Specification.Impl;
    using GamaEdtech.Common.DataAnnotation;
    using GamaEdtech.Common.Identity;
    using GamaEdtech.Common.Resources;
    using GamaEdtech.Domain.Entity;
    using GamaEdtech.Domain.Specification.Ticket;
    using GamaEdtech.Presentation.ViewModel.Ticket;

    using Hangfire;

    using Microsoft.AspNetCore.Authorization;
    using Microsoft.AspNetCore.Mvc;

    using static GamaEdtech.Common.Core.Constants;

    [Route("api/v{version:apiVersion}/[controller]")]
    [ApiVersion("1.0")]
    [Permission(policy: null)]
    public class TicketsController(Lazy<ILogger<TicketsController>> logger, Lazy<ITicketService> ticketService
        , Lazy<IGlobalService> globalService)
        : ApiControllerBase<TicketsController>(logger)
    {
        [HttpGet, Produces<ApiResponse<ListDataSource<TicketsResponseViewModel>>>()]
        [Display(Name = "Get List of Tickets")]
        public async Task<IActionResult<ListDataSource<TicketsResponseViewModel>>> GetTickets([NotNull, FromQuery] TicketsRequestViewModel request)
        {
            try
            {
                var result = await ticketService.Value.GetUserTicketsAsync(new ListRequestDto<Ticket>
                {
                    PagingDto = request.PagingDto,
                    Specification = new UserTicketsSpecification(User),
                });
                return Ok<ListDataSource<TicketsResponseViewModel>>(new(result.Errors)
                {
                    Data = result.Data.List is null ? new() : new()
                    {
                        List = result.Data.List.Select(t => new TicketsResponseViewModel
                        {
                            Id = t.Id,
                            Sender = t.FullName,
                            Subject = t.Subject,
                            Email = t.Email,
                            CreationDate = t.CreationDate,
                            Receivers = t.Receivers,
                        }),
                        TotalRecordsCount = result.Data.TotalRecordsCount,
                    }
                });
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);

                return Ok<ListDataSource<TicketsResponseViewModel>>(new() { Errors = [new() { Message = exc.Message }] });
            }
        }

        [HttpGet("{id:long}"), Produces<ApiResponse<TicketResponseViewModel>>()]
        [Display(Name = "Get a Ticket Details")]
        public async Task<IActionResult<TicketResponseViewModel>> GetTicket([FromRoute] long id)
        {
            try
            {
                var specification = new IdEqualsSpecification<Ticket, long>(id)
                    .And(new UserTicketsSpecification(User));
                // A customer opening their ticket must not touch the admin's read flag (it used to toggle it).
                var result = await ticketService.Value.GetTicketAsync(specification);
                return result.OperationResult is not OperationResult.Succeeded
                    ? Ok<TicketResponseViewModel>(new(result.Errors))
                    : Ok<TicketResponseViewModel>(new()
                    {
                        Data = new()
                        {
                            Id = result.Data!.Id,
                            FullName = result.Data.FullName,
                            CreationUser = result.Data.CreationUser,
                            Email = result.Data.Email,
                            Subject = result.Data.Subject,
                            Body = result.Data.Body,
                            CreationDate = result.Data.CreationDate,
                            FileUri = result.Data.FileUri,
                            Receivers = result.Data.Receivers,
                        }
                    });
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);

                return Ok<TicketResponseViewModel>(new() { Errors = [new() { Message = exc.Message }] });
            }
        }

        [HttpGet("{id:long}/replys"), Produces<ApiResponse<IEnumerable<TicketReplyResponseViewModel>>>()]
        [Display(Name = "Get All of Replys of a Ticket")]
        public async Task<IActionResult<IEnumerable<TicketReplyResponseViewModel>>> GetTicketReplys([FromRoute] long id)
        {
            try
            {
                var specification = new UserTicketReplysSpecification(id, User);
                var result = await ticketService.Value.GetTicketReplysAsync(specification);
                if (result.OperationResult is not OperationResult.Succeeded)
                {
                    return Ok<IEnumerable<TicketReplyResponseViewModel>>(new(result.Errors));
                }

                _ = await ticketService.Value.SetReplysAsReadedByUserAsync(specification);

                return Ok<IEnumerable<TicketReplyResponseViewModel>>(new()
                {
                    Data = result.Data?.Select(t => new TicketReplyResponseViewModel
                    {
                        Id = t.Id,
                        Body = t.Body,
                        CreationDate = t.CreationDate,
                        CreationUser = t.CreationUser,
                        FileUri = t.FileUri,
                        Receivers = t.Receivers,
                    }),
                });
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);

                return Ok<IEnumerable<TicketReplyResponseViewModel>>(new() { Errors = [new() { Message = exc.Message }] });
            }
        }

        [HttpPost("{id:long}/replys"), Produces(typeof(ApiResponse<Void>))]
        [Display(Name = "Reply a Ticket")]
        public async Task<IActionResult> Reply([FromRoute] long id, [NotNull, FromForm] ReplyTicketByUserRequestViewModel request)
        {
            try
            {
                // The ticket must be the caller's own: ReplyTicketAsync itself doesn't check ownership (the admin area
                // replies to any ticket), and without this any signed-in user could post into anyone's thread.
                var exists = await ticketService.Value.ExistsTicketAsync(new IdEqualsSpecification<Ticket, long>(id).And(new UserTicketsSpecification(User)));
                if (exists.OperationResult is not OperationResult.Succeeded || !exists.Data)
                {
                    return Ok<Void>(new(exists.Errors ?? [new() { Message = "Ticket not found" }]));
                }

                var result = await ticketService.Value.ReplyTicketAsync(new()
                {
                    TicketId = id,
                    Body = request.Body!,
                    File = request.File,
                    CreationUserId = User.UserId(),
                    ReplyByAdmin = false,
                });
                return Ok<Void>(new(result.Errors));
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);

                return Ok<Void>(new() { Errors = new[] { new Error { Message = exc.Message } } });
            }
        }

        [HttpPost, Produces<ApiResponse<ManageTicketResponseViewModel>>()]
        [AllowAnonymous]
        public async Task<IActionResult<ManageTicketResponseViewModel>> CreateTicket([NotNull, FromForm] CreateTicketRequestViewModel request)
        {
            try
            {
                var validateCaptcha = await globalService.Value.VerifyCaptchaAsync(request.Captcha);
                if (!validateCaptcha.Data)
                {
                    return Ok<ManageTicketResponseViewModel>(new(new Error { Message = "Invalid Captcha" }));
                }

                var isAuthenticated = User.Identity?.IsAuthenticated == true;
                long? userId = isAuthenticated ? User.UserId() : null;

                // A signed-in user's tickets always go to their account email (replies are mailed there and it is
                // how UserTicketsSpecification finds them). The posted email is only used by anonymous callers and
                // by accounts that have no email on file (e.g. phone-only sign-ups).
                var accountEmail = isAuthenticated ? User.FindFirstValue(ClaimTypes.Email) : null;
                var email = string.IsNullOrEmpty(accountEmail) ? request.Email : accountEmail;
                if (string.IsNullOrEmpty(email))
                {
                    var msg = GlobalResource.Validation_Required;
                    return Ok<ManageTicketResponseViewModel>(new(new Error
                    {
                        Message = string.Format(msg, Globals.DisplayNameFor<CreateTicketRequestViewModel>(t => t.Email!)),
                    }));
                }

                var result = await ticketService.Value.CreateTicketAsync(new()
                {
                    Body = request.Body,
                    Email = email,
                    FullName = request.FullName,
                    Subject = request.Subject,
                    UserId = userId,
                    File = request.File,
                });
                if (result.OperationResult is OperationResult.Succeeded)
                {
                    _ = BackgroundJob.Enqueue<ITicketService>(t => t.SendTicketConfirmationAsync(new()
                    {
                        Body = request.Body,
                        ReceiverEmail = email,
                        ReceiverName = request.FullName,
                        Subject = request.Subject,
                        TicketId = result.Data,
                    }));
                }

                return Ok<ManageTicketResponseViewModel>(new(result.Errors)
                {
                    Data = new() { Id = result.Data },
                });
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return Ok<ManageTicketResponseViewModel>(new(new Error { Message = exc.Message }));
            }
        }

        [HttpPost("inbound-webhook"), Produces<ApiResponse<Void>>()]
        [AllowAnonymous]
        public async Task<IActionResult<Void>> InboundWebHook()
        {
            try
            {
                await ticketService.Value.ProccessInboundEmailAsync(Request);
                return Ok<Void>(new());
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return Ok<Void>(new(new Error { Message = exc.Message }));
            }
        }
    }
}
