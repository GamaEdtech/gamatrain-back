namespace GamaEdtech.Domain.Enumeration
{
    using GamaEdtech.Common.Data.Enumeration;
    using GamaEdtech.Common.DataAnnotation;

    /// <summary>
    /// Where a support conversation stands, kept apart from the read flag (Ticket.IsReadByAdmin). Open and Answered
    /// follow the replies: a customer reply (or a new ticket from a customer) sets Open, an admin reply (or a ticket an
    /// admin starts) sets Answered. Closed and NotImportant are set by an admin; a later customer reply reopens them.
    /// </summary>
    public sealed class TicketStatus : Enumeration<TicketStatus, byte>
    {
        [Display]
        public static readonly TicketStatus Open = new(nameof(Open), 0);

        [Display]
        public static readonly TicketStatus Answered = new(nameof(Answered), 1);

        [Display]
        public static readonly TicketStatus Closed = new(nameof(Closed), 2);

        [Display]
        public static readonly TicketStatus NotImportant = new(nameof(NotImportant), 3);

        public TicketStatus()
        {
        }

        public TicketStatus(string name, byte value) : base(name, value)
        {
        }
    }
}
