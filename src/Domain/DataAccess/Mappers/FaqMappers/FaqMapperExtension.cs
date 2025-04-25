namespace GamaEdtech.Domain.DataAccess.Mappers.FaqMappers
{
    using GamaEdtech.Common.Core.Extensions;
    using GamaEdtech.Domain.DataAccess.Responses.FaqResponses;
    using GamaEdtech.Domain.Entity;

    public static class FaqMapperExtension
    {
        public static IEnumerable<FaqResponse> MapToResult(this IEnumerable<Faq> fAQs, CustomDateFormat customDateFormat) => fAQs.Select(s => new FaqResponse
        {
            Id = s.Id,
            SummaryOfQuestion = s.SummaryOfQuestion,
            Question = s.Question,
            CreateDate = s.CreateDate.ConvertToCustomDate(customDateFormat),
            LastUpdatedDate = s.LastUpdatedDate.ConvertToCustomDate(customDateFormat),
            FaqCategoryTree = FaqCategory
            .BuildHierarchyTree(s.FaqAndFaqCategories.Select(s => s.FaqCategory)).MapToResult(customDateFormat)
        }).ToList();
    }
}
