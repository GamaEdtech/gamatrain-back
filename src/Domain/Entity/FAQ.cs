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
            classificationNodeRelationships = [];
            media = [];
        }
        private Faq(string summaryOfQuestion, string question, IReadOnlyList<ClassificationNode> classificationNodes)
        {
            SummaryOfQuestion = summaryOfQuestion;
            Question = question;
            classificationNodeRelationships = classificationNodes.Select(s => ClassificationNodeRelationship.Create(Id, s.Id, NodeRelationEntityType.Faq)).ToList();
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
        private readonly List<ClassificationNodeRelationship> classificationNodeRelationships;
        public IReadOnlyCollection<ClassificationNodeRelationship> ClassificationNodeRelationships => classificationNodeRelationships;

        private readonly List<Media>? media;
        public IReadOnlyCollection<Media>? Media => media;
        #endregion
        #endregion

        #region Functionalities
        public static Faq Create(string summaryOfQuestion, string question, IReadOnlyList<ClassificationNode> faqCategories)
            => new(summaryOfQuestion, question, faqCategories);
        public void AddMedia(IEnumerable<Media> newMedia) => media?.AddRange(newMedia);
        #endregion

        #region Domain Events

        #endregion
    }
}
