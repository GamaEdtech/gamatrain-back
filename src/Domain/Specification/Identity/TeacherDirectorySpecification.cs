namespace GamaEdtech.Domain.Specification.Identity
{
    using System.Linq.Expressions;

    using GamaEdtech.Common.DataAccess.Specification;
    using GamaEdtech.Domain.Entity.Identity;
    using GamaEdtech.Domain.Enumeration;

    /// <summary>
    /// The profiles the public teacher directory lists and the sitemap advertises: Public, in Role.Teacher, with a first
    /// or last name. Students are never listed or advertised (often minors; GDPR data minimisation), and a profile with no
    /// name has nothing to show. One rule for both, so they can't drift apart.
    /// </summary>
    public sealed class TeacherDirectorySpecification : SpecificationBase<ApplicationUser>
    {
        public override Expression<Func<ApplicationUser, bool>> Expression() =>
            new ProfileVisibilityEqualsSpecification(ProfileVisibility.Public)
                .And(new RoleEqualsSpecification(Role.Teacher))
                .And(new HasNameSpecification())
                .Expression();
    }
}
