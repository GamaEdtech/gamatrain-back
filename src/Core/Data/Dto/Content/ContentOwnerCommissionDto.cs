namespace GamaEdtech.Data.Dto.Content
{
    using GamaEdtech.Domain.Enumeration;

    public sealed class ContentOwnerCommissionDto
    {
        public long Id { get; set; }
        public long OwnerUserId { get; set; }
        public string? OwnerFirstName { get; set; }
        public string? OwnerLastName { get; set; }
        public long DownloaderUserId { get; set; }
        public CommissionReason Reason { get; set; }
        public ContentSource Source { get; set; }
        public ContentType ContentType { get; set; }
        public long ExternalContentId { get; set; }
        public string? ExternalFileType { get; set; }
        public long? ExternalExtraId { get; set; }
        public long Points { get; set; }
        public decimal CommissionPercent { get; set; }
        public decimal AmountUsd { get; set; }
        public DateTimeOffset CreationDate { get; set; }
    }
}
