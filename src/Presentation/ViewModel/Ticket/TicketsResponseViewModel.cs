namespace GamaEdtech.Presentation.ViewModel.Ticket
{
    using System.Collections.Generic;

    public sealed class TicketsResponseViewModel
    {
        public long Id { get; set; }
        public string? Sender { get; set; }
        public string? Email { get; set; }
        public string? Subject { get; set; }
        public bool IsReadByAdmin { get; set; }

        /// <summary>Admin list only: the customer replied and admin has not read that reply yet.</summary>
        public bool HasNewReply { get; set; }

        public DateTimeOffset CreationDate { get; set; }

        /// <summary>Admin list only: date of the newest reply, or <see cref="CreationDate"/> when there is none.</summary>
        public DateTimeOffset? LastActivityDate { get; set; }

        public IEnumerable<string?>? Receivers { get; set; }
    }
}
