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
        Task<ResultData<IEnumerable<KeyValuePair<int, string?>>>> GetBoardsAsync();

        /// <summary>
        /// Temporary legacy-auth-bridge methods, proxying gama-api's /users/* endpoints. Remove alongside LegacyAuthBridgeController once the frontend migrates off the old backend.
        /// </summary>
        Task<ResultData<LegacyAuthResponseDto>> LegacyLoginAsync([NotNull] LegacyLoginRequestDto requestDto);
        Task<ResultData<LegacyAuthResponseDto>> LegacyGoogleAuthAsync([NotNull] LegacyGoogleAuthRequestDto requestDto);
        Task<ResultData<LegacyMessageResponseDto>> LegacyRegisterAsync([NotNull] LegacyOtpFlowRequestDto requestDto);
        Task<ResultData<LegacyMessageResponseDto>> LegacyRecoveryAsync([NotNull] LegacyOtpFlowRequestDto requestDto);
        Task<ResultData<Void>> LegacyLogoutAsync([NotNull] LegacyLogoutRequestDto requestDto);

        /// <summary>
        /// Proxies gama-api's POST /users/group (Set user group - 5 = Teacher, 6 = Student, see
        /// ApplicationUser.Group's doc comment). Deliberately never sends gama-api's optional "uid" form field -
        /// omitting it makes gama-api infer the target user from the forwarded token itself, so this can only
        /// ever act on the caller's own account, never an arbitrary uid an untrusted caller could supply.
        /// </summary>
        Task<ResultData<Void>> LegacyUpdateGroupAsync([NotNull] LegacyUpdateGroupRequestDto requestDto);

        /// <summary>
        /// Proxies gama-api's GET /teachers/dashboard or GET /students/dashboard (picked by requestDto.Group,
        /// mirroring gamatrain-front's own selection - see DashboardRequestDto.Group). Not part of the
        /// legacy-auth-bridge (this call doesn't need an [AllowAnonymous] action; the caller is always already
        /// authenticated against this backend), but shares the same "forward the raw legacy JWT" mechanism.
        /// </summary>
        Task<ResultData<LegacyDashboardDataDto>> GetDashboardAsync([NotNull] DashboardRequestDto requestDto);
    }
}
