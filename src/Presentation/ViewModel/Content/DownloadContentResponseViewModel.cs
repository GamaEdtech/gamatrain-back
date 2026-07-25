namespace GamaEdtech.Presentation.ViewModel.Content
{
    using System.Text.Json.Serialization;

    using GamaEdtech.Common.Converter;
    using GamaEdtech.Domain.Enumeration;
    using GamaEdtech.Presentation.ViewModel.Game;

    public sealed class DownloadContentResponseViewModel
    {
        public string? Url { get; set; }
        public string? Name { get; set; }
        public bool Spent { get; set; }

        [JsonConverter(typeof(EnumerationConverter<SpendSource, byte>))]
        public SpendSource? PaidBy { get; set; }

        public IEnumerable<UpgradeSuggestionViewModel>? UpgradeSuggestions { get; set; }
    }
}
