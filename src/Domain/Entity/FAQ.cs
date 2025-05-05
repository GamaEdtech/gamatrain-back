namespace GamaEdtech.Domain.Entity
{
    using System.Diagnostics.CodeAnalysis;

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
        private Faq(string summaryOfQuestion, string question)
        {
            SummaryOfQuestion = summaryOfQuestion;
            Question = question;
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
        private readonly List<ClassificationNodeRelationship> classificationNodeRelationships = [];
        public IReadOnlyCollection<ClassificationNodeRelationship> ClassificationNodeRelationships => classificationNodeRelationships;

        private readonly List<Media>? media;
        public IReadOnlyCollection<Media>? Media => media;
        #endregion
        #endregion

        #region Functionalities
        public static Faq Create(string summaryOfQuestion, string question, IReadOnlyList<ClassificationNode> classificationNodes)
        {
            var faq = new Faq(summaryOfQuestion, question);
            faq.AddRelationShip(classificationNodes);
            return faq;
        }
        public void AddMedia(IEnumerable<Media> newMedia) => media?.AddRange(newMedia);

        public void AddRelationShip([NotNull] IEnumerable<ClassificationNode> classificationNodes)
        {
            var notExistsClassificationNodes = classificationNodes
            .Where(c => !classificationNodeRelationships
                        .Any(a => a.NodeRelationshipEntityId == Id &&
                                  a.ClassificationNode.Id == c.Id)).ToList();

            foreach (var classificationNode in notExistsClassificationNodes)
            {
                classificationNodeRelationships.Add(
                ClassificationNodeRelationship.Create(Id, classificationNode.Id,
                NodeRelationEntityType.Faq));
            }

            UpdateLastUpdatedDate();
        }
        #endregion

        #region Domain Events

        #endregion
    }
}
