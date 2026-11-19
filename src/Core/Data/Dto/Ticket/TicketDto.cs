namespace GamaEdtech.Data.Dto.Ticket
{
    using System;
    using System.Collections.Generic;

    public sealed class TicketDto
    {
        public long Id { get; set; }

        /// <summary>The account that sent the ticket; null when it was sent while signed out.</summary>
        public long? UserId { get; set; }

        public string? FullName { get; set; }
        public string? CreationUser { get; set; }
        public string? Email { get; set; }
        public string? Subject { get; set; }
        public string? Body { get; set; }
        public DateTimeOffset CreationDate { get; set; }
        public string? FileUri { get; set; }
        public ICollection<string?>? Receivers { get; set; }
    }
}
