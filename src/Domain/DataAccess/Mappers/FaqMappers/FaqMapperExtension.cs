namespace GamaEdtech.Domain.DataAccess.Mappers.FaqMappers
{
    using System.Diagnostics.CodeAnalysis;

    using GamaEdtech.Common.Core.Extensions;
    using GamaEdtech.Domain.DataAccess.Responses.FaqResponses;
    using GamaEdtech.Domain.Entity;
    using GamaEdtech.Domain.DataAccess.Mappers.MediaMappers;
    public static class FaqMapperExtension
    {
        public static FaqResponse MapToResult([NotNull] this Faq faq, CustomDateFormat customDateFormat) => new()
        {
            Id = faq.Id,
            SummaryOfQuestion = faq.SummaryOfQuestion,
            Question = faq.Question,
            CreateDate = faq.CreateDate.ConvertToCustomDate(customDateFormat),
            LastUpdatedDate = faq.LastUpdatedDate.ConvertToCustomDate(customDateFormat)
        };

        public static IEnumerable<FaqResponse> MapToResult(this IEnumerable<Faq> faqs, CustomDateFormat customDateFormat) => faqs.Select(s => new FaqResponse
        {
            Id = s.Id,
            SummaryOfQuestion = s.SummaryOfQuestion,
            Question = s.Question,
            CreateDate = s.CreateDate.ConvertToCustomDate(customDateFormat),
            LastUpdatedDate = s.LastUpdatedDate.ConvertToCustomDate(customDateFormat),
            FaqCategoryTree = ClassificationNode
            .BuildHierarchyTree(s.ClassificationNodeRelationships.Select(s => s.ClassificationNode)).MapToResult(customDateFormat),
            FileResponses = s.Media?.MapToResult(customDateFormat)
        }).ToList();
    }
}
