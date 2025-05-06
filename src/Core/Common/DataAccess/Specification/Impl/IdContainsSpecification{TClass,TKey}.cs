namespace GamaEdtech.Common.DataAccess.Specification.Impl
{
    using System;
    using System.Linq.Expressions;

    using GamaEdtech.Common.DataAccess.Entities;
    using GamaEdtech.Common.DataAccess.Specification;

    public sealed class IdContainsSpecification<TClass, TKey>(IEnumerable<TKey> ids) : SpecificationBase<TClass>
        where TClass : class, IIdentifiable<TKey>
        where TKey : IEquatable<TKey>
    {
        public override Expression<Func<TClass, bool>> Expression() => t => ids.Contains(t.Id);
    }
}
