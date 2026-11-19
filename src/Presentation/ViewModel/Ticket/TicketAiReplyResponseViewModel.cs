namespace GamaEdtech.Presentation.ViewModel.Ticket
{
    using System.Collections.Generic;

    public sealed class TicketAiReplyResponseViewModel
    {
        /// <summary>The suggested reply as HTML for the reply editor. Never sent to the customer by this endpoint.</summary>
        public string? Reply { get; set; }

        /// <summary>
        /// What was found on the customer's account (subscriptions, payments, quota), with ids. For the admin only:
        /// never put into the reply. The model sees these facts without the ids, and only in Generate mode.
        /// </summary>
        public IEnumerable<string>? Notes { get; set; }
    }
}
