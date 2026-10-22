namespace GamaEdtech.Data.Dto.Dashboard
{
    using GamaEdtech.Domain.Enumeration;

    /// <summary>Business overview for a date range (whole UTC days).</summary>
    public sealed class AdminDashboardOverviewDto
    {
        public DateTimeOffset From { get; set; }

        public DateTimeOffset ToExclusive { get; set; }

        public int SignUps { get; set; }

        public int TeacherSignUps { get; set; }

        public int StudentSignUps { get; set; }

        /// <summary>Paid payments in the range, per kind (new subscription, renewal, plan switch, points top-up).</summary>
        public IList<RevenueByKindDto> Revenue { get; set; } = [];

        public decimal RevenueUsd { get; set; }

        /// <summary>Paid payments in a currency with no USD value (none for subscriptions today), counted but not in RevenueUsd.</summary>
        public int PaymentsWithoutUsd { get; set; }

        public int ActiveSubscriptions { get; set; }

        /// <summary>Active subscriptions set to end at the period end.</summary>
        public int CancellingSubscriptions { get; set; }

        public int NewSubscriptions { get; set; }

        public int ExpiredSubscriptions { get; set; }

        public IList<DailyPointDto> Daily { get; set; } = [];
    }

    public sealed class RevenueByKindDto
    {
        /// <summary>Null for old payments recorded before kinds existed.</summary>
        public PaymentKind? Kind { get; set; }

        public int Count { get; set; }

        public decimal AmountUsd { get; set; }
    }

    public sealed class DailyPointDto
    {
        public DateOnly Date { get; set; }

        public int SignUps { get; set; }

        public int PaidPayments { get; set; }

        public decimal RevenueUsd { get; set; }

        public int InviteJoins { get; set; }

        public int InvitedSignUps { get; set; }
    }
}
