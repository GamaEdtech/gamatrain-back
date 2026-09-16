namespace GamaEdtech.Data.Dto.Payment
{
    using GamaEdtech.Domain.Enumeration;

    public sealed class PaymentsSummaryDto
    {
        public DateTime Date { get; set; }
        public PaymentStatus Status { get; set; }
        public PaymentKind? Kind { get; set; }
        public decimal Amount { get; set; }
        public long Count { get; set; }
    }
}
