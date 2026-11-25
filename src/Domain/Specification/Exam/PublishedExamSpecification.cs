namespace GamaEdtech.Domain.Specification.Exam
{
    using System.Linq.Expressions;

    using GamaEdtech.Common.DataAccess.Specification;
    using GamaEdtech.Domain.Entity;

    public sealed class PublishedExamSpecification : SpecificationBase<Exam>
    {
        private static readonly byte[] PublishedStatuses = [1, 5, 7];

        public override Expression<Func<Exam, bool>> Expression() => (t) => !t.IsDeleted && PublishedStatuses.Contains(t.Status);
    }
}
