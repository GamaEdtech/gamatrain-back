namespace GamaEdtech.Application.Interface
{
    using System.Diagnostics.CodeAnalysis;

    using GamaEdtech.Common.Core;
    using GamaEdtech.Common.DataAccess.UnitOfWork;
    using GamaEdtech.Common.DataAnnotation;
    using GamaEdtech.Data.Dto.SiteMap;
    using GamaEdtech.Domain.Enumeration;

    [Injectable]
    public interface ISiteMapHandler
    {
        ItemType ItemType { get; }
        IQueryable<SiteMapItemDto> GetSiteMapData([NotNull] IUnitOfWork uow);

        /// <summary>The URL's last segment from <c>Path2</c>: it must be the slug the page declares as canonical, or the listed
        /// URL redirects. Slugify by default (posts, schools, profiles); the content copied from gama-api keeps gama-api's
        /// <c>title_url</c>.</summary>
        string? Slug(string? path2) => path2.Slugify();
    }
}
