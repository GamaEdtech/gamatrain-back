namespace GamaEdtech.Domain.DataAccess.Mappers.FaqMappers
{
    using GamaEdtech.Domain.DataAccess.Responses.FaqResponses;
    using GamaEdtech.Domain.Entity;

    public static class FaqMapperExtension
    {
        public static IEnumerable<FaqResponse> MapToResult(this IEnumerable<Faq> fAQs) => fAQs.Select(s => new FaqResponse
        {
            Id = s.Id,
            SummaryOfQuestion = s.SummaryOfQuestion,
            Question = s.Question,
            FaqCategoryTree = FaqCategory
            .BuildHierarchyTree(s.FaqAndFaqCategories.Select(s => s.FaqCategory)).MapToResult()
        }).ToList();
    }
}
