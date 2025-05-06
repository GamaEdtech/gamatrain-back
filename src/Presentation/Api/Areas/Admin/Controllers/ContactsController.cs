namespace GamaEdtech.Presentation.Api.Areas.Admin.Controllers
{
    using System.Diagnostics.CodeAnalysis;

    using Asp.Versioning;

    using GamaEdtech.Application.Interface;
    using GamaEdtech.Common.Core;
    using GamaEdtech.Common.Data;
    using GamaEdtech.Common.DataAccess.Specification.Impl;
    using GamaEdtech.Common.Identity;
    using GamaEdtech.Domain.Entity;
    using GamaEdtech.Domain.Enumeration;
    using GamaEdtech.Presentation.ViewModel.Contact;

    using Microsoft.AspNetCore.Mvc;

    [Common.DataAnnotation.Area(nameof(Admin), "Admin")]
    [Route("api/v{version:apiVersion}/[area]/[controller]")]
    [ApiVersion("1.0")]
    [Permission(Roles = [nameof(Role.Admin)])]
    public class ContactsController(Lazy<ILogger<ContactsController>> logger, Lazy<IContactService> contactService)
        : ApiControllerBase<ContactsController>(logger)
    {
        [HttpGet, Produces<ApiResponse<ListDataSource<ContactsResponseViewModel>>>()]
        public async Task<IActionResult<ListDataSource<ContactsResponseViewModel>>> GetContacts([NotNull, FromQuery] ContactsRequestViewModel request)
        {
            try
            {
                var result = await contactService.Value.GetContactsAsync(new ListRequestDto<Contact>
                {
                    PagingDto = request.PagingDto,
                });
                return Ok<ListDataSource<ContactsResponseViewModel>>(new(result.Errors)
                {
                    Data = result.Data.List is null ? new() : new()
                    {
                        List = result.Data.List.Select(t => new ContactsResponseViewModel
                        {
                            Id = t.Id,
                            Subject = t.Subject,
                            FullName = t.FullName,
                            Email = t.Email,
                            IsRead = t.IsRead,
                        }),
                        TotalRecordsCount = result.Data.TotalRecordsCount,
                    }
                });
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);

                return Ok<ListDataSource<ContactsResponseViewModel>>(new() { Errors = [new() { Message = exc.Message }] });
            }
        }

        [HttpGet("{id:long}"), Produces<ApiResponse<ContactResponseViewModel>>()]
        public async Task<IActionResult<ContactResponseViewModel>> GetContact([FromRoute] long id)
        {
            try
            {
                var result = await contactService.Value.GetContactAsync(new IdEqualsSpecification<Contact, long>(id));
                return Ok<ContactResponseViewModel>(new(result.Errors)
                {
                    Data = result.Data is null ? null : new()
                    {
                        Id = result.Data.Id,
                        Body = result.Data.Body,
                        Email = result.Data.Email,
                        FullName = result.Data.FullName,
                        Subject = result.Data.Subject,
                    }
                });
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);

                return Ok<ContactResponseViewModel>(new() { Errors = [new() { Message = exc.Message }] });
            }
        }

        [HttpPatch("{id:long}/toggle"), Produces<ApiResponse<bool>>()]
        public async Task<IActionResult<bool>> ToggleIsRead([FromRoute] long id)
        {
            try
            {
                var result = await contactService.Value.ToggleIsReadAsync(new IdEqualsSpecification<Contact, long>(id));
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
        public async Task<IActionResult<bool>> RemoveContact([FromRoute] long id)
        {
            try
            {
                var result = await contactService.Value.RemoveContactAsync(new IdEqualsSpecification<Contact, long>(id));
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
