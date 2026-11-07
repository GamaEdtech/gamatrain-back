namespace GamaEdtech.Domain.Specification.Identity
{
    using System.Linq.Expressions;

    using GamaEdtech.Common.DataAccess.Specification;
    using GamaEdtech.Domain.Entity.Identity;

    /// <summary>
    /// A user with a first or last name set (not blank). The public teacher directory lists only these: a card with
    /// no name has nothing to show for the person.
    /// </summary>
    public sealed class HasNameSpecification : SpecificationBase<ApplicationUser>
    {
        public override Expression<Func<ApplicationUser, bool>> Expression() =>
            (t) => !string.IsNullOrWhiteSpace(t.FirstName) || !string.IsNullOrWhiteSpace(t.LastName);
    }
}
