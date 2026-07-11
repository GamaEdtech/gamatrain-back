namespace GamaEdtech.Infrastructure.Interface
{
    using System.Diagnostics.CodeAnalysis;

    using GamaEdtech.Common.Data;
    using GamaEdtech.Common.DataAnnotation;
    using GamaEdtech.Data.Dto.Game;
    using GamaEdtech.Data.Dto.Identity;

    [Injectable]
    public interface ICoreProvider
    {
        Task<ResultData<bool>> ValidateTestAsync([NotNull] TestTimeRequestDto requestDto);
        Task<ResultData<ExamResultResponseDto>> GetExamResultAsync([NotNull] ExamResultRequestDto requestDto);
        Task<ResultData<ExamInformationResponseDto>> GetExamInformationAsync([NotNull] ExamInformationRequestDto requestDto);
        Task<ResultData<UserInformationResponseDto>> GetUserInformationAsync([NotNull] UserInformationRequestDto requestDto);
        Task<ResultData<IEnumerable<KeyValuePair<int, string?>>>> GetBoardsAsync();

        /// <summary>
        /// Temporary legacy-auth-bridge methods, proxying gama-api's /users/* endpoints. Remove alongside LegacyAuthBridgeController once the frontend migrates off the old backend.
        /// </summary>
        Task<ResultData<LegacyAuthResponseDto>> LegacyLoginAsync([NotNull] LegacyLoginRequestDto requestDto);
        Task<ResultData<LegacyAuthResponseDto>> LegacyGoogleAuthAsync([NotNull] LegacyGoogleAuthRequestDto requestDto);
        Task<ResultData<LegacyMessageResponseDto>> LegacyRegisterAsync([NotNull] LegacyOtpFlowRequestDto requestDto);
        Task<ResultData<LegacyMessageResponseDto>> LegacyRecoveryAsync([NotNull] LegacyOtpFlowRequestDto requestDto);
    }
}
