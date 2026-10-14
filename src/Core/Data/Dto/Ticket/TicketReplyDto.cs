namespace GamaEdtech.Data.Dto.Ticket
{
    using System;
    using System.Collections.Generic;

    public sealed class TicketReplyDto
    {
        public long Id { get; set; }
        public string? CreationUser { get; set; }
        public bool BySupport { get; set; }
        public string? Body { get; set; }
        public DateTimeOffset CreationDate { get; set; }
        public string? FileUri { get; set; }
        public IEnumerable<string?>? Receivers { get; set; }
    }
}
