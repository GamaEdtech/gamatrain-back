namespace GamaEdtech.Application.Interface
{
    using System.Diagnostics.CodeAnalysis;

    using GamaEdtech.Common.Data;
    using GamaEdtech.Common.DataAnnotation;
    using GamaEdtech.Data.Dto.Ticket;

    /// <summary>
    /// The admin contact-us AI assistant: drafts or polishes a reply to a ticket, grounded in the support handbook
    /// (<c>SupportHandbook.md</c>) so it can explain how the website works. Returns the reply as editor-ready HTML.
    /// </summary>
    [Injectable]
    public interface ITicketAiReplyService
    {
        Task<ResultData<string>> GenerateReplyAsync([NotNull] TicketAiReplyRequestDto requestDto);
    }
}
