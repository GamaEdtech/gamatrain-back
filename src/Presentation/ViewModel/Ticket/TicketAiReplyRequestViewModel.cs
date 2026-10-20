namespace GamaEdtech.Presentation.ViewModel.Ticket
{
    using System.Text.Json.Serialization;

    using GamaEdtech.Common.Converter;
    using GamaEdtech.Common.DataAnnotation;
    using GamaEdtech.Domain.Enumeration;

    public sealed class TicketAiReplyRequestViewModel
    {
        /// <summary>"Generate" writes a reply to the ticket; "Polish" improves <see cref="Draft"/>.</summary>
        [Display]
        [Required]
        [JsonConverter(typeof(EnumerationConverter<TicketAiReplyMode, byte>))]
        public TicketAiReplyMode? Mode { get; set; }

        /// <summary>The admin's reply so far (HTML from the editor); required when polishing.</summary>
        [Display]
        public string? Draft { get; set; }
    }
}
