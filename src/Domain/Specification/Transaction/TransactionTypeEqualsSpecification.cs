namespace GamaEdtech.Domain.Specification.Transaction
{
    using System.Linq.Expressions;

    using GamaEdtech.Common.DataAccess.Specification;
    using GamaEdtech.Domain.Entity;
    using GamaEdtech.Domain.Enumeration;

    public sealed class TransactionTypeEqualsSpecification(TransactionType transactionType) : SpecificationBase<Transaction>
    {
        public override Expression<Func<Transaction, bool>> Expression() => (t) => t.TransactionType == transactionType;
    }
}
