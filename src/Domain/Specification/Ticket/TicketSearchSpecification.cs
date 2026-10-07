namespace GamaEdtech.Domain.Specification.Ticket
{
    using System.Diagnostics.CodeAnalysis;
    using System.Globalization;
    using System.Linq.Expressions;

    using GamaEdtech.Common.DataAccess.Specification;
    using GamaEdtech.Domain.Entity;

    // One admin search box: subject, sender name or email contains the term. A term that reads as a ticket number
    // ("123", "#123", "Ticket-123", "[Ticket-123]" as quoted from the email subject) also matches that ticket's id.
    public sealed class TicketSearchSpecification(string term) : SpecificationBase<Ticket>
    {
        private const string TicketPrefix = "Ticket-";

        public override Expression<Func<Ticket, bool>> Expression()
        {
            var text = term.Trim();
            var id = ParseTicketId(text);
            return id.HasValue
                ? (t) => t.Id == id.Value || t.Subject!.Contains(text) || t.FullName!.Contains(text) || t.Email!.Contains(text)
                : (t) => t.Subject!.Contains(text) || t.FullName!.Contains(text) || t.Email!.Contains(text);
        }

        public static long? ParseTicketId([NotNull] string term)
        {
            var text = term.Trim().TrimStart('[').TrimEnd(']').TrimStart('#');
            if (text.StartsWith(TicketPrefix, StringComparison.OrdinalIgnoreCase))
            {
                text = text[TicketPrefix.Length..];
            }

            return long.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var id) && id > 0 ? id : null;
        }
    }
}
