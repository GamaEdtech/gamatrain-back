namespace GamaEdtech.Application.Interface
{
    using System.Collections.Generic;

    using GamaEdtech.Common.Data;
    using GamaEdtech.Common.DataAnnotation;
    using GamaEdtech.Data.Dto.Ticket;

    /// <summary>
    /// Reads a ticket customer's subscriptions, payments and quota and turns them into short findings for the
    /// contact-us AI reply (e.g. "a renewal payment is still not confirmed", "two active subscriptions"). Code decides
    /// what is checked; the model only ever sees the findings' plain text, never the raw data.
    /// </summary>
    [Injectable]
    public interface ITicketCustomerSnapshotService
    {
        /// <param name="userId">The ticket's own <c>Ticket.UserId</c>; null when it was sent while signed out.</param>
        /// <param name="email">The ticket's email, used to find the account only when <paramref name="userId"/> is null.</param>
        Task<ResultData<IReadOnlyList<CustomerFindingDto>>> GetFindingsAsync(long? userId, string? email);
    }
}
