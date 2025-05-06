namespace GamaEdtech.Application.Service
{
    using System;
    using System.Diagnostics.CodeAnalysis;

    using GamaEdtech.Application.Interface;
    using GamaEdtech.Common.Core;
    using GamaEdtech.Common.Core.Extensions.Linq;
    using GamaEdtech.Common.Data;
    using GamaEdtech.Common.DataAccess.Specification;
    using GamaEdtech.Common.DataAccess.UnitOfWork;
    using GamaEdtech.Common.Service;
    using GamaEdtech.Data.Dto.Contact;
    using GamaEdtech.Domain.Entity;

    using Microsoft.AspNetCore.Http;
    using Microsoft.EntityFrameworkCore;
    using Microsoft.Extensions.Localization;
    using Microsoft.Extensions.Logging;

    using static GamaEdtech.Common.Core.Constants;

    public class ContactService(Lazy<IUnitOfWorkProvider> unitOfWorkProvider, Lazy<IHttpContextAccessor> httpContextAccessor, Lazy<IStringLocalizer<ContactService>> localizer
        , Lazy<ILogger<ContactService>> logger)
        : LocalizableServiceBase<ContactService>(unitOfWorkProvider, httpContextAccessor, localizer, logger), IContactService
    {
        public async Task<ResultData<ListDataSource<ContactsDto>>> GetContactsAsync(ListRequestDto<Contact>? requestDto = null)
        {
            try
            {
                var uow = UnitOfWorkProvider.Value.CreateUnitOfWork();
                var result = await uow.GetRepository<Contact>().GetManyQueryable(requestDto?.Specification).FilterListAsync(requestDto?.PagingDto);
                var users = await result.List.Select(t => new ContactsDto
                {
                    Id = t.Id,
                    Email = t.Email,
                    FullName = t.FullName,
                    IsRead = t.IsRead,
                    Subject = t.Subject,
                }).ToListAsync();
                return new(OperationResult.Succeeded) { Data = new() { List = users, TotalRecordsCount = result.TotalRecordsCount } };
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return new(OperationResult.Failed) { Errors = [new() { Message = exc.Message },] };
            }
        }

        public async Task<ResultData<ContactDto>> GetContactAsync([NotNull] ISpecification<Contact> specification)
        {
            try
            {
                var uow = UnitOfWorkProvider.Value.CreateUnitOfWork();
                var repository = uow.GetRepository<Contact>();
                var contact = await repository.GetManyQueryable(specification).Select(t => new ContactDto
                {
                    Id = t.Id,
                    Body = t.Body,
                    Subject = t.Subject,
                    FullName = t.FullName,
                    Email = t.Email,
                }).FirstOrDefaultAsync();
                if (contact is null)
                {
                    return new(OperationResult.NotFound)
                    {
                        Errors = [new() { Message = Localizer.Value["ContactNotFound"] },],
                    };
                }

                _ = await repository.GetManyQueryable(specification).ExecuteUpdateAsync(t => t.SetProperty(p => p.IsRead, true));

                return new(OperationResult.Succeeded) { Data = contact };
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return new(OperationResult.Failed) { Errors = [new() { Message = exc.Message },] };
            }
        }

        public async Task<ResultData<long>> CreateContactAsync([NotNull] CreateContactRequestDto requestDto)
        {
            try
            {
                var uow = UnitOfWorkProvider.Value.CreateUnitOfWork();
                var repository = uow.GetRepository<Contact>();
                Contact? contact = null;

                contact = new Contact
                {
                    FullName = requestDto.FullName,
                    Body = requestDto.Body,
                    Email = requestDto.Email,
                    Subject = requestDto.Subject,
                    IsRead = false,
                };
                repository.Add(contact);
                _ = await uow.SaveChangesAsync();

                return new(OperationResult.Succeeded) { Data = contact.Id };
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return new(OperationResult.Failed) { Errors = [new() { Message = exc.Message, }] };
            }
        }

        public async Task<ResultData<bool>> ToggleIsReadAsync([NotNull] ISpecification<Contact> specification)
        {
            try
            {
                var uow = UnitOfWorkProvider.Value.CreateUnitOfWork();
                var rowAffected = await uow.GetRepository<Contact>().GetManyQueryable(specification)
                    .ExecuteUpdateAsync(t => t.SetProperty(p => p.IsRead, p => !p.IsRead));

                return rowAffected == 0
                    ? new(OperationResult.NotFound)
                    {
                        Errors = [new() { Message = Localizer.Value["ContactNotFound"] },],
                    }
                    : new(OperationResult.Succeeded) { Data = true };
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return new(OperationResult.Failed) { Errors = [new() { Message = exc.Message, },] };
            }
        }

        public async Task<ResultData<bool>> RemoveContactAsync([NotNull] ISpecification<Contact> specification)
        {
            try
            {
                var uow = UnitOfWorkProvider.Value.CreateUnitOfWork();
                var rowAffected = await uow.GetRepository<Contact>().GetManyQueryable(specification)
                    .ExecuteDeleteAsync();

                return rowAffected == 0
                    ? new(OperationResult.NotFound)
                    {
                        Errors = [new() { Message = Localizer.Value["ContactNotFound"] },],
                    }
                    : new(OperationResult.Succeeded) { Data = true };
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return new(OperationResult.Failed) { Errors = [new() { Message = exc.Message, },] };
            }
        }
    }
}
