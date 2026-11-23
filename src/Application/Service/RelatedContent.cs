namespace GamaEdtech.Application.Service
{
    using System.Linq;

    using GamaEdtech.Common.DataAccess.UnitOfWork;
    using GamaEdtech.Domain.Entity;

    using Microsoft.EntityFrameworkCore;

    /// <summary>
    /// A page's "related" rows (gama-api's <c>recommendations/related</c>): the kinds of item they are for and list, and the
    /// item itself, whose grade, subject and session month pick the rows. Each kind's rows come from its own source (papers,
    /// exams, tutorials). See docs/business/exams-and-content.md, "Related content".
    /// </summary>
    internal static class RelatedContent
    {
        public const string PaperKind = "test";
        public const string ExamKind = "exam";
        public const string TutorialKind = "tutorial";

        /// <summary>A kind by any of gama-api's names for it (<c>test</c>, <c>papers</c>, <c>exams</c>...); null for the
        /// kinds that only gama-api has (multimedia, Q&amp;A questions, blogs).</summary>
        public static string? Kind(string? name) => name switch
        {
            "test" or "tests" or "paper" or "papers" => PaperKind,
            "exam" or "exams" => ExamKind,
            "tutorial" or "tutorials" => TutorialKind,
            _ => null,
        };

        /// <summary>The page's item, or null when there is none (whatever its status: the rows only borrow its subject).</summary>
        public static async Task<Item?> FindAsync(IUnitOfWork uow, string source, long id) => Kind(source) switch
        {
            PaperKind => await uow.GetRepository<PastPaper>().GetManyQueryable(t => t.Id == id && !t.IsDeleted)
                .Select(t => new Item(PaperKind, t.Id, t.CoreGradeId, t.CoreSubjectId, t.EduMonth)).FirstOrDefaultAsync(),
            ExamKind => await uow.GetRepository<Exam>().GetManyQueryable(t => t.Id == id && !t.IsDeleted)
                .Select(t => new Item(ExamKind, t.Id, t.CoreGradeId ?? 0, t.CoreSubjectId, t.EduMonth)).FirstOrDefaultAsync(),
            TutorialKind => await uow.GetRepository<Tutorial>().GetManyQueryable(t => t.Id == id)
                .Select(t => new Item(TutorialKind, t.Id, t.CoreGradeId ?? 0, t.CoreSubjectId, null)).FirstOrDefaultAsync(),
            _ => null,
        };

        /// <summary>A page's item: its kind and id, grade (0 when it has none: no grade filter), subject and session month.</summary>
        public sealed record Item(string ItemKind, long Id, int Grade, int? Subject, byte? Month)
        {
            /// <summary>Its id when it is of <paramref name="kind"/> (a list never holds the page's own item), else 0.</summary>
            public long Own(string kind) => ItemKind == kind ? Id : 0;
        }
    }
}
