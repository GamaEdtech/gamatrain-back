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

        [Display]
        public static readonly PurchasableContentType ExamAnalysis = new(nameof(ExamAnalysis), 2);

        /// <summary>One file of a past paper; ContentId is the paper id (gama-api's, which <c>PastPapers</c> keeps), Variant
        /// the file: a <c>PastPaperFileKind</c> name (Question/Answer/QuestionWord), or <c>Extra:{id}</c> with the extra
        /// file's gama-api id. Purchases made on gama-api before the move are copied in (<c>SpendSource.GamaApi</c>).</summary>
        [Display]
        public static readonly PurchasableContentType PastPaper = new(nameof(PastPaper), 3);

        public PurchasableContentType()
        {
        }

        public PurchasableContentType(string name, byte value) : base(name, value)
        {
        }
    }
}
