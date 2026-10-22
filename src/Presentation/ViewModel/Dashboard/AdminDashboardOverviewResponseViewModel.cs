namespace GamaEdtech.Presentation.ViewModel.Dashboard
{
    using System.Text.Json.Serialization;

    using GamaEdtech.Common.Converter;
    using GamaEdtech.Domain.Enumeration;

    public sealed class AdminDashboardOverviewResponseViewModel
    {
        public DateTimeOffset From { get; set; }

        public DateTimeOffset ToExclusive { get; set; }

        public int SignUps { get; set; }

        public int TeacherSignUps { get; set; }

        public int StudentSignUps { get; set; }

        public IEnumerable<RevenueByKindResponseViewModel> Revenue { get; set; } = [];

        public decimal RevenueUsd { get; set; }

        public int PaymentsWithoutUsd { get; set; }

        public int ActiveSubscriptions { get; set; }

        public int CancellingSubscriptions { get; set; }

        public int NewSubscriptions { get; set; }

        public int ExpiredSubscriptions { get; set; }

        public IEnumerable<DailyPointResponseViewModel> Daily { get; set; } = [];
    }

    public sealed class RevenueByKindResponseViewModel
    {
        /// <summary>Null for old payments recorded before kinds existed.</summary>
        [JsonConverter(typeof(EnumerationConverter<PaymentKind, byte>))]
        public PaymentKind? Kind { get; set; }

        public int Count { get; set; }

        public decimal AmountUsd { get; set; }
    }

    public sealed class DailyPointResponseViewModel
    {
        public DateOnly Date { get; set; }

        public int SignUps { get; set; }

        public int PaidPayments { get; set; }

        public decimal RevenueUsd { get; set; }

        public int InviteJoins { get; set; }

        public int InvitedSignUps { get; set; }
    }
}
