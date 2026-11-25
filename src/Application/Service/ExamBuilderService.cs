namespace GamaEdtech.Application.Service
{
    using System.Diagnostics.CodeAnalysis;

    using GamaEdtech.Application.Interface;
    using GamaEdtech.Common.Data;
    using GamaEdtech.Data.Dto.ExamBuilder;
    using GamaEdtech.Data.Dto.ExamImport;
    using GamaEdtech.Domain.Enumeration;
    using GamaEdtech.Infrastructure.Interface;

    using Void = Common.Data.Void;

    /// <inheritdoc cref="IExamBuilderService"/>
    public sealed class ExamBuilderService(Lazy<IMigrationRouter> migrationRouter) : IExamBuilderService
    {
        private const int MaxPageSize = 50;
        private const int MaxBankPageSize = 100;

        public Task<ResultData<long?>> GetCurrentDraftAsync(long userId, string? token) =>
            ReadAsync(t => t.GetCurrentExamIdAsync(Request(userId, token, 0)));

        public Task<ResultData<ListDataSource<ExamImportDraftDto>>> GetExamsAsync(long userId, string? token, IReadOnlyList<int> statuses, int page, int pageSize) =>
            ReadAsync(t => t.GetExamsAsync(new()
            {
                SecretKey = token ?? string.Empty,
                UserId = userId,
                Statuses = statuses,
                Page = Math.Max(page, 1),
                PageSize = Math.Clamp(pageSize, 1, MaxPageSize),
            }));

        public Task<ResultData<ExamImportDraftDto>> GetExamAsync(long userId, string? token, long examId) =>
            ReadAsync(t => t.GetExamAsync(Request(userId, token, examId)));

        public Task<ResultData<long>> SaveExamAsync([NotNull] SaveExamRequestDto requestDto) =>
            WriteAsync(t => t.SaveExamAsync(requestDto));

        public Task<ResultData<Void>> DeleteExamAsync(long userId, string? token, long examId) =>
            WriteAsync(t => t.DeleteExamAsync(Request(userId, token, examId)));

        public Task<ResultData<IEnumerable<ExamImportDraftQuestionDto>>> GetExamQuestionsAsync(long userId, string? token, long examId) =>
            ReadAsync(t => t.GetExamQuestionsAsync(Request(userId, token, examId)));

        public Task<ResultData<Void>> SetExamQuestionsAsync(long userId, string? token, long examId, [NotNull] IEnumerable<long> questionIds) =>
            WriteAsync(t => t.SetExamTestsAsync(new() { SecretKey = token ?? string.Empty, UserId = userId, ExamId = examId, TestIds = [.. questionIds] }));

        public Task<ResultData<Void>> PublishExamAsync(long userId, string? token, long examId) =>
            WriteAsync(t => t.PublishExamAsync(Request(userId, token, examId)));

        public Task<ResultData<ExamBuilderQuestionDto>> GetQuestionAsync(long userId, string? token, long questionId) =>
            ReadAsync(t => t.GetQuestionAsync(Request(userId, token, questionId)));

        public Task<ResultData<long>> SaveQuestionAsync([NotNull] SaveExamTestRequestDto requestDto) =>
            WriteAsync(t => t.SaveExamTestAsync(requestDto));

        public Task<ResultData<Void>> DeleteQuestionAsync(long userId, string? token, long questionId) =>
            WriteAsync(t => t.DeleteExamTestAsync(Request(userId, token, questionId)));

        public Task<ResultData<string>> UploadImageAsync([NotNull] ExamImportUploadRequestDto requestDto) =>
            WriteAsync(t => t.UploadFileAsync(requestDto));

        public Task<ResultData<ListDataSource<ExamBankQuestionDto>>> SearchBankAsync([NotNull] ExamBankRequestDto requestDto)
        {
            requestDto.Page = Math.Max(requestDto.Page, 1);
            requestDto.PageSize = Math.Clamp(requestDto.PageSize, 1, MaxBankPageSize);
            return ReadAsync(t => t.SearchBankAsync(requestDto));
        }

        private static ExamImportRequestDto Request(long userId, string? token, long id) => new() { SecretKey = token ?? string.Empty, UserId = userId, Id = id };

        private Task<ResultData<T>> ReadAsync<T>(Func<IExamSource, Task<ResultData<T>>> call) =>
            migrationRouter.Value.RunAsync(MigrationDomain.Exams, MigrationFeatures.TestMaker, call);

        private Task<ResultData<T>> WriteAsync<T>(Func<IExamSource, Task<ResultData<T>>> call) =>
            migrationRouter.Value.RunWriteAsync(MigrationDomain.Exams, MigrationFeatures.TestMaker, call);
    }
}
