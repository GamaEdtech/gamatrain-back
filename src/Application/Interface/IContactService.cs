namespace GamaEdtech.Application.Interface
{
    using System.Diagnostics.CodeAnalysis;

    using GamaEdtech.Common.Data;
    using GamaEdtech.Common.DataAccess.Specification;
    using GamaEdtech.Common.DataAnnotation;
    using GamaEdtech.Data.Dto.Contact;
    using GamaEdtech.Domain.Entity;

    [Injectable]
    public interface IContactService
    {
        Task<ResultData<ListDataSource<ContactsDto>>> GetContactsAsync(ListRequestDto<Contact>? requestDto = null);
        Task<ResultData<ContactDto>> GetContactAsync([NotNull] ISpecification<Contact> specification);
        Task<ResultData<long>> CreateContactAsync([NotNull] CreateContactRequestDto requestDto);
        Task<ResultData<bool>> ToggleIsReadAsync([NotNull] ISpecification<Contact> specification);
        Task<ResultData<bool>> RemoveContactAsync([NotNull] ISpecification<Contact> specification);
    }
}
