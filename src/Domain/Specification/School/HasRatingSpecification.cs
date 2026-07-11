namespace GamaEdtech.Domain.Specification.School
{
    using System.Linq.Expressions;

    using GamaEdtech.Common.DataAccess.Specification;
    using GamaEdtech.Domain.Entity;

    public sealed class HasRatingSpecification(bool hasRating) : SpecificationBase<School>
    {
        public override Expression<Func<School, bool>> Expression() => (t) => hasRating && t.SchoolComments.Any();
    }
}
