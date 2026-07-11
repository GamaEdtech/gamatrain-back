namespace GamaEdtech.Presentation.ViewModel.Game
{
    using System.Text.Json.Serialization;

    using GamaEdtech.Common.Converter;
    using GamaEdtech.Domain.Enumeration;

    public sealed class SpendPointsResponseViewModel
    {
        public bool Spent { get; set; }

        [JsonConverter(typeof(EnumerationConverter<SpendSource, byte>))]
        public SpendSource? PaidBy { get; set; }

        public int? RemainingQuota { get; set; }

        public IEnumerable<UpgradeSuggestionViewModel>? UpgradeSuggestions { get; set; }
    }
}
