namespace GamaEdtech.Application.Interface
{
    using System.Diagnostics.CodeAnalysis;

    using GamaEdtech.Common.Data;
    using GamaEdtech.Common.DataAccess.Specification;
    using GamaEdtech.Common.DataAnnotation;
    using GamaEdtech.Data.Dto.Identity;
    using GamaEdtech.Domain.Entity.Identity;
    using GamaEdtech.Domain.Enumeration;

    using Microsoft.AspNetCore.Authentication.Cookies;

    using NetTopologySuite.Geometries;

    [Injectable]
    public interface IIdentityService
    {
        Task<ResultData<ListDataSource<ApplicationUserDto>>> GetUsersAsync(ListRequestDto<ApplicationUser>? requestDto = null);
        Task<ResultData<IEnumerable<ApplicationRoleDto>>> GetRolesAsync(ISpecification<ApplicationRoleDto>? specification = null);
        Task<ResultData<ApplicationUserDto>> GetUserAsync([NotNull] ISpecification<ApplicationUser> specification);
        Task<ResultData<List<long>>> GetUserIdsAsync([NotNull] ISpecification<ApplicationUser> specification);
        Task<ResultData<List<string?>>> GetUsersEmailAsync([NotNull] ISpecification<ApplicationUser> specification);
        Task<ResultData<(long Id, string? FullName)?>> GetUserFullNameAsync([NotNull] ISpecification<ApplicationUser> specification);
        Task<ResultData<Point?>> GetUserCoordinateAsync([NotNull] ISpecification<ApplicationUser> specification);
        Task<ResultData<ICollection<string>>> GetUserRolesAsync([NotNull] long userId);
        Task<ResultData<bool>> UserIsInRoleAsync([NotNull] long userId, [NotNull] string role);
        Task<ResultData<AuthenticationResponseDto>> AuthenticateAsync([NotNull] AuthenticationRequestDto requestDto);
        Task<ResultData<bool>> RegisterAsync([NotNull] RegistrationRequestDto requestDto);
        Task SendRegistrationEmailAsync([NotNull] RegistrationEmailRequestDto requestDto);
        Task<ResultData<SignInResponseDto>> SignInAsync([NotNull] SignInRequestDto requestDto);
        Task<ResultData<Void>> SignOutAsync();
        Task<ResultData<bool>> CreateUserAsync([NotNull] CreateUserRequestDto requestDto);
        Task<ResultData<bool>> UpdateUserAsync([NotNull] UpdateUserRequestDto requestDto);
        Task<ResultData<bool>> ToggleUserAsync([NotNull] ISpecification<ApplicationUser> specification);
        Task<ResultData<bool>> RemoveUserAsync([NotNull] ISpecification<ApplicationUser> specification);
        Task<ResultData<string?>> GetUserTokenAsync([NotNull] GetUserTokenRequestDto requestDto);
        Task<ResultData<GenerateUserTokenResponseDto>> GenerateUserTokenAsync([NotNull] GenerateUserTokenRequestDto requestDto);
        Task<ResultData<bool>> RemoveUserTokenAsync([NotNull] RemoveUserTokenRequestDto requestDto);
        Task<ResultData<bool>> ChangePasswordAsync([NotNull] ChangePasswordRequestDto requestDto);
        Task<ResultData<bool>> ResetPasswordAsync([NotNull] ResetPasswordRequestDto requestDto);
        Task ValidatePrincipalAsync([NotNull] CookieValidatePrincipalContext context);
        Task<ResultData<UserPermissionsResponseDto>> GetUserPermissionsAsync([NotNull] UserPermissionsRequestDto requestDto);
        Task<ResultData<Void>> UpdateUserPermissionsAsync([NotNull] UpdateUserPermissionsRequestDto requestDto);
        Task<ResultData<ProfileSettingsDto>> GetProfileSettingsAsync([NotNull] ISpecification<ApplicationUser> specification);
        Task<ResultData<bool>> ManageProfileSettingsAsync([NotNull] ManageProfileSettingsRequestDto requestDto);
        Task<ResultData<string>> GenerateReferralUserAsync();
        Task<ResultData<bool>> HasClaimAsync(long userId, SystemClaim claims);
        Task<ResultData<List<UserPointsDto>>> GetTop100UsersAsync(Top100UsersRequestDto? requestDto);
        Task<ResultData<Void>> AddLoginHistoryAsync([NotNull] LoginHistoryRequestDto requestDto);
        Task<ResultData<PublicProfileResponseDto>> GetPublicProfileAsync([NotNull] PublicProfileRequestDto requestDto);
        Task<ResultData<bool>> ManageAvatarAsync([NotNull] ManageAvatarRequestDto requestDto);
        Task<ResultData<bool>> InitializeDeletingAccountAsync([NotNull] ISpecification<ApplicationUser> specification);
        Task<ResultData<bool>> RecoverAccountAsync([NotNull] ISpecification<ApplicationUser> specification);
        Task<ResultData<bool>> UpdateOrphanUsersAsync();
        Task<ResultData<string>> ValidateHandleAsync([NotNull] ValidateHandleRequestDto requestDto);
        Task<ResultData<ListDataSource<PublicProfileDto>>> GetProfilesListAsync(ListRequestDto<ApplicationUser>? requestDto = null);

        /// <summary>
        /// Resolves a "target user" API parameter to a local ApplicationUser.Id. When idType is Id, returns id
        /// unchanged; when CoreId, looks up the local user linked to that legacy CoreId and returns NotFound if
        /// none exists yet (never auto-creates one).
        /// </summary>
        Task<ResultData<long>> ResolveUserIdAsync(long id, [NotNull] IdentifierType idType);

        /// <summary>
        /// Bulk form of <see cref="ResolveUserIdAsync"/>. Returns a map of the originally requested id to the
        /// resolved local ApplicationUser.Id; ids that don't resolve (e.g. an unlinked CoreId) are simply omitted,
        /// never fail the whole batch.
        /// </summary>
        Task<ResultData<Dictionary<long, long>>> ResolveUserIdsAsync([NotNull] IEnumerable<long> ids, [NotNull] IdentifierType idType);

        /// <summary>
        /// Temporary legacy-auth-bridge methods proxying gama-api. Remove alongside LegacyAuthBridgeController once the frontend migrates off the old backend.
        /// </summary>
        Task<ResultData<LegacyBridgeTokenResponseDto>> LegacyLoginAsync([NotNull] LegacyLoginRequestDto requestDto);
        Task<ResultData<LegacyBridgeTokenResponseDto>> LegacyGoogleAuthAsync([NotNull] LegacyGoogleAuthRequestDto requestDto);
        Task<ResultData<LegacyMessageResponseDto>> LegacyRegisterAsync([NotNull] LegacyOtpFlowRequestDto requestDto);
        Task<ResultData<LegacyMessageResponseDto>> LegacyRecoveryAsync([NotNull] LegacyOtpFlowRequestDto requestDto);

        /// <summary>
        /// Ends a gama-api-issued session by proxying to gama-api's own GET /users/logout (bearerAuth) with the
        /// caller's raw legacy JWT. Pure passthrough, same as LegacyRegisterAsync/LegacyRecoveryAsync - no local
        /// state to update, since this backend never stores the legacy token in the first place.
        /// </summary>
        Task<ResultData<Void>> LegacyLogoutAsync([NotNull] string token);

        /// <summary>
        /// Proxies gama-api's POST /users/group to set the caller's own Group (5 = Teacher, 6 = Student - see
        /// ApplicationUser.Group's doc comment), then - on success - updates the local ApplicationUser.Group and
        /// re-runs the same Role sync legacy login already does (SyncRoleFromGroupAsync), so both take effect
        /// immediately instead of waiting for the user's next legacy login. userId is the already-resolved local
        /// user (TokenAuthenticationHandler already authenticated this request); token is the caller's own raw
        /// legacy JWT, forwarded to gama-api so it can identify the same user on its own side.
        /// </summary>
        Task<ResultData<Void>> LegacyUpdateGroupAsync(long userId, [NotNull] string token, int group);

        /// <summary>
        /// Phase 0 of the identities/dashboard proxy: loads the caller's local ApplicationUser.Group, then a
        /// field-for-field passthrough of gama-api's GET /teachers/dashboard or /students/dashboard (picked by
        /// that Group, exactly matching gamatrain-front's own selection today). token is the caller's raw legacy
        /// JWT (null when the caller has none, e.g. a native/local-token account). Always returns Succeeded -
        /// a failed/unreachable legacy call degrades to DashboardResponseDto.LegacyDataAvailable = false rather
        /// than failing the whole request; see docs/business/identity-and-access.md, "User dashboard proxy".
        /// </summary>
        Task<ResultData<DashboardResponseDto>> GetDashboardAsync(long userId, string? token);
    }
}

