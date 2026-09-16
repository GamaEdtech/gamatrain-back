namespace GamaEdtech.Domain.Specification.Payment
{
    using System.Linq.Expressions;

    using GamaEdtech.Common.DataAccess.Specification;
    using GamaEdtech.Domain.Entity;
    using GamaEdtech.Domain.Enumeration;

    public sealed class KindEqualsSpecification(PaymentKind kind) : SpecificationBase<Payment>
    {
        public override Expression<Func<Payment, bool>> Expression() => (t) => t.Kind == kind;
    }
}
