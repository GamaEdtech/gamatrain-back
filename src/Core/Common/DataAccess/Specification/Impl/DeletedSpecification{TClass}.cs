namespace GamaEdtech.Common.DataAccess.Specification.Impl
{
    using System;
    using System.Linq.Expressions;

    using GamaEdtech.Common.DataAccess.Entities;
    using GamaEdtech.Common.DataAccess.Specification;

    public sealed class DeletedSpecification<TClass>(bool deleted) : SpecificationBase<TClass>
        where TClass : class, IDeletable
    {
        private readonly bool deleted = deleted;

        public override Expression<Func<TClass, bool>> Expression() => t => t.IsDeleted == deleted;
    }
}
