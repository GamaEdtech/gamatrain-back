namespace GamaEdtech.Domain.Specification.PastPaper
{
    using System.Linq.Expressions;

    using GamaEdtech.Common.DataAccess.Specification;
    using GamaEdtech.Domain.Entity;
    using GamaEdtech.Domain.Enumeration;

    public sealed class PublishedPastPaperSpecification : SpecificationBase<PastPaper>
    {
        public override Expression<Func<PastPaper, bool>> Expression() => (t) => !t.IsDeleted && t.Status == PastPaperStatus.Confirmed;
    }
}
