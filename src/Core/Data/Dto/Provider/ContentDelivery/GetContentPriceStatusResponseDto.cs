namespace GamaEdtech.Data.Dto.Provider.ContentDelivery
{
    public sealed class GetContentPriceStatusResponseDto
    {
        /// <summary>The source's own reported price for this specific file, in points. Null means no charge applies at all.</summary>
        public long? Points { get; set; }

        /// <summary>Whether the source already considers this file paid for by this caller - read from a side-effect-free call, safe to trust.</summary>
        public bool Paid { get; set; }
    }
}
