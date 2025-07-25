namespace GamaEdtech.Application.Interface
{
    using GamaEdtech.Common.Data;
    using GamaEdtech.Common.DataAccess.Specification;
    using GamaEdtech.Common.DataAnnotation;

    using Microsoft.AspNetCore.Authentication.Cookies;

    using System.Diagnostics.CodeAnalysis;
    using GamaEdtech.Data.Dto.Identity;
    using GamaEdtech.Domain.Entity.Identity;
    using GamaEdtech.Domain.Enumeration;

    [Injectable]
    public interface IIdentityService
    {
        Task<ResultData<ListDataSource<ApplicationUserDto>>> GetUsersAsync(ListRequestDto<ApplicationUser>? requestDto = null);

        Task<ResultData<IEnumerable<ApplicationRoleDto>>> GetRolesAsync(ISpecification<ApplicationRoleDto>? specification = null);

        Task<ResultData<ApplicationUserDto>> GetUserAsync([NotNull] ISpecification<ApplicationUser> specification);

        Task<ResultData<ICollection<string>>> GetUserRolesAsync([NotNull] int userId);

        Task<ResultData<bool>> UserIsInRoleAsync([NotNull] int userId, [NotNull] string role);

        Task<ResultData<AuthenticationResponseDto>> AuthenticateAsync([NotNull] AuthenticationRequestDto requestDto);

        Task<ResultData<bool>> RegisterAsync([NotNull] RegistrationRequestDto requestDto);

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

        Task<ResultData<ProfileSettingsDto>> GetProfileSettingsAsync();

        Task<ResultData<ProfileSettingsUpdateResultDto>> UpdateProfileSettingsAsync([NotNull] ProfileSettingsDto requestDto);

        Task<ResultData<bool>> HasClaimAsync(int userId, SystemClaim claims);
    }
}
