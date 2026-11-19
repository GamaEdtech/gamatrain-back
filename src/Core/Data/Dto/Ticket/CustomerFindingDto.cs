namespace GamaEdtech.Data.Dto.Ticket
{
    /// <summary>
    /// One fact about a ticket's customer account, worked out by code (<c>TicketCustomerSnapshotService</c>) for the
    /// contact-us AI reply.
    /// </summary>
    public sealed class CustomerFindingDto
    {
        /// <summary>The fact in plain words, without ids, amounts, names or emails. This part is sent to the AI model.</summary>
        public required string Text { get; init; }

        /// <summary>Ids and other detail for the admin only (e.g. "payment 123, Stripe"); never sent to the model.</summary>
        public string? AdminDetail { get; init; }

        /// <summary>Shown to the admin but not sent to the model at all (e.g. "the ticket isn't linked to an account").</summary>
        public bool AdminOnly { get; init; }

        /// <summary>The line the admin sees: the text plus its detail.</summary>
        public string ToAdminNote() => string.IsNullOrEmpty(AdminDetail) ? Text : $"{Text} ({AdminDetail})";
    }
}
