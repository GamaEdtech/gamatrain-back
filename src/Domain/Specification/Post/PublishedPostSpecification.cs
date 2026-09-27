namespace GamaEdtech.Domain.Specification.Post
{
    using System.Linq.Expressions;

    using GamaEdtech.Common.DataAccess.Specification;
    using GamaEdtech.Domain.Entity;
    using GamaEdtech.Domain.Enumeration;

    public sealed class PublishedPostSpecification : SpecificationBase<Post>
    {
        public override Expression<Func<Post, bool>> Expression() => (t) => t.Status == Status.Confirmed && t.PublishDate <= DateTimeOffset.UtcNow;
    }
}
