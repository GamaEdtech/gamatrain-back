namespace GamaEdtech.Domain.Enumeration
{
    using GamaEdtech.Common.Data.Enumeration;
    using GamaEdtech.Common.DataAnnotation;

    /// <summary>
    /// What a <c>ContentPurchase</c> is for -- anything a user pays for once and then owns (see IContentPurchaseService).
    /// A new kind of paid content (e.g. a premium <c>Post</c>) is one new member here, not a new table.
    /// </summary>
    public sealed class PurchasableContentType : Enumeration<PurchasableContentType, byte>
    {
        /// <summary>Our own generated export of a gama-api exam (<c>exams/export</c>); the purchase's Variant is the
        /// format (Pdf/Word/PowerPoint), its ContentId the gama-api exam id.</summary>
        [Display]
        public static readonly PurchasableContentType ExamExport = new(nameof(ExamExport), 1);

        public PurchasableContentType()
        {
        }

        public PurchasableContentType(string name, byte value) : base(name, value)
        {
        }
    }
}
