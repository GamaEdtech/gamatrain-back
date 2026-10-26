namespace GamaEdtech.Domain.Specification.ExamAnalysis
{
    using System.Linq.Expressions;

    using GamaEdtech.Common.DataAccess.Specification;
    using GamaEdtech.Domain.Entity;

    public sealed class UserIdAndExamIdEqualsSpecification(long userId, long examId) : SpecificationBase<ExamAnalysis>
    {
        public override Expression<Func<ExamAnalysis, bool>> Expression() => t => t.UserId == userId && t.ExamId == examId;
    }
}
