namespace GamaEdtech.Domain.Specification.FaqSpecs.Criterias
{
    using GamaEdtech.Domain;
    using GamaEdtech.Domain.Entity;

    using System.Linq.Expressions;

    public class CheckFaqSummaryOfQuestionCriteria(string summaryOfQuestion) : CriteriaSpecification<Faq>
    {
        private readonly string summaryOfQuestion = summaryOfQuestion;

        public override Expression<Func<Faq, bool>> ToExpression() => summaryOfQuestion == null ? (current => true)
                : (current => current.SummaryOfQuestion == summaryOfQuestion);
    }
}
