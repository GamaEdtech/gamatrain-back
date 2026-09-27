namespace GamaEdtech.Presentation.Api.Controllers
{
    using System.Collections.Generic;
    using System.Linq;

    using GamaEdtech.Data.Dto.Subscription;
    using GamaEdtech.Presentation.ViewModel.Game;
    using GamaEdtech.Presentation.ViewModel.Subscription;

    /// <summary>Maps a refused charge's upgrade suggestions to their view models -- shared by <c>downloads</c> and
    /// <c>exams/export</c>, which both return a <c>DownloadContentResponseViewModel</c> when a charge is refused.</summary>
    internal static class UpgradeSuggestionMapper
    {
        public static IEnumerable<UpgradeSuggestionViewModel>? Map(IEnumerable<UpgradeSuggestionDto>? suggestions) =>
            suggestions?.Select(t => new UpgradeSuggestionViewModel
            {
                Id = t.Id,
                Title = t.Title,
                Highlight = t.Highlight,
                Prices = t.Prices?.Select(p => new UpgradeSuggestionPriceViewModel
                {
                    BillingInterval = p.BillingInterval,
                    Currency = p.Currency,
                    CurrencySymbol = p.CurrencySymbol,
                    Price = p.Price,
                    MonthlyEquivalentPrice = p.MonthlyEquivalentPrice,
                    DiscountPercent = p.DiscountPercent,
                    Limit = p.Limit,
                    IsCurrent = p.IsCurrent,
                    CanUpgrade = p.CanUpgrade,
                    Description = p.Description,
                    PooledFeatureCodes = p.PooledFeatureCodes,
                    FeatureGroups = p.FeatureGroups?.Select(g => new UpgradeSuggestionFeatureGroupViewModel
                    {
                        Features = g.Features.Select(f => new PlanFeatureViewModel
                        {
                            FeatureId = f.FeatureId,
                            FeatureCode = f.FeatureCode,
                            FeatureName = f.FeatureName,
                        }),
                        Limit = g.Limit,
                        Description = g.Description,
                    }),
                }),
            });
    }
}
