namespace GamaEdtech.Domain.DataAccess.Mappers.FaqMappers
{
    using System.Diagnostics.CodeAnalysis;

    using GamaEdtech.Domain.DataAccess.Responses.FaqResponses;
    using GamaEdtech.Domain.Entity;
    using GamaEdtech.Domain.DataAccess.Mappers.MediaMappers;
    public static class FaqMapperExtension
    {
        public static FaqResponse MapToResult([NotNull] this Faq faq) => new()
        {
            Id = faq.Id,
            SummaryOfQuestion = faq.SummaryOfQuestion,
            Question = faq.Question,
            CreateDate = faq.CreateDate,
            LastUpdatedDate = faq.LastUpdatedDate
        };

        public static IEnumerable<FaqResponse> MapToResult(this IEnumerable<Faq> faqs) => faqs.Select(s => new FaqResponse
        {
            Id = s.Id,
            SummaryOfQuestion = s.SummaryOfQuestion,
            Question = s.Question,
            CreateDate = s.CreateDate,
            LastUpdatedDate = s.LastUpdatedDate,
            ClassificationNodes = s.ClassificationNodeRelationships.Select(s => s.ClassificationNode.MapToResult()),
            FileResponses = s.Media?.MapToResult()
        }).ToList();
    }
}
