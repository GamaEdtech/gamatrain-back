namespace GamaEdtech.Domain.Specification.FaqSpecs
{
    using Ardalis.Specification;

    using GamaEdtech.Domain.DataAccess.Requests.FaqRequests;
    using GamaEdtech.Domain.Entity;
    using GamaEdtech.Domain.Specification.FaqSpecs.Criterias;

    public class GetFaqWithDynamicFilterSpecification : BaseSpecification<Faq>
    {
        private readonly GetFaqWithDynamicFilterRequest dynamicFilterReq;

        public GetFaqWithDynamicFilterSpecification(GetFaqWithDynamicFilterRequest dynamicFilterReq, params FaqRelations[] fAQRelations)
        {
            this.dynamicFilterReq = dynamicFilterReq;
            _ = Query.Where(Criteria().ToExpression());
            OrderBy();
            fAQRelations.ToList().ForEach(GetFaqRelations);
        }
        protected override CriteriaSpecification<Faq> Criteria() => new CheckFaqCategoriesOfFaqCriteria(dynamicFilterReq.FaqCategoriesTitle)
                .And(new CheckFaqDateTimeCriteria(dynamicFilterReq.FromDate, dynamicFilterReq.ToDate));

        private void GetFaqRelations(FaqRelations fAQRelations)
        {
            switch (fAQRelations)
            {
                case FaqRelations.FaqCategory:
                    _ = Query.Include(inc => inc.FaqAndFaqCategories)
                        .ThenInclude(thin => thin.FaqCategory);
                    break;
                case FaqRelations.Media:
                    _ = Query.Include(inc => inc.Media);
                    break;
                default:
                    break;
            }
        }

        private void OrderBy()
        {
            switch (dynamicFilterReq.CustomOrderBy)
            {
                case CustomOrderBy.OrderByCreateDateAscending:
                    _ = Query.OrderBy(c => c.CreateDate);
                    break;
                case CustomOrderBy.OrderByCreateDateDescending:
                    _ = Query.OrderByDescending(c => c.CreateDate);
                    break;
                default:
                    break;
            }
        }
    }
    public enum FaqRelations
    {
        FaqCategory,
        Media
    }
}
