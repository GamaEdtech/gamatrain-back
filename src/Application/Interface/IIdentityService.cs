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
        Task<ResultData<GenerateUserTokenResponseDto>> GenerateTokenByCoreTokenAsync([NotNull] GenerateTokenByCoreTokenRequestDto requestDto);
        Task<ResultData<Void>> AddLoginHistoryAsync([NotNull] LoginHistoryRequestDto requestDto);
        Task<ResultData<PublicProfileResponseDto>> GetPublicProfileAsync([NotNull] PublicProfileRequestDto requestDto);
        Task<ResultData<bool>> ManageAvatarAsync([NotNull] ManageAvatarRequestDto requestDto);
        Task<ResultData<bool>> InitializeDeletingAccountAsync([NotNull] ISpecification<ApplicationUser> specification);
        Task<ResultData<bool>> RecoverAccountAsync([NotNull] ISpecification<ApplicationUser> specification);
        Task<ResultData<bool>> UpdateOrphanUsersAsync();
        Task<ResultData<string>> ValidateHandleAsync([NotNull] ValidateHandleRequestDto requestDto);
        Task<ResultData<ListDataSource<PublicProfileDto>>> GetProfilesListAsync(ListRequestDto<ApplicationUser>? requestDto = null);
        Task<ResultData<bool>> ConvertAvatarsAsync();
    }
}

