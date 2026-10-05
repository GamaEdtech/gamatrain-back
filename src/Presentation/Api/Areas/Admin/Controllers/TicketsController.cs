namespace GamaEdtech.Presentation.Api.Areas.Admin.Controllers
{
    using System.Diagnostics.CodeAnalysis;
    using System.Linq;

    using Asp.Versioning;

    using GamaEdtech.Application.Interface;
    using GamaEdtech.Common.Core;
    using GamaEdtech.Common.Data;
    using GamaEdtech.Common.DataAccess.Specification.Impl;
    using GamaEdtech.Common.DataAnnotation;
    using GamaEdtech.Common.Identity;
    using GamaEdtech.Domain.Entity;
    using GamaEdtech.Domain.Enumeration;
    using GamaEdtech.Domain.Specification.Ticket;
    using GamaEdtech.Presentation.ViewModel.Ticket;

    using Microsoft.AspNetCore.Mvc;

    using static GamaEdtech.Common.Core.Constants;

    [Common.DataAnnotation.Area(nameof(Admin), "Admin")]
    [Route("api/v{version:apiVersion}/[area]/[controller]")]
    [ApiVersion("1.0")]
    [Permission(Roles = [nameof(Role.Admin)])]
    public class TicketsController(Lazy<ILogger<TicketsController>> logger, Lazy<ITicketService> ticketService, Lazy<IEmailService> emailService)
        : ApiControllerBase<TicketsController>(logger)
    {
        [HttpGet, Produces<ApiResponse<ListDataSource<TicketsResponseViewModel>>>()]
        [Display(Name = "Get list of tickets")]
        public async Task<IActionResult<ListDataSource<TicketsResponseViewModel>>> GetTickets([NotNull, FromQuery] TicketsRequestViewModel request)
        {
            try
            {
                var result = await ticketService.Value.GetTicketsAsync(new ListRequestDto<Ticket>
                {
                    PagingDto = request.PagingDto,
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
                            IsReadByAdmin = t.IsReadByAdmin,
                            HasNewReply = t.HasNewReply,
                            CreationDate = t.CreationDate,
                            LastActivityDate = t.LastActivityDate,
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

        [HttpPost, Produces<ApiResponse<ManageTicketResponseViewModel>>()]
        [Display(Name = "Send ticket to user")]
        public async Task<IActionResult<ManageTicketResponseViewModel>> SendTicket([NotNull, FromForm] SendTicketRequestViewModel request)
        {
            try
            {
                var validationResult = emailService.Value.ValidateFromEmailAddress(request.From);
                if (!validationResult.Data)
                {
                    return Ok<ManageTicketResponseViewModel>(new() { Errors = validationResult.Errors });
                }

                var result = await ticketService.Value.CreateTicketAsync(new()
                {
                    Body = request.Body,
                    Email = request.ReceiverEmail,
                    Subject = request.Subject,
                    File = request.File,
                    FullName = request.ReceiverName,
                });
                if (result.OperationResult is OperationResult.Succeeded)
                {
                    _ = await emailService.Value.SendEmailAsync(new()
                    {
                        Subject = ticketService.Value.GenerateSubject(result.Data, request.Subject),
                        Body = request.Body!,
                        EmailAddresses = [request.ReceiverEmail!],
                        From = request.From,
                    });
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

        [HttpGet("{id:long}"), Produces<ApiResponse<TicketResponseViewModel>>()]
        [Display(Name = "Get a Ticket Details")]
        public async Task<IActionResult<TicketResponseViewModel>> GetTicket([FromRoute] long id)
        {
            try
            {
                var specification = new IdEqualsSpecification<Ticket, long>(id);
                var result = await ticketService.Value.GetTicketAsync(specification);
                if (result.OperationResult is not OperationResult.Succeeded)
                {
                    return Ok<TicketResponseViewModel>(new(result.Errors));
                }

                _ = await ticketService.Value.SetAsReadByAdminAsync(specification);

                return Ok<TicketResponseViewModel>(new()
                {
                    Data = new()
                    {
                        Id = result.Data!.Id,
                        FullName = result.Data.FullName,
                        CreationUser = result.Data.CreationUser,
                        Email = result.Data.Email,
                        Subject = result.Data.Subject,
                        Body = result.Data.Body,
                        Receivers = result.Data.Receivers,
                        CreationDate = result.Data.CreationDate,
                        FileUri = result.Data.FileUri,
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
                var specification = new TicketIdEqualsSpecification(id);
                var result = await ticketService.Value.GetTicketReplysAsync(specification);
                if (result.OperationResult is not OperationResult.Succeeded)
                {
                    return Ok<IEnumerable<TicketReplyResponseViewModel>>(new(result.Errors));
                }

                _ = await ticketService.Value.SetReplysAsReadedByAdminAsync(specification);

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
        public async Task<IActionResult<Void>> Reply([FromRoute] long id, [NotNull, FromForm] ReplyTicketByAdminRequestViewModel request)
        {
            try
            {
                var validationResult = emailService.Value.ValidateFromEmailAddress(request.From);
                if (!validationResult.Data)
                {
                    return Ok<Void>(new() { Errors = validationResult.Errors });
                }

                var result = await ticketService.Value.ReplyTicketAsync(new()
                {
                    TicketId = id,
                    Body = request.Body!,
                    File = request.File,
                    CreationUserId = User.UserId(),
                    From = request.From,
                    ReplyByAdmin = true,
                });
                return Ok<Void>(new(result.Errors));
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);

                return Ok<Void>(new() { Errors = new[] { new Error { Message = exc.Message } } });
            }
        }

        [HttpPatch("{id:long}/toggle"), Produces<ApiResponse<bool>>()]
        [Display(Name = "Toggle a Ticket as readed or not by Admin")]
        public async Task<IActionResult<bool>> ToggleIsReadByAdmin([FromRoute] long id)
        {
            try
            {
                var result = await ticketService.Value.ToggleIsReadByAdminAsync(new IdEqualsSpecification<Ticket, long>(id));
                return Ok<bool>(new(result.Errors)
                {
                    Data = result.Data
                });
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);

                return Ok<bool>(new() { Errors = [new() { Message = exc.Message }] });
            }
        }

        [HttpDelete("{id:long}"), Produces<ApiResponse<bool>>()]
        [Display(Name = "Remove a Ticket")]
        public async Task<IActionResult<bool>> RemoveTicket([FromRoute] long id)
        {
            try
            {
                var result = await ticketService.Value.RemoveTicketAsync(new IdEqualsSpecification<Ticket, long>(id));
                return Ok<bool>(new(result.Errors)
                {
                    Data = result.Data
                });
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);

                return Ok<bool>(new() { Errors = [new() { Message = exc.Message }] });
            }
        }
    }
}
