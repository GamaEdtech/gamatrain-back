namespace GamaEdtech.Application.Interface
{
    using System.Diagnostics.CodeAnalysis;

    using GamaEdtech.Common.Data;
    using GamaEdtech.Common.DataAnnotation;
    using GamaEdtech.Data.Dto.ExamBuilder;
    using GamaEdtech.Data.Dto.ExamImport;

    using Void = Common.Data.Void;

    /// <summary>
    /// A teacher building exams over the REST API (the website's test maker, the exam bots): their draft, its questions and
    /// publishing, and the question bank to pick from. Through <see cref="IMigrationRouter"/> (<c>Exams</c>, feature
    /// <c>TestMaker</c>); the caller is always <c>userId</c>, the signed-in user. See
    /// docs/business/exams-migration.md, "Exam builder API".
    /// </summary>
    [Injectable]
    public interface IExamBuilderService
    {
        Task<ResultData<long?>> GetCurrentDraftAsync(long userId, string? token);

        Task<ResultData<ListDataSource<ExamImportDraftDto>>> GetExamsAsync(long userId, string? token, IReadOnlyList<int> statuses, int page, int pageSize);

        Task<ResultData<ExamImportDraftDto>> GetExamAsync(long userId, string? token, long examId);

        /// <summary>Creates the caller's draft (<c>ExamId</c> null; one at a time) or changes their exam. The exam's id.</summary>
        Task<ResultData<long>> SaveExamAsync([NotNull] SaveExamRequestDto requestDto);

        Task<ResultData<Void>> DeleteExamAsync(long userId, string? token, long examId);

        Task<ResultData<IEnumerable<ExamImportDraftQuestionDto>>> GetExamQuestionsAsync(long userId, string? token, long examId);

        /// <summary>Replaces the exam's question list, in this order.</summary>
        Task<ResultData<Void>> SetExamQuestionsAsync(long userId, string? token, long examId, [NotNull] IEnumerable<long> questionIds);

        Task<ResultData<Void>> PublishExamAsync(long userId, string? token, long examId);

        /// <summary>One of the caller's own questions with every field a change takes.</summary>
        Task<ResultData<ExamBuilderQuestionDto>> GetQuestionAsync(long userId, string? token, long questionId);

        /// <summary>Creates a question (<c>Id</c> null) or changes the caller's own. The question's id.</summary>
        Task<ResultData<long>> SaveQuestionAsync([NotNull] SaveExamTestRequestDto requestDto);

        Task<ResultData<Void>> DeleteQuestionAsync(long userId, string? token, long questionId);

        /// <summary>Stores a question image; the key the question's image fields take.</summary>
        Task<ResultData<string>> UploadImageAsync([NotNull] ExamImportUploadRequestDto requestDto);

        Task<ResultData<ListDataSource<ExamBankQuestionDto>>> SearchBankAsync([NotNull] ExamBankRequestDto requestDto);
    }
}
