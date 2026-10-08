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
        /// <paramref name="token"/>. On failure the error carries gama-api's error code in <c>Reference</c>, its
        /// <c>reason</c> (e.g. <c>rateLimit-addnew</c>) in <c>Info</c> and the HTTP status (when there was a response)
        /// in <c>Value</c>.
        /// </summary>
        /// <param name="token">The caller's gama-api token.</param>
        /// <param name="type">section, base, course, lesson, topic or exam_type.</param>
        /// <param name="filters">The parent filter: section_id, base_id, course_id or lesson_id.</param>
        Task<ResultData<IEnumerable<ExamImportOptionDto>>> GetTypesAsync([NotNull] string token, [NotNull] string type, IReadOnlyDictionary<string, string?>? filters = null);

        /// <summary>Past papers (<c>GET tests</c>) matching <paramref name="filters"/> (section, base, lesson, edu_year, edu_month,
        /// page, perpage, sortby): all of them for gama-api's admins and sub-admins, only the caller's own for a teacher.</summary>
        Task<ResultData<IEnumerable<ExamImportPastPaperDto>>> GetPastPapersAsync([NotNull] string token, [NotNull] IReadOnlyDictionary<string, string?> filters);

        /// <summary><c>GET tests/{id}</c>: a past paper with its files, each with whether it is free for the caller.</summary>
        Task<ResultData<ExamImportPastPaperDto>> GetPastPaperAsync([NotNull] string token, long id);

        /// <summary>
        /// <c>GET tests/download/{id}/{type}[/{extraId}]</c>: gama-api's temporary download link (about an hour) to one of a
        /// paper's files. One call per second (gama-api answers <c>gone</c> to more). It charges a caller who neither manages
        /// the paper nor has the file for free, so call it only for those.
        /// </summary>
        Task<ResultData<Uri>> GetPastPaperFileUrlAsync([NotNull] string token, long id, [NotNull] string type, long? extraId);

        /// <summary><c>POST upload</c>: the temporary file key a question's <c>q_file</c>/<c>a_file</c>... fields take. gama-api
        /// moves the file when the question is created, so a key works for one question only.</summary>
        Task<ResultData<string>> UploadFileAsync([NotNull] string token, [NotNull] string fileName, [NotNull] string contentType, [NotNull] byte[] content);

        /// <summary><c>POST examTests</c> (one question per user every 20 seconds) - the new question's id.</summary>
        Task<ResultData<long>> CreateExamTestAsync([NotNull] string token, [NotNull] IReadOnlyList<KeyValuePair<string, string?>> form);

        Task<ResultData<Void>> DeleteExamTestAsync([NotNull] string token, long id);

        /// <summary><c>GET exams/current</c>: the caller's unpublished draft exam (one per teacher), or null.</summary>
        Task<ResultData<ExamImportDraftDto?>> GetCurrentExamAsync([NotNull] string token);

        /// <summary><c>POST exams</c> (one exam per user every 60 seconds) - a draft (status 6).</summary>
        Task<ResultData<ExamImportDraftDto>> CreateExamAsync([NotNull] string token, [NotNull] IReadOnlyList<KeyValuePair<string, string?>> form);

        Task<ResultData<Void>> UpdateExamAsync([NotNull] string token, long id, [NotNull] IReadOnlyList<KeyValuePair<string, string?>> form);

        /// <summary><c>PUT exams/tests/{id}</c>: replaces the exam's whole question list (it does not append); needs at least one id.</summary>
        Task<ResultData<Void>> SetExamTestsAsync([NotNull] string token, long id, [NotNull] IEnumerable<long> testIds);

        Task<ResultData<Void>> PublishExamAsync([NotNull] string token, long id);

        Task<ResultData<Void>> DeleteExamAsync([NotNull] string token, long id);
    }
}
