namespace GamaEdtech.Domain.Enumeration
{
    using GamaEdtech.Common.Data.Enumeration;
    using GamaEdtech.Common.DataAnnotation;

    /// <summary>
    /// What the admin contact-us AI assistant does: write a reply to the ticket, or polish the admin's own draft.
    /// </summary>
    public sealed class TicketAiReplyMode : Enumeration<TicketAiReplyMode, byte>
    {
        [Display]
        public static readonly TicketAiReplyMode Generate = new(nameof(Generate), 0);

        [Display]
        public static readonly TicketAiReplyMode Polish = new(nameof(Polish), 1);

        public TicketAiReplyMode()
        {
        }

        public TicketAiReplyMode(string name, byte value) : base(name, value)
        {
        }
    }
}
