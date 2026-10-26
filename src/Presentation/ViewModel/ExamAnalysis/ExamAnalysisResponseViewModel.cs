namespace GamaEdtech.Presentation.ViewModel.ExamAnalysis
{
    using System.Text.Json.Serialization;

    using GamaEdtech.Common.Converter;
    using GamaEdtech.Domain.Enumeration;
    using GamaEdtech.Presentation.ViewModel.Game;

    public sealed class ExamAnalysisResponseViewModel
    {
        public long? Id { get; set; }
        public long? ExamId { get; set; }
        public string? Result { get; set; }
        public long? Points { get; set; }

        [JsonConverter(typeof(EnumerationConverter<SpendSource, byte>))]
        public SpendSource? PaidBy { get; set; }

        public int? RemainingQuota { get; set; }

        [JsonConverter(typeof(EnumerationConverter<QuotaFailureReason, byte>))]
        public QuotaFailureReason? Reason { get; set; }

        public long? CurrentSubscriptionId { get; set; }
        public long? CurrentPlanId { get; set; }
        public string? CurrentPlanTitle { get; set; }
        public IEnumerable<UpgradeSuggestionViewModel>? UpgradeSuggestions { get; set; }
        public IEnumerable<string>? AvailableBillingIntervals { get; set; }
    }
}
