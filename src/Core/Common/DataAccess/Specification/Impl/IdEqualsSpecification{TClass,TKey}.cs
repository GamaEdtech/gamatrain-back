namespace GamaEdtech.Common.DataAccess.Specification.Impl
{
    using System;
    using System.Linq.Expressions;

    using GamaEdtech.Common.DataAccess.Entities;
    using GamaEdtech.Common.DataAccess.Specification;

    public sealed class IdEqualsSpecification<TClass, TKey>(TKey id) : SpecificationBase<TClass>
        where TClass : class, IIdentifiable<TKey>
        where TKey : IEquatable<TKey>
    {
        public override Expression<Func<TClass, bool>> Expression() => t => t.Id.Equals(id);
    }
}
