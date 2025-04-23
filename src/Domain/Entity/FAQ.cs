namespace GamaEdtech.Domain.Entity
{
    using GamaEdtech.Domain;

    public class Faq : AggregateRoot
    {
        #region Ctors
        private Faq()
        {
            SummaryOfQuestion = string.Empty;
            Question = string.Empty;
            fAQAndFAQCategories = [];
            media = [];
        }
        private Faq(string summaryOfQuestion, string question, IReadOnlyList<FaqCategory> fAQCategories)
        {
            SummaryOfQuestion = summaryOfQuestion;
            Question = question;
            fAQAndFAQCategories = fAQCategories.Select(s => FaqAndFaqCategory.Create(Id, s.Id)).ToList();
            media = [];
        }
        #endregion

        #region Propeties
        public string SummaryOfQuestion { get; private set; }
        public string Question { get; private set; }
        #endregion

        #region Relation
        #region ForeignKey
        #endregion

        #region ICollaction
        private readonly List<FaqAndFaqCategory> fAQAndFAQCategories;
        public IReadOnlyCollection<FaqAndFaqCategory> FAQAndFAQCategories => fAQAndFAQCategories;

        private readonly List<Media>? media;
        public IReadOnlyCollection<Media>? Media => media;
        #endregion
        #endregion

        #region Functionalities
        public static Faq Create(string summaryOfQuestion, string question, IReadOnlyList<FaqCategory> faqCategories)
            => new(summaryOfQuestion, question, faqCategories);
        public void AddMedia(IEnumerable<Media> newMedia) => media?.AddRange(newMedia);
        #endregion

        #region Domain Events

        #endregion
    }
}
