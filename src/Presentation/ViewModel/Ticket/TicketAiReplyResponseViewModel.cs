namespace GamaEdtech.Presentation.ViewModel.Ticket
{
    public sealed class TicketAiReplyResponseViewModel
    {
        /// <summary>The suggested reply as HTML for the reply editor. Never sent to the customer by this endpoint.</summary>
        public string? Reply { get; set; }
    }
}
