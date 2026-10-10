namespace GamaEdtech.Infrastructure.Interface
{
    using System.Diagnostics.CodeAnalysis;

    using GamaEdtech.Common.Data;
    using GamaEdtech.Common.DataAnnotation;
    using GamaEdtech.Data.Dto.ExamImport;
    using GamaEdtech.Data.Dto.Game;
    using GamaEdtech.Data.Dto.Identity;

    [Injectable]
    public interface ICoreProvider
    {
        Task<ResultData<bool>> ValidateTestAsync([NotNull] TestTimeRequestDto requestDto);
        Task<ResultData<ExamResultResponseDto>> GetExamResultAsync([NotNull] ExamResultRequestDto requestDto);
        Task<ResultData<ExamInformationResponseDto>> GetExamInformationAsync([NotNull] ExamInformationRequestDto requestDto);

        /// <summary>How many questions an exam has (its <c>exams/{id}</c> question ids), without loading them -- what an
        /// export's price is based on (see <c>ExamExportPricing</c>).</summary>
        Task<ResultData<int>> GetExamQuestionCountAsync([NotNull] ExamInformationRequestDto requestDto);
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

        /// <summary>
        /// gama-api's exam builder, used by the MCP exam import (see IExamImportService), always with the caller's own
        /// token (<c>SecretKey</c>). gama-api's field names stay here. On failure the error carries gama-api's error code
        /// in <c>Reference</c>, its <c>reason</c> (e.g. <c>alreadyInProgress</c>) in <c>Info</c> and the HTTP status
        /// (when there was a response) in <c>Value</c>.
        /// </summary>
        Task<ResultData<IEnumerable<ExamImportOptionDto>>> GetExamOptionsAsync([NotNull] ExamImportOptionsRequestDto requestDto);

        /// <summary><c>GET tests</c>: all papers for gama-api's admins and sub-admins, only the caller's own for a teacher.</summary>
        Task<ResultData<IEnumerable<ExamImportPastPaperDto>>> GetPastPapersAsync([NotNull] ExamImportPastPapersRequestDto requestDto);

        /// <summary><c>GET search?type=test</c>: gamatrain's paper directory, the confirmed past papers whose title has
        /// <see cref="ExamImportPastPapersRequestDto.Title"/>, newest first, with whether each has an online exam.</summary>
        Task<ResultData<IEnumerable<ExamImportPastPaperDto>>> SearchPastPapersAsync([NotNull] ExamImportPastPapersRequestDto requestDto);

        /// <summary><c>GET tests/{id}</c>: a past paper with its files, each with whether it is free for the caller.</summary>
        Task<ResultData<ExamImportPastPaperDto>> GetPastPaperAsync([NotNull] ExamImportRequestDto requestDto);

        /// <summary>
        /// <c>GET tests/download/{id}/{type}[/{extraId}]</c>: gama-api's temporary download link (about an hour) to one of a
        /// paper's files. One call per second (gama-api answers <c>gone</c> to more). It charges a caller who neither manages
        /// the paper nor has the file for free, so call it only for those.
        /// </summary>
        Task<ResultData<Uri>> GetPastPaperFileUrlAsync([NotNull] ExamImportPaperFileRequestDto requestDto);

        /// <summary><c>POST upload</c>: the temporary file key a question's image fields take. gama-api moves the file when
        /// the question is saved, so a key works for one question only.</summary>
        Task<ResultData<string>> UploadFileAsync([NotNull] ExamImportUploadRequestDto requestDto);

        /// <summary><c>POST examTests</c>, or <c>PUT examTests/{id}</c> to change one - the question's id. An image left out
        /// keeps the question's image.</summary>
        Task<ResultData<long>> SaveExamTestAsync([NotNull] SaveExamTestRequestDto requestDto);

        Task<ResultData<Void>> DeleteExamTestAsync([NotNull] ExamImportRequestDto requestDto);

        /// <summary><c>GET exams/current</c>: the id of the caller's unpublished draft exam (a teacher has one at most), or null.</summary>
        Task<ResultData<long?>> GetCurrentExamIdAsync([NotNull] ExamImportRequestDto requestDto);

        /// <summary><c>GET exams/{id}</c>: an exam's details, its question ids and whether the caller owns it.</summary>
        Task<ResultData<ExamImportDraftDto>> GetExamAsync([NotNull] ExamImportRequestDto requestDto);

        /// <summary><c>GET exams</c>: a page of the caller's exams, newest first, and how many there are. gama-api filters
        /// them by status for its staff only: a teacher always gets every status.</summary>
        Task<ResultData<ListDataSource<ExamImportDraftDto>>> GetExamsAsync([NotNull] ExamImportExamsRequestDto requestDto);

        /// <summary><c>GET examTests?exam_id={id}</c>: an exam's questions in exam order (for its owner or a manager).</summary>
        Task<ResultData<IEnumerable<ExamImportDraftQuestionDto>>> GetExamQuestionsAsync([NotNull] ExamImportRequestDto requestDto);

        /// <summary><c>POST exams</c>, a draft (status 6; gama-api allows a teacher one), or <c>PUT exams/{id}</c> to change
        /// one - the exam's id.</summary>
        Task<ResultData<long>> SaveExamAsync([NotNull] SaveExamRequestDto requestDto);

        /// <summary><c>GET exams/tests/{id}</c>: the ids of an exam's questions, in exam order.</summary>
        Task<ResultData<IEnumerable<long>>> GetExamTestIdsAsync([NotNull] ExamImportRequestDto requestDto);

        /// <summary><c>PUT exams/tests/{id}</c>: replaces the exam's whole question list (it does not append).</summary>
        Task<ResultData<Void>> SetExamTestsAsync([NotNull] SetExamTestsRequestDto requestDto);

        Task<ResultData<Void>> PublishExamAsync([NotNull] ExamImportRequestDto requestDto);

        Task<ResultData<Void>> DeleteExamAsync([NotNull] ExamImportRequestDto requestDto);
    }
}
