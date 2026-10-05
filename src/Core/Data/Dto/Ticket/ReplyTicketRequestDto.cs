namespace GamaEdtech.Data.Dto.Ticket
{
    using System.Collections.Generic;

    using Microsoft.AspNetCore.Http;

    public sealed class ReplyTicketRequestDto
    {
        public required long TicketId { get; set; }
        public required string Body { get; set; }
        public required bool ReplyByAdmin { get; set; }
        public string? From { get; set; }
        public long? CreationUserId { get; set; }
        public IFormFile? File { get; set; }

        /// <summary>Inbound-email attachments; used only when <see cref="File"/> is null. See TicketService.SaveAttachmentAsync.</summary>
        public IEnumerable<Email.EmailDto.AttachmentDto>? Attachments { get; set; }
        public ICollection<string?>? Receivers { get; set; }
    }
}
