namespace GamaEdtech.Domain.Specification.School
{
    using System.Linq.Expressions;

    using GamaEdtech.Common.DataAccess.Specification;
    using GamaEdtech.Domain.Entity;

    /// <summary>Schools with one facility tag (e.g. Transport Services, the school bus). Several facilities are combined with And.</summary>
    public sealed class HasTagSpecification(long tagId) : SpecificationBase<School>
    {
        public override Expression<Func<School, bool>> Expression() => (t) => t.SchoolTags.Any(s => s.TagId == tagId);
    }
}
