namespace GamaEdtech.Domain.Specification.Payment
{
    using System;
    using System.Linq.Expressions;

    using GamaEdtech.Common.DataAccess.Specification;
    using GamaEdtech.Domain.Entity;

    public sealed class CreationDateBetweenSpecification(DateOnly? start, DateOnly? end, TimeZoneInfo? timeZone = null) : SpecificationBase<Payment>
    {
        public override Expression<Func<Payment, bool>> Expression()
        {
            // start/end are calendar days of timeZone (UTC when null), so each bound carries that zone's own
            // offset on that day rather than a fixed one - a range spanning a DST change stays exact.
            var zone = timeZone ?? TimeZoneInfo.Utc;
            DateTimeOffset? startDate = start.HasValue ? ToOffset(start.Value.ToDateTime(new TimeOnly(0, 0)), zone) : null;
            DateTimeOffset? endDate = end.HasValue ? ToOffset(end.Value.ToDateTime(new TimeOnly(23, 59, 59, 999, 999)), zone) : null;

            return t => (startDate == null || t.CreationDate >= startDate) && (endDate == null || t.CreationDate <= endDate);
        }

        private static DateTimeOffset ToOffset(DateTime local, TimeZoneInfo zone) => new(local, zone.GetUtcOffset(local));
    }
}
