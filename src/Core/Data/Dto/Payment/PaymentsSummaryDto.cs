namespace GamaEdtech.Data.Dto.Payment
{
    using System.Diagnostics.CodeAnalysis;

    using GamaEdtech.Domain.Enumeration;

    public sealed class PaymentsSummaryDto
    {
        public DateTime Date { get; set; }
        public PaymentStatus Status { get; set; }
        public PaymentKind? Kind { get; set; }
        public decimal Amount { get; set; }
        public long Count { get; set; }

        // Calendar day, in timeZone, of a 15-minute UTC bucket (utcDate + hour + quarter * 15 minutes). Every
        // real zone's offset and DST transition falls on a quarter-hour, so a whole bucket maps to one day.
        public static DateTime ToLocalDate(DateTime utcDate, int hour, int quarter, [NotNull] TimeZoneInfo timeZone)
        {
            var utc = new DateTimeOffset(DateTime.SpecifyKind(utcDate.Date, DateTimeKind.Unspecified), TimeSpan.Zero)
                .AddHours(hour).AddMinutes(quarter * 15);

            return TimeZoneInfo.ConvertTime(utc, timeZone).Date;
        }
    }
}
