namespace GamaEdtech.Data.Dto.Ticket
{
    using System.Collections.Generic;

    using Microsoft.AspNetCore.Http;

    public sealed class CreateTicketRequestDto
    {
        public required string? FullName { get; set; }
        public required string? Email { get; set; }
        public required string? Subject { get; set; }
        public required string? Body { get; set; }
        public long? UserId { get; set; }
        public IFormFile? File { get; set; }

        /// <summary>Inbound-email attachments; used only when <see cref="File"/> is null. See TicketService.SaveAttachmentAsync.</summary>
        public IEnumerable<Email.EmailDto.AttachmentDto>? Attachments { get; set; }
        public ICollection<string?>? Receivers { get; set; }
    }
}
