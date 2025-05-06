namespace GamaEdtech.Application.Interface
{
    using GamaEdtech.Common.Data;
    using GamaEdtech.Common.DataAccess.Specification;
    using System.Diagnostics.CodeAnalysis;

    using GamaEdtech.Common.DataAnnotation;

    using GamaEdtech.Data.Dto.Question;
    using GamaEdtech.Domain.Entity;

    [Injectable]
    public interface IQuestionService
    {
        Task<ResultData<ListDataSource<QuestionsDto>>> GetQuestionsAsync(ListRequestDto<Question>? requestDto = null);
        Task<ResultData<IEnumerable<QuestionDto>>> GetRandomQuestionsAsync([NotNull] RandomQuestionsRequestDto requestDto);
        Task<ResultData<QuestionDto>> GetQuestionAsync([NotNull] ISpecification<Question> specification);
        Task<ResultData<long>> ManageQuestionAsync([NotNull] ManageQuestionRequestDto requestDto);
        Task<ResultData<bool>> RemoveQuestionAsync([NotNull] ISpecification<Question> specification);
    }
}
