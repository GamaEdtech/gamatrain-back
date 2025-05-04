namespace GamaEdtech.Domain.Specification.ClassificationNodeSpecs.Criterias
{
    using GamaEdtech.Domain.Entity;

    using System.Linq.Expressions;
#pragma warning disable S101
    public class CheckClassificationNodeIdCriteria(Guid faqCategoryId) : CriteriaSpecification<ClassificationNode>
    {
        public override Expression<Func<ClassificationNode, bool>> ToExpression()
            => current => current.Id == faqCategoryId;
    }
#pragma warning restore S101
}
