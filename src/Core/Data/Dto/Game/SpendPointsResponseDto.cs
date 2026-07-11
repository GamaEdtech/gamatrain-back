namespace GamaEdtech.Data.Dto.Game
{
    using GamaEdtech.Data.Dto.Subscription;
    using GamaEdtech.Domain.Enumeration;

    public sealed class SpendPointsResponseDto
    {
        public bool Spent { get; set; }
        public SpendSource? PaidBy { get; set; }
        public int? RemainingQuota { get; set; }
        public IEnumerable<UpgradeSuggestionDto>? UpgradeSuggestions { get; set; }
    }
}
