namespace GamaEdtech.Data.Dto.Provider.Payout
{
    public sealed class PayoutTransferRequestDto
    {
        public long PayoutId { get; set; }

        public string? ExternalAccountId { get; set; }

        public decimal AmountUsd { get; set; }

        /// <summary>Must be the same for every attempt of one payout, so a retry can never send the money twice.</summary>
        public string? IdempotencyKey { get; set; }
    }
}
