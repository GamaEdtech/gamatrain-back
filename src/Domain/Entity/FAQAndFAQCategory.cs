namespace GamaEdtech.Domain.Entity
{
    public class FaqAndFaqCategory
    {
        #region Ctors
        private FaqAndFaqCategory()
        {
        }
        private FaqAndFaqCategory(Guid faqId, Guid faqCategoryId)
        {
            FaqId = faqId;
            FaqCategoryId = faqCategoryId;
            Faq = null;
            FaqCategory = null;
        }
        #endregion

        #region Propeties
        public Guid FaqId { get; private set; }
        public Guid FaqCategoryId { get; private set; }
        #endregion

        #region Relations
        #region ForeignKey
        public virtual Faq? Faq { get; private set; }
        public virtual FaqCategory? FaqCategory { get; private set; }
        #endregion
        #region ICollectiona

        #endregion
        #endregion

        #region Functionalities
        public static FaqAndFaqCategory Create(Guid faqId, Guid faqCategoryId) =>
            new(faqId, faqCategoryId);
        #endregion

        #region Domain Events

        #endregion
    }
}
