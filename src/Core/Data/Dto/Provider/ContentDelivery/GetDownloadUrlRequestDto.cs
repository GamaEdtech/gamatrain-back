namespace GamaEdtech.Data.Dto.Provider.ContentDelivery
{
    public sealed class GetDownloadUrlRequestDto
    {
        /// <summary>The downloading user's own credential against the content source (e.g. a gama-api legacy JWT) - the source authorizes/prices per caller, so this can't be a service-level credential.</summary>
        public required string Token { get; set; }
        public required long ExternalContentId { get; set; }
        public required string FileType { get; set; }
        public long? ExtraId { get; set; }
    }
}
