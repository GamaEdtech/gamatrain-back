namespace GamaEdtech.Application.Service
{
    using System.Diagnostics.CodeAnalysis;
    using System.Linq;

    using GamaEdtech.Application.Interface;
    using GamaEdtech.Common.DataAccess.UnitOfWork;
    using GamaEdtech.Data.Dto.SiteMap;
    using GamaEdtech.Domain.Entity;
    using GamaEdtech.Domain.Enumeration;

    /// <summary>
    /// The sitemap's past papers, exams and tutorials, from this backend's copy (since 2026-11-23; until then the website served
    /// gama-api's own sitemap files for them, which never list what is made here). Each URL is the page's canonical one,
    /// <c>/{type}/{id}/{title_url}</c>, with gama-api's <c>title_url</c> (<see cref="PastPaperOutput.TitleUrl"/>), so it never
    /// redirects. See docs/architecture/cross-cutting-concerns.md, "Sitemap".
    /// </summary>
    public abstract class ContentSiteMapHandler : ISiteMapHandler
    {
        public abstract ItemType ItemType { get; }

        public abstract IQueryable<SiteMapItemDto> GetSiteMapData([NotNull] IUnitOfWork uow);

        public string? Slug(string? path2)
        {
            var slug = PastPaperOutput.TitleUrl(path2);
            return slug.Length == 0 ? null : Uri.EscapeDataString(slug);
        }
    }

    /// <summary>Published papers.</summary>
    public sealed class PastPaperSiteMapHandler : ContentSiteMapHandler
    {
        public override ItemType ItemType => ItemType.Paper;

        public override IQueryable<SiteMapItemDto> GetSiteMapData([NotNull] IUnitOfWork uow) => uow.GetRepository<PastPaper>()
            .GetManyQueryable(t => t.Status == PastPaperStatus.Confirmed && !t.IsDeleted)
            .Select(t => new SiteMapItemDto { Id = t.Id, Path1 = t.Id.ToString(), Path2 = t.Title, LastModifyDate = t.LastModifyDate ?? t.CreationDate });
    }

    /// <summary>Public exams: confirmed (1), cloned (5) or published by their teacher (7), not deleted.</summary>
    public sealed class ExamSiteMapHandler : ContentSiteMapHandler
    {
        public override ItemType ItemType => ItemType.Exam;

        public override IQueryable<SiteMapItemDto> GetSiteMapData([NotNull] IUnitOfWork uow) => uow.GetRepository<Exam>()
            .GetManyQueryable(t => (t.Status == 1 || t.Status == 5 || t.Status == 7) && !t.IsDeleted)
            .Select(t => new SiteMapItemDto { Id = t.Id, Path1 = t.Id.ToString(), Path2 = t.Title, LastModifyDate = t.LastModifyDate ?? t.CreationDate });
    }

    /// <summary>Confirmed tutorials.</summary>
    public sealed class TutorialSiteMapHandler : ContentSiteMapHandler
    {
        public override ItemType ItemType => ItemType.Tutorial;

        public override IQueryable<SiteMapItemDto> GetSiteMapData([NotNull] IUnitOfWork uow) => uow.GetRepository<Tutorial>()
            .GetManyQueryable(t => t.Status == TutorialStatus.Confirmed)
            .Select(t => new SiteMapItemDto { Id = t.Id, Path1 = t.Id.ToString(), Path2 = t.Title, LastModifyDate = t.LastModifyDate ?? t.CreationDate });
    }
}
