namespace GamaEdtech.Application.Interface
{
    using System.Diagnostics.CodeAnalysis;

    using GamaEdtech.Common.Data;
    using GamaEdtech.Common.DataAnnotation;
    using GamaEdtech.Data.Dto.ExamAdmin;

    /// <summary>
    /// The admin panel's exams and question bank, on this backend's copy: lists, one exam or question, editing, status, delete,
    /// an exam's question order, adding a question. Reads work at any time; every change is refused (<c>notNative</c>) until
    /// <c>Migration:WriteRoutes:Exams</c> is <c>Native</c> (exams are edited on gama-api until then, and the copy would overwrite
    /// a change made here). See docs/business/exams-migration.md, "Admin".
    /// </summary>
    [Injectable]
    public interface IExamAdminService
    {
        Task<ResultData<ExamAdminListDto>> GetExamsAsync([NotNull] ExamAdminListRequestDto requestDto);

        /// <summary>Exam attempts (who took which exam, and the result), newest first, with the totals of all matching ones.</summary>
        Task<ResultData<ExamParticipationListDto>> GetParticipationsAsync([NotNull] ExamParticipationListRequestDto requestDto);

        Task<ResultData<ExamAdminDto>> GetExamAsync(long id);

        Task<ResultData<bool>> UpdateExamAsync(long id, [NotNull] ExamEditDto requestDto);

        Task<ResultData<bool>> SetExamStatusAsync(long id, byte status);

        /// <summary>Hides an exam (gama-api's <c>bit_delete</c>); its results stay.</summary>
        Task<ResultData<bool>> DeleteExamAsync(long id);

        /// <summary>Replaces an exam's questions with these, in this order.</summary>
        Task<ResultData<bool>> SetExamQuestionsAsync(long id, [NotNull] IReadOnlyList<long> questionIds);

        Task<ResultData<ExamQuestionAdminListDto>> GetQuestionsAsync([NotNull] ExamQuestionAdminListRequestDto requestDto);

        Task<ResultData<ExamQuestionAdminDto>> GetQuestionAsync(long id);

        /// <summary>Adds a question owned by <paramref name="userId"/>; answers its id.</summary>
        Task<ResultData<long>> CreateQuestionAsync(long userId, [NotNull] ExamQuestionEditDto requestDto);

        Task<ResultData<bool>> UpdateQuestionAsync(long id, [NotNull] ExamQuestionEditDto requestDto);

        Task<ResultData<bool>> SetQuestionStatusAsync(long id, byte status);

        /// <summary>Stores an image (JPG, PNG, GIF or WebP, at most 5 MB, metadata removed) as one of a question's separate image
        /// files, gama-api's way: <paramref name="slot"/> is <c>question</c>, <c>a</c>-<c>d</c> or <c>answerFull</c>. It replaces
        /// that slot's image; answers its public link.</summary>
        Task<ResultData<string>> SetQuestionImageAsync(long id, string slot, [NotNull] byte[] content);

        /// <summary>Takes a slot's image off the question. The stored file stays (a cloned question can share it).</summary>
        Task<ResultData<bool>> RemoveQuestionImageAsync(long id, string slot);

        /// <summary>Deletes a question that no exam uses.</summary>
        Task<ResultData<bool>> DeleteQuestionAsync(long id);
    }
}
