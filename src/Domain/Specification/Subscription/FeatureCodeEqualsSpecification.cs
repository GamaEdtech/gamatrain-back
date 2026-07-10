namespace GamaEdtech.Domain.Specification.Subscription
{
    using System.Linq.Expressions;

    using GamaEdtech.Common.DataAccess.Specification;
    using GamaEdtech.Domain.Entity;

    public sealed class FeatureCodeEqualsSpecification(string code) : SpecificationBase<Feature>
    {
        public override Expression<Func<Feature, bool>> Expression() => (t) => t.Code == code;
    }
}
