namespace GamaEdtech.Data.Dto.Ticket
{
    using System.Collections.Generic;

    public sealed class TicketsDto
    {
        public long Id { get; set; }
        public string? FullName { get; set; }
        public string? Email { get; set; }
        public string? Subject { get; set; }
        public bool IsReadByAdmin { get; set; }
        public bool HasNewReply { get; set; }
        public DateTimeOffset CreationDate { get; set; }
        public DateTimeOffset LastActivityDate { get; set; }
        public IEnumerable<string?>? Receivers { get; set; }
    }
}
