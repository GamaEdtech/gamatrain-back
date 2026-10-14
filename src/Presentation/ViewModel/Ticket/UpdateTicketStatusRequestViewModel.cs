namespace GamaEdtech.Presentation.ViewModel.Ticket
{
    using System.Text.Json.Serialization;

    using GamaEdtech.Common.Converter;
    using GamaEdtech.Common.DataAnnotation;
    using GamaEdtech.Domain.Enumeration;

    public sealed class UpdateTicketStatusRequestViewModel
    {
        /// <summary>The new status, by name: "Open", "Answered", "Closed" or "NotImportant".</summary>
        [Display]
        [Required]
        [JsonConverter(typeof(EnumerationConverter<TicketStatus, byte>))]
        public TicketStatus? Status { get; set; }
    }
}
