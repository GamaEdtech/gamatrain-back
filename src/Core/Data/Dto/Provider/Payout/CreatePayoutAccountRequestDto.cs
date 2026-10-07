namespace GamaEdtech.Data.Dto.Provider.Payout
{
    public sealed class CreatePayoutAccountRequestDto
    {
        public long UserId { get; set; }

        public string? Email { get; set; }

        /// <summary>ISO 3166-1 alpha-2, upper case.</summary>
        public string? Country { get; set; }
    }
}
