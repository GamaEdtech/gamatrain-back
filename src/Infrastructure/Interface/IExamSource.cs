namespace GamaEdtech.Infrastructure.Interface
{
    using System.Diagnostics.CodeAnalysis;
    using System.Text.Json.Nodes;

    using GamaEdtech.Common.Data;
    using GamaEdtech.Common.DataAnnotation;
    using GamaEdtech.Common.Service.Factory;
    using GamaEdtech.Data.Dto.Exam;
    using GamaEdtech.Data.Dto.ExamImport;
    using GamaEdtech.Data.Dto.Game;
    using GamaEdtech.Data.Dto.Provider.Core;
    using GamaEdtech.Domain.Enumeration;

    /// <summary>
    /// The exams, their questions and results of <see cref="MigrationDomain.Exams"/>. Never call a source directly: go
    /// through <c>IMigrationRouter</c> (see <see cref="ITaxonomySource"/> for the rules a source follows), with
    /// <c>RunWriteAsync</c> for the calls under "Writes" below. The exam builder calls (used by the MCP exam import, see
    /// IExamImportService) always take the caller's own token (<c>SecretKey</c>) and keep gama-api's field names; on
    /// failure the error carries gama-api's error code in <c>Reference</c>, its <c>reason</c> (e.g.
    /// <c>alreadyInProgress</c>) in <c>Info</c> and the HTTP status (when there was a response) in <c>Value</c>.
    /// </summary>
    [Injectable]
    public interface IExamSource : IProvider<MigrationRoute>
    {
        Task<ResultData<bool>> ValidateTestAsync([NotNull] TestTimeRequestDto requestDto);

        Task<ResultData<ExamResultResponseDto>> GetExamResultAsync([NotNull] ExamResultRequestDto requestDto);

        /// <summary>The caller's own exam result in full (every question, its correct answer and the student's answer, the
        /// time taken and rank), from the same <c>exams/result/{id}</c> call as <see cref="GetExamResultAsync"/> -- what an
        /// exam analysis is built from.</summary>
        Task<ResultData<ExamResultDetailResponseDto>> GetExamResultDetailAsync([NotNull] ExamResultRequestDto requestDto);

        /// <summary>The caller's own <c>exams/result/{id}</c> (<c>Core:ExamResult</c>) as gama-api sends its <c>data</c>, for the
        /// results page (<c>GET exams/{id}/result</c>). Needs the caller's gama-api token.</summary>
        Task<ResultData<JsonObject>> GetExamResultPageAsync([NotNull] ExamResultRequestDto requestDto);

        /// <summary>Reads a result page from <see cref="GetExamResultPageAsync"/> the way <see cref="GetExamResultDetailAsync"/>
        /// does, without another call; null when it isn't a result. Pure.</summary>
        ExamResultDetailResponseDto? ReadExamResultDetail([NotNull] JsonObject page);

        /// <summary>Submits a student's exam (gama-api: <c>Core:ExamEnd</c>, <c>exams/end/{id}</c>) with their token. <c>Data</c>
        /// is the result id whenever one is given, also when it is refused (an exam already submitted answers with its result).
        /// A write: callers go through <c>IMigrationRouter.RunWriteAsync</c>.</summary>
        Task<ResultData<long?>> EndExamAsync([NotNull] ExamEndRequestDto requestDto);

        Task<ResultData<ExamInformationResponseDto>> GetExamInformationAsync([NotNull] ExamInformationRequestDto requestDto);

        /// <summary>How many questions an exam has (its <c>exams/{id}</c> question ids), without loading them -- what an
        /// export's price is based on (see <c>ExamExportPricing</c>).</summary>
        Task<ResultData<int>> GetExamQuestionCountAsync([NotNull] ExamInformationRequestDto requestDto);

        /// <summary><c>GET exams/{id}</c> (<c>Core:Exam</c>) as gama-api sends its <c>data</c>, for the public exam page. Public:
        /// <see cref="ExamInformationRequestDto.SecretKey"/> is optional and only adds the caller's own fields (e.g. their result).</summary>
        Task<ResultData<JsonObject>> GetExamDetailsAsync([NotNull] ExamInformationRequestDto requestDto);

        /// <summary>One question, anonymously (<c>examTests/{id}</c>, <c>Core:Test</c>), for the exam page's samples. It holds the
        /// correct option, so never pass it to a client as it is (see <c>ExamPageSamples.Map</c>).</summary>
        Task<ResultData<CoreExamTestResponse>> GetPublicExamTestAsync(long testId);

        /// <summary>Starts the caller's attempt at an exam, or resumes their unfinished one (<c>exams/start/{id}</c>): the attempt id
        /// (<c>startID</c>), the exam, its questions without their answers and the seconds left, as gama-api sends its <c>data</c>.
        /// Refused (<c>alreadyParticipated</c>, with the attempt id) once submitted. A write: route it with <c>RunWriteAsync</c>.</summary>
        Task<ResultData<JsonObject>> StartExamAsync([NotNull] ExamStartRequestDto requestDto);

        /// <summary>The caller's own results (<c>exams/results</c>), newest first: <c>num</c> and <c>list</c>.</summary>
        Task<ResultData<JsonObject>> GetUserResultsAsync([NotNull] ExamListRequestDto requestDto);

        /// <summary>An exam's participants for its owner (<c>exams/participants/{id}</c>): <c>num</c>, <c>list</c> and <c>info.exam</c>.</summary>
        Task<ResultData<JsonObject>> GetParticipantsAsync([NotNull] ExamListRequestDto requestDto);

        /// <summary>A question's public page (<c>examTests/{code}</c>, by its code, or by its id for a published one), as gama-api
        /// sends its <c>data</c>; <paramref name="full"/> adds the topic's tutorial (<c>?full=true</c>). Anonymous.</summary>
        Task<ResultData<JsonObject>> GetExamTestPageAsync([NotNull] string identity, bool full);

        /// <summary>A random published question of a subject (and topic) for practice (<c>examTests/random</c>): its code. Anonymous.</summary>
        Task<ResultData<JsonObject>> GetRandomExamTestAsync([NotNull] string query);

        /// <summary>The search's exam tab (<c>search?type=azmoon</c>): <c>num</c> and <c>list</c> only, published exams. Anonymous.</summary>
        Task<ResultData<JsonObject>> SearchExamsAsync([NotNull] string query);

        /// <summary><c>GET exams/current</c>: the id of the caller's unpublished draft exam (a teacher has one at most), or null.</summary>
        Task<ResultData<long?>> GetCurrentExamIdAsync([NotNull] ExamImportRequestDto requestDto);

        /// <summary><c>GET exams/{id}</c>: an exam's details, its question ids and whether the caller owns it.</summary>
        Task<ResultData<ExamImportDraftDto>> GetExamAsync([NotNull] ExamImportRequestDto requestDto);

        /// <summary><c>GET exams</c>: a page of the caller's exams, newest first, and how many there are. gama-api filters
        /// them by status for its staff only: a teacher always gets every status.</summary>
        Task<ResultData<ListDataSource<ExamImportDraftDto>>> GetExamsAsync([NotNull] ExamImportExamsRequestDto requestDto);

        /// <summary><c>GET examTests?exam_id={id}</c>: an exam's questions in exam order (for its owner or a manager).</summary>
        Task<ResultData<IEnumerable<ExamImportDraftQuestionDto>>> GetExamQuestionsAsync([NotNull] ExamImportRequestDto requestDto);

        /// <summary><c>GET exams/tests/{id}</c>: the ids of an exam's questions, in exam order.</summary>
        Task<ResultData<IEnumerable<long>>> GetExamTestIdsAsync([NotNull] ExamImportRequestDto requestDto);

        // Writes: route these with IMigrationRouter.RunWriteAsync.

        /// <summary><c>POST upload</c>: the temporary file key a question's image fields take. gama-api moves the file when
        /// the question is saved, so a key works for one question only.</summary>
        Task<ResultData<string>> UploadFileAsync([NotNull] ExamImportUploadRequestDto requestDto);

        /// <summary><c>POST examTests</c>, or <c>PUT examTests/{id}</c> to change one - the question's id. An image left out
        /// keeps the question's image.</summary>
        Task<ResultData<long>> SaveExamTestAsync([NotNull] SaveExamTestRequestDto requestDto);

        Task<ResultData<Void>> DeleteExamTestAsync([NotNull] ExamImportRequestDto requestDto);

        /// <summary><c>POST exams</c>, a draft (status 6; gama-api allows a teacher one), or <c>PUT exams/{id}</c> to change
        /// one - the exam's id.</summary>
        Task<ResultData<long>> SaveExamAsync([NotNull] SaveExamRequestDto requestDto);

        /// <summary><c>PUT exams/tests/{id}</c>: replaces the exam's whole question list (it does not append).</summary>
        Task<ResultData<Void>> SetExamTestsAsync([NotNull] SetExamTestsRequestDto requestDto);

        Task<ResultData<Void>> PublishExamAsync([NotNull] ExamImportRequestDto requestDto);

        Task<ResultData<Void>> DeleteExamAsync([NotNull] ExamImportRequestDto requestDto);
    }
}
