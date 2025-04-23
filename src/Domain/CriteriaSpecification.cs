namespace GamaEdtech.Domain
{
    using System.Linq.Expressions;

    public sealed class IdentityCriteriaSpecification<T> : CriteriaSpecification<T>
    {
        public override Expression<Func<T, bool>> ToExpression() => x => true;
    }

    public abstract class CriteriaSpecification<T>
    {
        public static readonly CriteriaSpecification<T> All = new IdentityCriteriaSpecification<T>();

        public bool IsSatisfiedBy(T entity)
        {
            var predicate = ToExpression().Compile();
            return predicate(entity);
        }

        public abstract Expression<Func<T, bool>> ToExpression();

#pragma warning disable S3358 // Disable nested ternary operation warning
        public CriteriaSpecification<T> And(CriteriaSpecification<T> specification)
        {
            var isThisAll = this == All;
            var isSpecificationAll = specification == All;

            return isThisAll
                ? specification
                : isSpecificationAll
                    ? this
                    : new AndCriteriaSpecification<T>(this, specification);
        }
#pragma warning restore S3358

        public CriteriaSpecification<T> Or(CriteriaSpecification<T> specification) => this == All || specification == All ? All
                : new OrCriteriaSpecification<T>(this, specification);

        public CriteriaSpecification<T> Not() => new NotCriteriaSpecification<T>(this);
    }

    internal sealed class AndCriteriaSpecification<T>(CriteriaSpecification<T> left, CriteriaSpecification<T> right)
        : CriteriaSpecification<T>
    {
        private readonly CriteriaSpecification<T> left = left;
        private readonly CriteriaSpecification<T> right = right;

        public override Expression<Func<T, bool>> ToExpression()
        {
            var leftExpression = left.ToExpression();
            var rightExpression = right.ToExpression();

            var invokedExpression = Expression.Invoke(rightExpression, leftExpression.Parameters);

            return (Expression<Func<T, bool>>)Expression.Lambda(Expression.AndAlso(leftExpression.Body, invokedExpression), leftExpression.Parameters);
        }
    }

    internal sealed class OrCriteriaSpecification<T>(CriteriaSpecification<T> left, CriteriaSpecification<T> right)
        : CriteriaSpecification<T>
    {
        private readonly CriteriaSpecification<T> left = left;
        private readonly CriteriaSpecification<T> right = right;

        public override Expression<Func<T, bool>> ToExpression()
        {
            var leftExpression = left.ToExpression();
            var rightExpression = right.ToExpression();

            var invokedExpression = Expression.Invoke(rightExpression, leftExpression.Parameters);

            return (Expression<Func<T, bool>>)Expression.Lambda(Expression.OrElse(leftExpression.Body, invokedExpression), leftExpression.Parameters);
        }
    }

    internal sealed class NotCriteriaSpecification<T>(CriteriaSpecification<T> specification)
        : CriteriaSpecification<T>
    {
        private readonly CriteriaSpecification<T> specification = specification;

        public override Expression<Func<T, bool>> ToExpression()
        {
            var expression = specification.ToExpression();
            var notExpression = Expression.Not(expression.Body);

            return Expression.Lambda<Func<T, bool>>(notExpression, expression.Parameters.Single());
        }
    }
}
