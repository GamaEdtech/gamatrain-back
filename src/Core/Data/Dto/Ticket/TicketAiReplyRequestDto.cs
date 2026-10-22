namespace GamaEdtech.Data.Dto.Ticket
{
    using GamaEdtech.Domain.Enumeration;

    public sealed class TicketAiReplyRequestDto
    {
        public required long TicketId { get; set; }
        public required TicketAiReplyMode Mode { get; set; }

        /// <summary>The admin's reply so far (HTML from the editor); required for <see cref="TicketAiReplyMode.Polish"/>.</summary>
        public string? Draft { get; set; }
    }
}
