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

        Task<ResultData<IEnumerable<PlanFeatureGroupDto>>> GetPlanFeaturesAsync(long subscriptionPlanId);
        Task<ResultData<bool>> SetPlanFeaturesAsync([NotNull] SetPlanFeaturesRequestDto requestDto);

        Task<ResultData<ListDataSource<SubscriptionPlanPriceDto>>> GetPlanPricesAsync(ListRequestDto<SubscriptionPlanPrice>? requestDto = null);
        Task<ResultData<long>> ManagePlanPriceAsync([NotNull] ManageSubscriptionPlanPriceRequestDto requestDto);
        Task<ResultData<bool>> RemovePlanPriceAsync([NotNull] ISpecification<SubscriptionPlanPrice> specification);

        Task<ResultData<ListDataSource<GatewayMappingDto>>> GetGatewayMappingsAsync(ListRequestDto<SubscriptionPlanGatewayMapping>? requestDto = null);
        Task<ResultData<long>> ManageGatewayMappingAsync([NotNull] ManageGatewayMappingRequestDto requestDto);
        Task<ResultData<bool>> RemoveGatewayMappingAsync([NotNull] ISpecification<SubscriptionPlanGatewayMapping> specification);

        Task<ResultData<SubscriptionPlanPriceDto>> ResolvePriceAsync([NotNull] ResolvePriceRequestDto requestDto);
        Task<ResultData<PurchaseSubscriptionResponseDto>> PurchaseSubscriptionAsync([NotNull] PurchaseSubscriptionRequestDto requestDto);

        /// <summary>
        /// User-initiated cancel-at-period-end for the caller's own current active subscription. NotValid/
        /// SubscriptionNotRecurring for a one-time/GamaTrain subscription (ExternalSubscriptionId is null) -
        /// it was never going to renew, so there's nothing to cancel. Idempotent if already requested.
        /// Data.EmailNotification is set only when the caller should enqueue the notification email.
        /// </summary>
        Task<ResultData<SubscriptionActionResultDto>> CancelSubscriptionAsync(long userId);

        /// <summary>
        /// Reverses a pending cancel-at-period-end request for the caller's own current active subscription.
        /// NotValid/SubscriptionNotRecurring for a one-time/GamaTrain subscription; idempotent no-op if nothing
        /// was pending. Data.EmailNotification is set only when the caller should enqueue the notification email.
        /// </summary>
        Task<ResultData<SubscriptionActionResultDto>> ResumeSubscriptionAsync(long userId);

        /// <summary>Fire-and-forget Hangfire job target, enqueued by CancelSubscriptionAsync - not meant to be called directly.</summary>
        Task SendSubscriptionCancelledEmailAsync([NotNull] SubscriptionEmailRequestDto requestDto);

        /// <summary>Fire-and-forget Hangfire job target, enqueued by ResumeSubscriptionAsync - not meant to be called directly.</summary>
        Task SendSubscriptionResumedEmailAsync([NotNull] SubscriptionEmailRequestDto requestDto);

        /// <summary>
        /// User-initiated plan switch for the caller's own current active subscription. Stripe-recurring only -
        /// NotValid/SubscriptionNotRecurring for a one-time/GamaTrain subscription. An upgrade (target plan's
        /// price beats the current PricePaid) applies immediately with a prorated invoice; a downgrade is
        /// deferred to the current period's end (no proration/credit). Data.Immediate/EffectiveDate reflect which
        /// happened. Data.EmailNotification is set only when the caller should enqueue the notification email.
        /// </summary>
        Task<ResultData<SubscriptionSwitchResultDto>> SwitchSubscriptionPlanAsync([NotNull] SwitchSubscriptionPlanRequestDto requestDto);

        /// <summary>Fire-and-forget Hangfire job target, enqueued by SwitchSubscriptionPlanAsync - not meant to be called directly.</summary>
        Task SendSubscriptionSwitchedEmailAsync([NotNull] SubscriptionEmailRequestDto requestDto);

        /// <summary>Admin visibility: list any user's subscription(s), filterable/pageable via the specification (e.g. by UserId, Status).</summary>
        Task<ResultData<ListDataSource<AdminUserSubscriptionDto>>> GetUserSubscriptionsAsync(ListRequestDto<UserSubscription>? requestDto = null);

        /// <summary>Admin visibility: a single user subscription's detail.</summary>
        Task<ResultData<AdminUserSubscriptionDto>> GetUserSubscriptionAsync([NotNull] ISpecification<UserSubscription> specification);

        /// <summary>Admin-initiated comped grant for a support case - creates and activates a new subscription immediately, bypassing payment. Returns the new subscription's id.</summary>
        Task<ResultData<long>> GrantSubscriptionAsync([NotNull] GrantUserSubscriptionRequestDto requestDto);

        /// <summary>
        /// Admin-initiated immediate revocation for a support case (unlike the user-facing cancel-at-period-end
        /// flow). If the subscription is gateway-recurring, also terminates it gateway-side first so no further
        /// billing happens, then flips it locally Cancelled right away - access stops immediately, not at period end.
        /// </summary>
        Task<ResultData<bool>> RevokeUserSubscriptionAsync(long userSubscriptionId);

        /// <summary>Admin-initiated support-case extension: pushes the subscription's ExpirationDate forward by the given number of days. Local record only - never re-bills or touches the gateway side.</summary>
        Task<ResultData<bool>> ExtendUserSubscriptionAsync(long userSubscriptionId, int days);
    }
}
