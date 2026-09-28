namespace GamaEdtech.Data.Dto.ContentPurchase
{
    using GamaEdtech.Domain.Enumeration;

    /// <summary>One piece of content a user is buying (or checking whether they own) -- see IContentPurchaseService.</summary>
    public sealed class ContentPurchaseRequestDto
    {
        public required long UserId { get; set; }

        public required PurchasableContentType ContentType { get; set; }

        public required long ContentId { get; set; }

        /// <summary>Which form of the content (e.g. an exam export's format); null/empty when it has only one.</summary>
        public string? Variant { get; set; }

        /// <summary>The price, computed by the caller from its own content's pricing rule; 0 means free (nothing charged,
        /// nothing recorded).</summary>
        public long Points { get; set; }

        /// <summary>Which spend category -- and so which subscription feature's quota -- pays for it
        /// (GameService.SpendPointsAsync maps it: e.g. <see cref="ContentType.Exam"/> to the ExamDownload feature).</summary>
        public required ContentType SpendContentType { get; set; }
    }
}
