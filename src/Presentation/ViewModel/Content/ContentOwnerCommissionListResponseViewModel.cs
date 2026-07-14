namespace GamaEdtech.Presentation.ViewModel.Content
{
    using System.Text.Json.Serialization;

    using GamaEdtech.Common.Converter;
    using GamaEdtech.Domain.Enumeration;

    public sealed class ContentOwnerCommissionListResponseViewModel
    {
        public long Id { get; set; }

        public long OwnerUserId { get; set; }

        public string? OwnerFirstName { get; set; }

        public string? OwnerLastName { get; set; }

        public long DownloaderUserId { get; set; }

        [JsonConverter(typeof(EnumerationConverter<CommissionReason, byte>))]
        public CommissionReason Reason { get; set; }
        [JsonConverter(typeof(EnumerationConverter<ContentSource, byte>))]
        public ContentSource Source { get; set; }
        [JsonConverter(typeof(EnumerationConverter<ContentType, byte>))]
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
