namespace GamaEdtech.Domain.Specification.Subscription
{
    using System.Linq.Expressions;

    using GamaEdtech.Common.DataAccess.Specification;
    using GamaEdtech.Domain.Entity;

    // One admin search box: the subscriber's email, first name, last name or full "first last" name contains the term.
    // The names are also matched on their own: in SQL the concatenation is NULL when either name is.
    public sealed class UserSubscriptionSearchSpecification(string term) : SpecificationBase<UserSubscription>
    {
        public override Expression<Func<UserSubscription, bool>> Expression()
        {
            var text = term.Trim();
            return (t) => t.User!.Email!.Contains(text)
                || t.User.FirstName!.Contains(text)
                || t.User.LastName!.Contains(text)
                || (t.User.FirstName + " " + t.User.LastName).Contains(text);
        }
    }
}
