namespace GamaEdtech.Application.Interface
{
    using System.Diagnostics.CodeAnalysis;

    using GamaEdtech.Common.Data;
    using GamaEdtech.Common.DataAccess.Specification;
    using GamaEdtech.Common.DataAnnotation;
    using GamaEdtech.Data.Dto.Subscription;
    using GamaEdtech.Domain.Entity;

    [Injectable]
    public interface ISubscriptionService
    {
        Task<ResultData<ListDataSource<SubscriptionPlanDto>>> GetSubscriptionPlansAsync(ListRequestDto<SubscriptionPlan>? requestDto = null);
        Task<ResultData<SubscriptionPlanDto>> GetSubscriptionPlanAsync([NotNull] ISpecification<SubscriptionPlan> specification);
        Task<ResultData<long>> ManageSubscriptionPlanAsync([NotNull] ManageSubscriptionPlanRequestDto requestDto);
        Task<ResultData<bool>> RemoveSubscriptionPlanAsync([NotNull] ISpecification<SubscriptionPlan> specification);

        Task<ResultData<ListDataSource<FeatureDto>>> GetFeaturesAsync(ListRequestDto<Feature>? requestDto = null);
        Task<ResultData<int>> ManageFeatureAsync([NotNull] ManageFeatureRequestDto requestDto);
        Task<ResultData<bool>> RemoveFeatureAsync([NotNull] ISpecification<Feature> specification);
        IReadOnlyList<string> GetFeatureCodes();

        Task<ResultData<IEnumerable<PlanFeatureDto>>> GetPlanFeaturesAsync(long subscriptionPlanId);
        Task<ResultData<bool>> SetPlanFeaturesAsync([NotNull] SetPlanFeaturesRequestDto requestDto);

        Task<ResultData<ListDataSource<SubscriptionPlanPriceDto>>> GetPlanPricesAsync(ListRequestDto<SubscriptionPlanPrice>? requestDto = null);
        Task<ResultData<long>> ManagePlanPriceAsync([NotNull] ManageSubscriptionPlanPriceRequestDto requestDto);
        Task<ResultData<bool>> RemovePlanPriceAsync([NotNull] ISpecification<SubscriptionPlanPrice> specification);

        Task<ResultData<ListDataSource<GatewayMappingDto>>> GetGatewayMappingsAsync(ListRequestDto<SubscriptionPlanGatewayMapping>? requestDto = null);
        Task<ResultData<long>> ManageGatewayMappingAsync([NotNull] ManageGatewayMappingRequestDto requestDto);
        Task<ResultData<bool>> RemoveGatewayMappingAsync([NotNull] ISpecification<SubscriptionPlanGatewayMapping> specification);

        Task<ResultData<SubscriptionPlanPriceDto>> ResolvePriceAsync([NotNull] ResolvePriceRequestDto requestDto);
        Task<ResultData<PurchaseSubscriptionResponseDto>> PurchaseSubscriptionAsync([NotNull] PurchaseSubscriptionRequestDto requestDto);
    }
}
