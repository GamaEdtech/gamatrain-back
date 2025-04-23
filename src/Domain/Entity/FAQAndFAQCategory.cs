namespace GamaEdtech.Domain.Entity
{
    using GamaEdtech.Domain;

    public class FaqAndFaqCategory : BaseEntity
    {
        #region Ctors
        private FaqAndFaqCategory()
        {
        }
        private FaqAndFaqCategory(Guid faqId, Guid faqCategoryId)
        {
            FaqId = faqId;
            FaqCategoryId = faqCategoryId;
        }
        #endregion

        #region Propeties
        public Guid FaqId { get; private set; }
        public Guid FaqCategoryId { get; private set; }
        #endregion

        #region Relations
        #region ForeignKey
#pragma warning disable S1144 // Disable unused private setter warning for Faq and FaqCategory
        public virtual Faq Faq { get; private set; }
        public virtual FaqCategory FaqCategory { get; private set; }
#pragma warning restore S1144

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
