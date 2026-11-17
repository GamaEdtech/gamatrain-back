namespace GamaEdtech.Application.Service
{
    using System.Globalization;
    using System.Linq;

    using GamaEdtech.Common.DataAccess.UnitOfWork;
    using GamaEdtech.Domain.Entity;

    using Microsoft.EntityFrameworkCore;

    /// <summary>
    /// gama-api's topic matching, shared by every native source that lists content by topic (past papers, tutorials): the
    /// links between topics of different boards (<c>LegacyTaxonomyRelations</c>, gama-api's <c>types_relation</c>, made by
    /// approving a topic match), a topic's linked topics, the topics imported into a board, grade or subject, and the match on
    /// a content's "+"-joined topic field.
    /// </summary>
    internal static class TopicMatching
    {
        /// <summary>Every item linked to this one, directly or through others (gama-api's <c>getFullRelationIDs</c>).</summary>
        internal static async Task<List<long>> LinkedAsync(IUnitOfWork uow, long id)
        {
            var graph = await GraphAsync(uow);
            HashSet<long> visited = [id];
            Queue<long> queue = new([id]);
            while (queue.TryDequeue(out var current))
            {
                foreach (var other in (graph.GetValueOrDefault(current) ?? []).Where(visited.Add))
                {
                    queue.Enqueue(other);
                }
            }

            _ = visited.Remove(id);
            return [.. visited];
        }

        /// <summary>The topics of other boards linked to a topic under this board, grade or subject (gama-api's
        /// <c>getImportedTopicIds</c>).</summary>
        internal static async Task<List<long>> ImportedTopicsAsync(IUnitOfWork uow, long filterId)
        {
            var graph = await GraphAsync(uow);
            if (graph.Count == 0)
            {
                return [];
            }

            var linked = graph.Keys.ToList();
            var ancestors = await AncestorsAsync(uow, linked);
            List<long> native = [.. linked.Where(t => t == filterId || ancestors.GetValueOrDefault(t)?.Contains(filterId) == true)];

            HashSet<long> imported = [];
            foreach (var id in native)
            {
                imported.UnionWith(await LinkedAsync(uow, id));
            }

            imported.ExceptWith(native);
            return [.. imported];
        }

        internal static async Task<Dictionary<long, List<long>>> GraphAsync(IUnitOfWork uow)
        {
            var edges = await uow.GetRepository<LegacyTaxonomyRelation, int>().GetManyQueryable().Select(t => new { t.FromId, t.ToId }).ToListAsync();
            Dictionary<long, List<long>> graph = [];
            foreach (var edge in edges)
            {
                Add(edge.FromId, edge.ToId);
                Add(edge.ToId, edge.FromId);
            }

            return graph;

            void Add(long from, long to)
            {
                if (!graph.TryGetValue(from, out var list))
                {
                    graph[from] = list = [];
                }

                list.Add(to);
            }
        }

        /// <summary>Each item's ancestors through its parent routes (gama-api's <c>getAncestorIds</c>: the parent field split
        /// on <c>+</c> and <c>,</c>, recursively).</summary>
        internal static async Task<Dictionary<long, HashSet<long>>> AncestorsAsync(IUnitOfWork uow, List<long> ids)
        {
            Dictionary<long, string?> parents = [];
            HashSet<long> wanted = [.. ids];
            while (wanted.Count > 0)
            {
                List<int> batch = [.. wanted.Select(t => (int)t)];
                var rows = await uow.GetRepository<LegacyTaxonomyItem, int>().GetManyQueryable(t => batch.Contains(t.Id)).Select(t => new { t.Id, t.Parent }).ToListAsync();
                foreach (var id in wanted)
                {
                    parents[id] = rows.FirstOrDefault(t => t.Id == id)?.Parent;
                }

                wanted = [.. parents.Values.SelectMany(ParentIds).Where(t => !parents.ContainsKey(t))];
            }

            Dictionary<long, HashSet<long>> result = [];
            foreach (var id in ids)
            {
                HashSet<long> ancestors = [];
                Stack<long> stack = new(ParentIds(parents.GetValueOrDefault(id)));
                while (stack.TryPop(out var current))
                {
                    if (ancestors.Add(current))
                    {
                        foreach (var next in ParentIds(parents.GetValueOrDefault(current)))
                        {
                            stack.Push(next);
                        }
                    }
                }

                result[id] = ancestors;
            }

            return result;

            static IEnumerable<long> ParentIds(string? parent) => (parent ?? string.Empty)
                .Split(['+', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(t => long.TryParse(t, NumberStyles.None, CultureInfo.InvariantCulture, out var n) ? n : 0)
                .Where(t => t > 0);
        }

        // gama-api's topic match on its "+"-joined topic field: the topic alone, first, middle or last.
        internal static System.Linq.Expressions.Expression<Func<T, bool>> TopicIn<T>(string topicField, IReadOnlyCollection<string> keys)
        {
            var paper = System.Linq.Expressions.Expression.Parameter(typeof(T), "t");
            var field = System.Linq.Expressions.Expression.Property(paper, topicField);
            System.Linq.Expressions.Expression? any = null;
            foreach (var key in keys)
            {
                foreach (var condition in new System.Linq.Expressions.Expression[]
                {
                    System.Linq.Expressions.Expression.Equal(field, System.Linq.Expressions.Expression.Constant(key)),
                    Like(field, "StartsWith", key + "+"),
                    Like(field, "Contains", "+" + key + "+"),
                    Like(field, "EndsWith", "+" + key),
                })
                {
                    any = any is null ? condition : System.Linq.Expressions.Expression.OrElse(any, condition);
                }
            }

            return System.Linq.Expressions.Expression.Lambda<Func<T, bool>>(any ?? System.Linq.Expressions.Expression.Constant(false), paper);

            static System.Linq.Expressions.Expression Like(System.Linq.Expressions.Expression field, string method, string value) =>
                System.Linq.Expressions.Expression.AndAlso(
                    System.Linq.Expressions.Expression.NotEqual(field, System.Linq.Expressions.Expression.Constant(null, typeof(string))),
                    System.Linq.Expressions.Expression.Call(field, typeof(string).GetMethod(method, [typeof(string)])!, System.Linq.Expressions.Expression.Constant(value)));
        }
    }
}
