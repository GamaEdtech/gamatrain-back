namespace GamaEdtech.Application.Interface
{
    using System.Diagnostics.CodeAnalysis;

    using GamaEdtech.Common.Data;
    using GamaEdtech.Common.DataAnnotation;
    using GamaEdtech.Data.Dto.ExamImport;

    using Void = Common.Data.Void;

    /// <summary>
    /// Importing a past paper into gama-api as questions and a draft exam through the MCP connector (the tools in
    /// <c>Presentation/Api/Mcp</c>): the AI reads the paper and hands over the exam details, the figure images and the
    /// extracted questions; this checks them against gama-api's rules, previews them and uploads them in the background
    /// (gama-api allows one new question per user every 20 seconds). Nothing here reads or converts the paper itself.
    /// One import per user, see docs/business/exams-and-content.md, "Exam import (MCP)". <c>token</c> is always the
    /// caller's own gama-api token.
    /// </summary>
    [Injectable]
    public interface IExamImportService
    {
        Task<ResultData<ExamImportStatusDto>> GetStatusAsync(long userId);

        /// <summary>Forgets the caller's import (details, questions, figures, upload progress). Nothing on gama-api changes.</summary>
        Task<ResultData<Void>> StartNewAsync(long userId);

        /// <summary>gama-api's choices for an exam detail: <paramref name="kind"/> is board, grade (parent: board), course
        /// (parent: board), subject (parent: grade), topic (parent: subject) or paper; <paramref name="search"/> ranks them.</summary>
        Task<ResultData<IEnumerable<ExamImportOptionDto>>> GetOptionsAsync(long userId, [NotNull] string token, [NotNull] string kind, int? parentId, string? search);

        Task<ResultData<ExamImportStatusDto>> SetDetailsAsync(long userId, [NotNull] string token, [NotNull] ExamImportDetailsRequestDto requestDto);

        /// <summary>Past papers on gamatrain with the import's board, grade, subject, year and session, to link the exam to.</summary>
        Task<ResultData<IEnumerable<ExamImportPastPaperDto>>> FindPastPapersAsync(long userId, [NotNull] string token);

        /// <summary>Stores a PNG or JPEG image for the import's questions.</summary>
        Task<ResultData<ExamImportFigureDto>> AddFigureAsync([NotNull] AddExamImportFigureRequestDto requestDto);

        /// <summary>The same as <see cref="AddFigureAsync"/>, for an upload through a link from <see cref="GetFigureUploadLink"/>.</summary>
        Task<ResultData<ExamImportFigureDto>> AddFigureByLinkAsync([NotNull] string link, [NotNull] AddExamImportFigureRequestDto requestDto);

        /// <summary>A signed, short-lived URL the assistant can POST images to (multipart <c>file</c>) without the MCP
        /// session, e.g. with curl from a shell, instead of sending them through the chat.</summary>
        ResultData<Uri> GetFigureUploadLink(long userId);

        /// <summary>Saves the questions in paper order; the same number replaces the earlier one.</summary>
        Task<ResultData<ExamImportReportDto>> SaveQuestionsAsync(long userId, [NotNull] IEnumerable<ExamImportQuestionDto> questions, bool replaceAll);

        Task<ResultData<ExamImportReportDto>> RemoveQuestionAsync(long userId, [NotNull] string number);

        Task<ResultData<ExamImportReportDto>> GetReviewAsync(long userId, bool details);

        Task<ResultData<ExamImportPreviewDto>> GetPreviewAsync(long userId);

        /// <summary>The preview for a signed link from <see cref="GetPreviewAsync"/>.</summary>
        Task<ResultData<ExamImportPreviewDto>> GetPreviewByLinkAsync([NotNull] string link);

        /// <summary>A figure image for a signed preview link.</summary>
        Task<ResultData<ExamImportFigureDto>> GetFigureByLinkAsync([NotNull] string link, long figureId);

        /// <summary>
        /// Starts uploading the import as a draft exam: checks it, deals with an existing draft, keeps the caller's token
        /// (encrypted) and returns the import and run to hand to <see cref="RunUploadAsync"/> as a background job.
        /// </summary>
        Task<ResultData<ExamImportUploadResultDto>> StartUploadAsync([NotNull] StartExamImportUploadRequestDto requestDto);

        /// <summary>Uploads again the questions that failed, or resumes an interrupted upload.</summary>
        Task<ResultData<ExamImportUploadResultDto>> RetryUploadAsync(long userId, [NotNull] string token);

        /// <summary>
        /// Hangfire job target, enqueued after <see cref="StartUploadAsync"/>/<see cref="RetryUploadAsync"/> - not meant to
        /// be called directly. Creates the draft, then each question (rate limited, with retries), attaching them to the
        /// draft as it goes; progress is saved after every step, so a restart resumes without duplicates. Stops when
        /// <paramref name="runId"/> is no longer the import's current run (discarded, or a newer run started).
        /// </summary>
        Task<ResultData<Void>> RunUploadAsync(long importId, [NotNull] string runId, CancellationToken cancellationToken);

        /// <summary>The upload's progress; waits up to <paramref name="waitSeconds"/> (at most 40) for it to change.</summary>
        Task<ResultData<ExamImportUploadResultDto>> GetUploadStatusAsync(long userId, int waitSeconds, CancellationToken cancellationToken);

        Task<ResultData<ExamImportUploadResultDto>> PublishAsync(long userId, [NotNull] string token);

        /// <summary>Deletes the draft exam on gama-api and, when <paramref name="deleteQuestions"/>, the questions this
        /// import created. Stops a running upload.</summary>
        Task<ResultData<ExamImportUploadResultDto>> DiscardAsync(long userId, [NotNull] string token, bool deleteQuestions);

        /// <summary>Daily recurring job: removes imports unchanged for <c>Mcp:ImportRetentionDays</c> (14 by default).</summary>
        Task<ResultData<int>> RemoveStaleImportsAsync();
    }
}
