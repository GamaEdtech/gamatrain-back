namespace GamaEdtech.Domain.Specification.ClassificationNodeSpecs.Criterias
{
    using GamaEdtech.Domain.Entity;

    using System.Linq.Expressions;
#pragma warning disable S101
    public class CheckClassificationNodeTitleCriteria : CriteriaSpecification<ClassificationNode>
    {
        private readonly string? title;
        private readonly IEnumerable<string>? titles;

        public CheckClassificationNodeTitleCriteria(string title)
        {
            this.title = title;
            titles = null;
        }
        public CheckClassificationNodeTitleCriteria(IEnumerable<string> titles)
        {
            this.titles = titles;
            title = null;
        }
#pragma warning disable S3358 // Disable nested ternary operation warnings for the entire file

        public override Expression<Func<ClassificationNode, bool>> ToExpression()
            => titles is null && title is not null
                ? (current => current.Title == title)
                : titles is not null && title is null
                    ? (current => titles.Any(a => a == current.Title)) : (current => true);
#pragma warning restore S3358 // Re-enable warnings if needed later in the file

    }
#pragma warning restore S101
}
