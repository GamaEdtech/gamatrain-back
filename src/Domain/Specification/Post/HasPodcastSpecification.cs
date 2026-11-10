namespace GamaEdtech.Domain.Specification.Post
{
    using System.Linq.Expressions;

    using GamaEdtech.Common.DataAccess.Specification;
    using GamaEdtech.Domain.Entity;

    /// <summary>Posts that have (or, with <paramref name="hasPodcast"/> false, don't have) a podcast file.</summary>
    public sealed class HasPodcastSpecification(bool hasPodcast) : SpecificationBase<Post>
    {
        public override Expression<Func<Post, bool>> Expression() => hasPodcast
            ? (t) => t.PodcastId != null && t.PodcastId != ""
            : (t) => t.PodcastId == null || t.PodcastId == "";
    }
}
