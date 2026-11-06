namespace GamaEdtech.Domain.Specification
{
    using System.Linq.Expressions;

    using GamaEdtech.Common.DataAccess.Specification;
    using GamaEdtech.Domain.Entity;

    /// <summary>
    /// Contributions last changed before <paramref name="date"/>. Contributions confirmed before confirmation
    /// recorded LastModifyDate have none, so they fall back to CreationDate.
    /// </summary>
    public sealed class LastActivityBeforeSpecification(DateTimeOffset date) : SpecificationBase<Contribution>
    {
        public override Expression<Func<Contribution, bool>> Expression() => (t) => (t.LastModifyDate ?? t.CreationDate) < date;
    }
}
