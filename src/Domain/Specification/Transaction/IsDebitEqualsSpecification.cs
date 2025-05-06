namespace GamaEdtech.Domain.Specification.Transaction
{
    using System.Linq.Expressions;

    using GamaEdtech.Common.DataAccess.Specification;
    using GamaEdtech.Domain.Entity;

    public sealed class IsDebitEqualsSpecification(bool isDebit) : SpecificationBase<Transaction>
    {
        public override Expression<Func<Transaction, bool>> Expression() => (t) => t.IsDebit == isDebit;
    }
}
