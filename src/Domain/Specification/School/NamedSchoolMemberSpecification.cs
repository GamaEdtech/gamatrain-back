namespace GamaEdtech.Domain.Specification.School
{
    using System.Linq.Expressions;

    using GamaEdtech.Common.DataAccess.Specification;
    using GamaEdtech.Domain.Entity;
    using GamaEdtech.Domain.Enumeration;

    /// <summary>
    /// The members a school page may name: teachers and staff who agreed to be named, with a Public profile and a name.
    /// Students, parents and alumni are only ever counted (often minors and their families).
    /// </summary>
    public sealed class NamedSchoolMemberSpecification : SpecificationBase<SchoolMember>
    {
        public override Expression<Func<SchoolMember, bool>> Expression() => (t) =>
            (t.Role == SchoolMemberRole.Teacher || t.Role == SchoolMemberRole.Staff)
            && t.ShowName
            && t.User!.ProfileVisibility == ProfileVisibility.Public
            && (!string.IsNullOrWhiteSpace(t.User.FirstName) || !string.IsNullOrWhiteSpace(t.User.LastName));
    }
}
