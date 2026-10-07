namespace GamaEdtech.Data.Dto.Content
{
    public sealed class RequestCommissionPayoutRequestDto
    {
        public long UserId { get; set; }

        /// <summary>Null means the whole available balance.</summary>
        public decimal? AmountUsd { get; set; }

        public string? Destination { get; set; }
    }
}
