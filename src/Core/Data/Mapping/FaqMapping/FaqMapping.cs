namespace GamaEdtech.Data.Mapping.FaqMapping
{
    using System.Diagnostics.CodeAnalysis;

    using GamaEdtech.Data.Dto.FaqManager;
    using GamaEdtech.Domain.DataAccess.Requests.FaqRequests;

    public static class FaqMapping
    {
        public static GetFaqWithDynamicFilterRequest MapToRequest([NotNull] this GetFaqWithDynamicFilterDto getFAQWithDynamicFilterDTO) => new()
        {
            FaqCategoriesTitle = getFAQWithDynamicFilterDTO.FaqCategoriesTitle,
            CustomOrderBy = getFAQWithDynamicFilterDTO.CustomOrderBy,
            FromDate = getFAQWithDynamicFilterDTO.FromDate,
            ToDate = getFAQWithDynamicFilterDTO.ToDate,
            SummaryOfQuestion = getFAQWithDynamicFilterDTO.SummaryOfQuestion
        };
    }
}
