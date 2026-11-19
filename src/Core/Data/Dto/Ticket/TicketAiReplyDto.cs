namespace GamaEdtech.Data.Dto.Ticket
{
    using System.Collections.Generic;

    public sealed class TicketAiReplyDto
    {
        /// <summary>The suggested reply as HTML for the editor.</summary>
        public required string Reply { get; init; }

        /// <summary>What code found on the customer's account, for the admin only (with ids). Empty when nothing was found.</summary>
        public IReadOnlyList<string> Notes { get; init; } = [];
    }
}
