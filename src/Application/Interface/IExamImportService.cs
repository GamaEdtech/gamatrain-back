namespace GamaEdtech.Application.Interface
{
    using System.Diagnostics.CodeAnalysis;

    using GamaEdtech.Common.Data;
    using GamaEdtech.Common.DataAnnotation;
    using GamaEdtech.Data.Dto.ExamImport;

    /// <summary>
    /// Importing a past paper into gama-api as questions on a draft exam through the MCP connector (the tools in
    /// <c>Presentation/Mcp</c>): the AI reads the paper and hands over the exam details, the figure images and the
    /// extracted questions; this checks them against gama-api's rules and saves them straight into the caller's draft exam
    /// on gama-api, within the tool call. Nothing is kept here: the draft on gama-api is the import. Nothing here reads or
    /// converts the paper itself. See docs/business/exams-and-content.md, "Exam import (MCP)". <c>token</c> is always the
    /// caller's own gama-api token, and <c>examId</c> must be the caller's unpublished draft.
    /// </summary>
    [Injectable]
    public interface IExamImportService
    {
        /// <summary>Whether the caller is staff (a gama-api admin or sub-admin, who can start from a paper on gamatrain), and
        /// their unpublished draft exam, if any.</summary>
        Task<ResultData<ExamImportStatusDto>> GetStatusAsync([NotNull] string token);

        /// <summary>gama-api's choices for an exam detail: <paramref name="kind"/> is board, grade (parent: board), course
        /// (parent: board), subject (parent: grade, within <paramref name="courseId"/> when the board has courses), topic
        /// (parent: subject) or paper; <paramref name="search"/> ranks them.</summary>
        Task<ResultData<IEnumerable<ExamImportOptionDto>>> GetOptionsAsync([NotNull] string token, [NotNull] string kind, int? parentId, int? courseId, string? search);

        /// <summary>
        /// Checks every exam detail as a chain (each id under its parent) and, once the user confirmed them
        /// (<see cref="ExamImportDetailsRequestDto.Confirmed"/>), creates the draft exam on gama-api or changes the details of
        /// draft <see cref="ExamImportDetailsRequestDto.ExamId"/>. Returns the details as they will be saved, or the draft with
        /// the subject's topics. A required detail that is left out or not valid under its parent fails with
        /// <c>pick{Detail}</c> (pickBoard, pickGrade, pickCourse, pickSubject, pickPaper, pickDuration) and gama-api's
        /// choices for it in <c>Error.Value</c>. A teacher who already has a draft is told so (<c>existingDraft</c>, the draft
        /// in <c>Error.Value</c>), to continue it or delete it.
        /// </summary>
        Task<ResultData<ExamImportDraftDto>> SetDetailsAsync([NotNull] string token, [NotNull] ExamImportDetailsRequestDto requestDto);

        /// <summary>Past papers on gamatrain with this board, grade, subject, paper type, year and session, to link the exam
        /// to, a page of 15. <paramref name="paperId"/> is the exam's paper type (<c>list_options</c> kind=paper).</summary>
        Task<ResultData<IEnumerable<ExamImportPastPaperDto>>> FindPastPapersAsync([NotNull] string token, int boardId, int gradeId, int subjectId, int? year, int? sessionMonth, int? paperId, int page);

        /// <summary>Staff only: the papers most recently added to gamatrain, newest first, a page of <paramref name="pageSize"/>.</summary>
        Task<ResultData<IEnumerable<ExamImportPastPaperDto>>> GetRecentPapersAsync([NotNull] string token, int page, int pageSize);

        /// <summary>Staff only: the past papers in gamatrain's directory whose title and classification have every word of
        /// <paramref name="text"/> in any order (e.g. <c>9709 paper 1 2024</c>), newest first, a page of
        /// <paramref name="pageSize"/>, and how many match.</summary>
        Task<ResultData<ListDataSource<ExamImportPastPaperDto>>> SearchPapersAsync([NotNull] string token, [NotNull] string text, int page, int pageSize);

        /// <summary>
        /// Staff only: a paper on gamatrain to make the exam from: its exam details (for <see cref="SetDetailsAsync"/>) and
        /// gama-api's temporary download link to each of its files, for the AI to read.
        /// </summary>
        Task<ResultData<ExamImportPastPaperDto>> LoadPaperAsync([NotNull] string token, long paperId);

        /// <summary>Uploads a PNG or JPEG image to gama-api for a question (downloading it first when it comes as a link);
        /// the result's key works for one question.</summary>
        Task<ResultData<ExamImportFigureDto>> AddFigureAsync([NotNull] string token, [NotNull] AddExamImportFigureRequestDto requestDto, CancellationToken cancellationToken = default);

        /// <summary>The same as <see cref="AddFigureAsync"/>, for an upload through a link from <see cref="GetFigureUploadLink"/>.</summary>
        Task<ResultData<ExamImportFigureDto>> AddFigureByLinkAsync([NotNull] string link, [NotNull] AddExamImportFigureRequestDto requestDto);

        /// <summary>A signed, short-lived URL the assistant can POST images to (multipart <c>file</c>) without the MCP
        /// session, e.g. with curl from a shell, instead of sending them through the chat.</summary>
        ResultData<Uri> GetFigureUploadLink([NotNull] string token);

        /// <summary>
        /// Saves the questions into the draft, in order: a new one is created and added to the draft, one with an id (on the
        /// draft) is changed, a skipped one with an id is removed. A question with an error isn't saved.
        /// </summary>
        Task<ResultData<ExamImportReportDto>> SaveQuestionsAsync([NotNull] string token, long examId, [NotNull] IEnumerable<ExamImportQuestionDto> questions);

        /// <summary>Takes a question off the draft, and deletes it when the caller made it and it isn't in the question bank yet.</summary>
        Task<ResultData<ExamImportReportDto>> RemoveQuestionAsync([NotNull] string token, long examId, long questionId);

        /// <summary>The draft's questions as gama-api stores them.</summary>
        Task<ResultData<ExamImportPreviewDto>> GetPreviewAsync([NotNull] string token, long examId);

        /// <summary>The caller's unpublished draft <paramref name="examId"/>; anything else is refused.</summary>
        Task<ResultData<ExamImportDraftDto>> GetDraftAsync([NotNull] string token, long examId);

        Task<ResultData<ExamImportDraftResultDto>> PublishAsync([NotNull] string token, long examId);

        /// <summary>Deletes the draft exam and, when <paramref name="deleteQuestions"/>, the questions on it that the caller
        /// made and that aren't in the question bank yet. A published exam is never touched.</summary>
        Task<ResultData<ExamImportDraftResultDto>> DiscardAsync([NotNull] string token, long examId, bool deleteQuestions);
    }
}
