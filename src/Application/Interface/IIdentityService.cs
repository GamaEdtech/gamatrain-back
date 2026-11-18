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
        /// <summary>Creates a local account with a confirmed email, after checking the code SendRegistrationCodeAsync emailed. Answers the new
        /// user's id.</summary>
        Task<ResultData<long>> RegisterAsync([NotNull] RegistrationRequestDto requestDto);

        /// <summary>Emails a sign-up code to an address that has no account yet (step 1 of registering). Answers the address, masked.</summary>
        Task<ResultData<string>> SendRegistrationCodeAsync([NotNull] string email);

        /// <summary>Checks a sign-up code without using it up (step 2, before the password is chosen).</summary>
        Task<ResultData<Void>> VerifyRegistrationCodeAsync([NotNull] string email, string? code);

        /// <summary>Emails a code to set a new password to an account's address (step 1 of a forgotten password). Answers the address, masked.</summary>
        Task<ResultData<string>> SendPasswordResetCodeAsync([NotNull] string email);

        /// <summary>Checks a password reset code without using it up (step 2).</summary>
        Task<ResultData<Void>> VerifyPasswordResetCodeAsync([NotNull] string email, string? code);

        /// <summary>Sets a new password after checking the emailed code (step 3), which also confirms the address. Answers the user's id.</summary>
        Task<ResultData<long>> ResetPasswordWithCodeAsync([NotNull] string email, string? code, [NotNull] string password);

        /// <summary>
        /// Sign-in for a person whose password this backend doesn't hold yet (no account here, no password, or an older one): asks
        /// gama-api once, and when it accepts, links or creates the local account and keeps the password here, so later sign-ins
        /// never ask gama-api. Answers the local user's id, or a failure when gama-api refused (or wants its own emailed step).
        /// </summary>
        Task<ResultData<long>> SignInWithLegacyPasswordAsync([NotNull] string identity, [NotNull] string password);
        Task SendRegistrationEmailAsync([NotNull] RegistrationEmailRequestDto requestDto);
        Task<ResultData<SignInResponseDto>> SignInAsync([NotNull] SignInRequestDto requestDto);
        Task<ResultData<Void>> SignOutAsync();
        Task<ResultData<bool>> CreateUserAsync([NotNull] CreateUserRequestDto requestDto);
        Task<ResultData<bool>> UpdateUserAsync([NotNull] UpdateUserRequestDto requestDto);
        /// <summary>Enables/disables an account. Requires the acting admin's current authenticator code (step-up).</summary>
        Task<ResultData<bool>> ToggleUserAsync([NotNull] ISpecification<ApplicationUser> specification, long adminUserId, string? twoFactorCode);

        /// <summary>Deletes an account. Requires the acting admin's current authenticator code (step-up).</summary>
        Task<ResultData<bool>> RemoveUserAsync([NotNull] ISpecification<ApplicationUser> specification, long adminUserId, string? twoFactorCode);
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
        /// <summary>Emails the signed-in user a one-time code to confirm deleting their account; returns the masked address.</summary>
        Task<ResultData<string>> SendDeleteAccountCodeAsync(long userId);
        /// <summary>Checks the emailed code, then schedules the account for deletion in 7 days (OrphanDate).</summary>
        Task<ResultData<bool>> InitializeDeletingAccountAsync(long userId, string? code);
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
        /// Sets the caller's own user type on this backend only: <c>group</c> 5 = Teacher, 6 = Student (anything else is refused
        /// with <c>InvalidUserType</c>), then the matching Role, a new teacher's public profile and the referral offer. Unlike
        /// <see cref="LegacyUpdateGroupAsync"/>, gama-api is never told.
        /// </summary>
        Task<ResultData<Void>> UpdateUserTypeAsync(long userId, int group);

        /// <summary>
        /// The caller's dashboard, built from this backend's own tables: profile, completion, unread messages, the papers and
        /// exams they own, and the exams of their grade's subjects. See docs/business/identity-and-access.md, "User dashboard".
        /// </summary>
        Task<ResultData<DashboardResponseDto>> GetDashboardAsync(long userId);

        /// <summary>
        /// The group_id claim of a gama-api (legacy) JWT, after the same signature/issuer/expiry check as every other
        /// legacy-JWT path - gama-api's live group for that session (5 = Teacher, 6 = Student, 2 = member). Null when the
        /// token doesn't validate or carries no group_id. Used by the MCP sign-in to refuse accounts that can't add
        /// questions on gama-api.
        /// </summary>
        Task<int?> GetLegacyJwtGroupAsync(string? token);

        /// <summary>The user_id claim of a gama-api (legacy) JWT (the user's gama-api id), after the same check as
        /// <see cref="GetLegacyJwtGroupAsync"/>. Null when the token doesn't validate or carries no user_id.</summary>
        Task<long?> GetLegacyJwtUserIdAsync(string? token);
    }
}

