namespace GamaEdtech.Presentation.ViewModel.Ticket
{
    using GamaEdtech.Common.Data;
    using GamaEdtech.Common.DataAnnotation;

    public sealed class AdminTicketsRequestViewModel
    {
        [Display]
        public PagingDto? PagingDto { get; set; } = new() { PageFilter = new(), };

        /// <summary>true: only tickets with something unread by admin (the ticket or a reply); false: only fully read ones; omitted: all.</summary>
        [Display]
        public bool? Unread { get; set; }

        /// <summary>Contains-match on subject, sender name or email; a ticket number ("123", "Ticket-123") also matches the id.</summary>
        [Display]
        public string? Search { get; set; }

        /// <summary>Exact sender email - every ticket from one customer.</summary>
        [Display]
        public string? Email { get; set; }

        /// <summary>Inclusive lower bound on LastActivityDate.</summary>
        [Display]
        public DateTimeOffset? StartDate { get; set; }

        /// <summary>Inclusive upper bound on LastActivityDate.</summary>
        [Display]
        public DateTimeOffset? EndDate { get; set; }
    }
}
