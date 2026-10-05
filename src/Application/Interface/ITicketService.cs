namespace GamaEdtech.Application.Interface
{
    using System.Diagnostics.CodeAnalysis;

    using GamaEdtech.Common.Data;
    using GamaEdtech.Common.DataAccess.Specification;
    using GamaEdtech.Common.DataAnnotation;
    using GamaEdtech.Data.Dto.Ticket;
    using GamaEdtech.Domain.Entity;

    using Microsoft.AspNetCore.Http;

    [Injectable]
    public interface ITicketService
    {
        Task<ResultData<ListDataSource<TicketsDto>>> GetTicketsAsync(ListRequestDto<Ticket>? requestDto = null);
        Task<ResultData<ListDataSource<TicketsDto>>> GetUserTicketsAsync(ListRequestDto<Ticket>? requestDto = null);
        Task<ResultData<TicketDto>> GetTicketAsync([NotNull] ISpecification<Ticket> specification);
        Task<ResultData<bool>> ExistsTicketAsync([NotNull] ISpecification<Ticket> specification);
        Task<ResultData<long>> CreateTicketAsync([NotNull] CreateTicketRequestDto requestDto);
        Task<ResultData<Void>> SendTicketConfirmationAsync([NotNull] SendTicketConfirmationRequestDto requestDto);
        Task<ResultData<IEnumerable<TicketReplyDto>>> GetTicketReplysAsync([NotNull] ISpecification<TicketReply> specification);
        Task<ResultData<long>> ReplyTicketAsync([NotNull] ReplyTicketRequestDto requestDto);
        Task<ResultData<bool>> SetReplysAsReadedByAdminAsync([NotNull] ISpecification<TicketReply> specification);
        Task<ResultData<bool>> SetReplysAsReadedByUserAsync([NotNull] ISpecification<TicketReply> specification);
        Task<ResultData<bool>> ToggleIsReadByAdminAsync([NotNull] ISpecification<Ticket> specification);
        Task<ResultData<bool>> SetAsReadByAdminAsync([NotNull] ISpecification<Ticket> specification);
        Task<ResultData<bool>> RemoveTicketAsync([NotNull] ISpecification<Ticket> specification);
        Task ProccessInboundEmailAsync(HttpRequest request);
        string GenerateSubject(long ticketId, string? subject);
    }
}
