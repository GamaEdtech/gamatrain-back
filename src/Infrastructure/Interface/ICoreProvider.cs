namespace GamaEdtech.Infrastructure.Interface
{
    using System.Diagnostics.CodeAnalysis;
    using System.Text.Json.Nodes;

    using GamaEdtech.Common.Data;
    using GamaEdtech.Common.DataAnnotation;
    using GamaEdtech.Data.Dto.Identity;

    /// <summary>
    /// What is left of the gama-api proxy that no migrating domain has claimed yet: the cross-type search and the
    /// sign-in bridge. A domain's calls live on its own source interface (<see cref="ITaxonomySource"/>,
    /// <see cref="IPastPaperSource"/>, <see cref="IExamSource"/>) and are reached through <c>IMigrationRouter</c>.
    /// </summary>
    [Injectable]
    public interface ICoreProvider
    {
        /// <summary>gama-api's public search (<c>search</c>, or <c>search/typesstats</c> with <paramref name="typesStats"/>), anonymously,
        /// with <paramref name="query"/> as its query string, and its <c>data</c> as gama-api sends it.</summary>
        Task<ResultData<JsonObject>> SearchAsync([NotNull] string query, bool typesStats);

        /// <summary>gama-api's <c>recommendations/related</c> (every content type related to an item), anonymously, its
        /// <c>data</c> as gama-api sends it.</summary>
        Task<ResultData<JsonObject>> GetRelatedAsync([NotNull] string query);

        /// <summary>
        /// Temporary legacy-auth-bridge methods, proxying gama-api's /users/* endpoints. Remove alongside LegacyAuthBridgeController once the frontend migrates off the old backend.
        /// </summary>
        Task<ResultData<LegacyAuthResponseDto>> LegacyLoginAsync([NotNull] LegacyLoginRequestDto requestDto);
        Task<ResultData<LegacyAuthResponseDto>> LegacyGoogleAuthAsync([NotNull] LegacyGoogleAuthRequestDto requestDto);
        Task<ResultData<LegacyMessageResponseDto>> LegacyRegisterAsync([NotNull] LegacyOtpFlowRequestDto requestDto);
        Task<ResultData<LegacyMessageResponseDto>> LegacyRecoveryAsync([NotNull] LegacyOtpFlowRequestDto requestDto);
        Task<ResultData<Void>> LegacyLogoutAsync([NotNull] LegacyLogoutRequestDto requestDto);

        /// <summary>
        /// Proxies gama-api's GET /teachers/dashboard or GET /students/dashboard (picked by requestDto.Group,
        /// mirroring gamatrain-front's own selection - see DashboardRequestDto.Group). Not part of the
        /// legacy-auth-bridge (this call doesn't need an [AllowAnonymous] action; the caller is always already
        /// authenticated against this backend), but shares the same "forward the raw legacy JWT" mechanism.
        /// </summary>
        Task<ResultData<LegacyDashboardDataDto>> GetDashboardAsync([NotNull] DashboardRequestDto requestDto);
    }
}
