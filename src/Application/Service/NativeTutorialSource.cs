namespace GamaEdtech.Application.Service
{
    using System.Diagnostics.CodeAnalysis;
    using System.Globalization;
    using System.Linq;
    using System.Text.Json.Nodes;

    using GamaEdtech.Application.Interface;
    using GamaEdtech.Common.Core;
    using GamaEdtech.Common.Data;
    using GamaEdtech.Common.DataAccess.UnitOfWork;
    using GamaEdtech.Common.Service;
    using GamaEdtech.Data.Dto.Taxonomy;
    using GamaEdtech.Domain.Entity;
    using GamaEdtech.Domain.Entity.Identity;
    using GamaEdtech.Domain.Enumeration;
    using GamaEdtech.Infrastructure.Interface;

    using Microsoft.AspNetCore.Http;
    using Microsoft.AspNetCore.WebUtilities;
    using Microsoft.EntityFrameworkCore;
    using Microsoft.Extensions.Logging;

    using static GamaEdtech.Common.Core.Constants;

    /// <summary>
    /// Tutorials from this backend's copy (<c>Tutorials</c>, the types copy, our users), answering the website's calls as
    /// gama-api does: a tutorial's page, the lesson tree and the tutorials search. Used only when
    /// <c>Migration:Routes:Tutorials.Website</c> says so. The author is <c>DisplayOwnerUserId</c>; the content's images are
    /// served from this backend's public storage and the HTML is sanitized. See docs/business/tutorials.md.
    /// </summary>
    public sealed partial class NativeTutorialSource(Lazy<IUnitOfWorkProvider> unitOfWorkProvider, Lazy<IHttpContextAccessor> httpContextAccessor,
        Lazy<ILogger<NativeTutorialSource>> logger, Lazy<IFileService> fileService)
        : ServiceBase<NativeTutorialSource>(unitOfWorkProvider, httpContextAccessor, logger), ITutorialSource
    {
        private const int DefaultPerPage = 10;
        private const int MaxPerPage = 50;
        private const int MaxPerPageMore = 1000;
        private const int TreeTutorialsPerTopic = 20;
        private const string PagePrefix = "درسنامه آموزشی";

        public MigrationRoute ProviderType => MigrationRoute.Native;

        public async Task<ResultData<JsonObject>> GetTutorialAsync(long id)
        {
            try
            {
                var uow = UnitOfWorkProvider.Value.CreateUnitOfWork();
                var tutorial = await uow.GetRepository<Tutorial>().GetManyQueryable(t => t.Id == id && t.Status == TutorialStatus.Confirmed).FirstOrDefaultAsync();
                if (tutorial is null)
                {
                    return NoResult<JsonObject>();
                }

                // gama-api counts a view each time a tutorial's page is fetched (here: once per cache refresh of the page).
                // A shadow comparison answers like it would but writes nothing: gama-api has counted this view already.
                if (MigrationRouter.InShadowCall || await CountViewAsync(uow, id))
                {
                    tutorial.ViewCount++;
                }

                var context = await LoadContextAsync(uow, [tutorial]);
                JsonObject data = new()
                {
                    ["id"] = Invariant(tutorial.Id),
                    ["section"] = Invariant(tutorial.CoreBoardId ?? 0),
                    ["base"] = Invariant(tutorial.CoreGradeId ?? 0),
                    ["course"] = Invariant(tutorial.CoreCourseId ?? 0),
                    ["lesson"] = Invariant(tutorial.CoreSubjectId ?? 0),
                    ["topic"] = tutorial.CoreTopicIds ?? string.Empty,
                    ["title"] = tutorial.Title,
                    ["content"] = Content(tutorial.Content),
                    ["up_date"] = Written(tutorial.LastModifyDate),
                    ["views"] = Invariant(tutorial.ViewCount),
                    ["status"] = Invariant(tutorial.Status.Value),
                };
                AddTitles(data, tutorial, context);
                data["title_url"] = PastPaperOutput.TitleUrl(tutorial.Title);
                data["pageTitle"] = (tutorial.Title ?? string.Empty).Trim();
                data["pageDescription"] = PageDescription(tutorial.Title, tutorial.Description, (string?)data["lesson_title"], (string?)data["base_title"]);
                return new(OperationResult.Succeeded) { Data = data };
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return new(OperationResult.Failed) { Errors = [new() { Message = exc.Message }] };
            }
        }

        public async Task<ResultData<JsonObject>> GetLessonTreeAsync(long subjectId)
        {
            try
            {
                var uow = UnitOfWorkProvider.Value.CreateUnitOfWork();
                var items = uow.GetRepository<LegacyTaxonomyItem, int>();
                var subject = await items.GetManyQueryable(t => t.Id == subjectId && t.Kind == LegacyTaxonomyKinds.Subject).FirstOrDefaultAsync();
                if (subject is null)
                {
                    return new(OperationResult.Failed) { Errors = [new() { Message = "wrongLessonId", Reference = "wrongLessonId" }] };
                }

                // gama-api's seasonTree: the subject's topics in list order; a season starts a group, the topics after it are
                // its chapters (a topic before any season stands alone).
                var key = subjectId.ToString(CultureInfo.InvariantCulture);
                var topics = await items.GetManyQueryable(t => t.Kind == LegacyTaxonomyKinds.Topic && !t.IsDeleted && t.Parents!.Any(p => p.ParentKey == key))
                    .OrderBy(t => t.ListOrder).ThenBy(t => t.Id).ToListAsync();
                var tutorials = await TutorialsByTopicAsync(uow, topics.Select(t => (long)t.Id));

                JsonArray list = [];
                JsonObject? season = null;
                foreach (var topic in topics)
                {
                    var node = NativeTaxonomySource.ListItem(topic);
                    JsonObject item = new() { ["id"] = node["id"]!.DeepClone(), ["title"] = node["title"]!.DeepClone(), ["season"] = (bool?)node["season"] ?? false };
                    if ((bool)item["season"]!)
                    {
                        season = item;
                        season["chapters"] = new JsonArray();
                        season["tutorialsNum"] = 0;
                        list.Add(season);
                    }
                    else
                    {
                        item["tutorials"] = Tutorials(tutorials, topic.Id);
                        if (season is not null)
                        {
                            season["chapters"]!.AsArray().Add(item);
                            season["tutorialsNum"] = (int)season["tutorialsNum"]! + item["tutorials"]!.AsArray().Count;
                        }
                        else
                        {
                            list.Add(item);
                        }
                    }
                }

                // A season without chapters lists its own tutorials, as gama-api does.
                foreach (var node in list.OfType<JsonObject>().Where(t => (bool)t["season"]! && t["chapters"]!.AsArray().Count == 0))
                {
                    _ = node.Remove("chapters");
                    _ = node.Remove("tutorialsNum");
                    node["tutorials"] = Tutorials(tutorials, int.Parse((string)node["id"]!, CultureInfo.InvariantCulture));
                }

                return new(OperationResult.Succeeded) { Data = new() { ["id"] = key, ["title"] = subject.Title, ["list"] = list } };
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return new(OperationResult.Failed) { Errors = [new() { Message = exc.Message }] };
            }
        }

        private const int RelatedTutorialCount = 3;

        /// <summary>
        /// gama-api's related tutorials (<c>Recommendation_lib::tutorials</c>) for a paper, exam or tutorial page: published
        /// tutorials of the item's grade and subject, newest update first, then of its grade only, until there are 3. Never the
        /// tutorial itself.
        /// </summary>
        public async Task<ResultData<JsonArray>> GetRelatedTutorialsAsync([NotNull] string source, long id)
        {
            try
            {
                var uow = UnitOfWorkProvider.Value.CreateUnitOfWork();
                if (await RelatedContent.FindAsync(uow, source, id) is not { } item)
                {
                    return NoResult<JsonArray>();
                }

                var self = item.Own(RelatedContent.TutorialKind);
                List<long> picked = [];
                var subject = item.Subject is > 0 ? item.Subject : null;
                while (picked.Count < RelatedTutorialCount)
                {
                    var query = uow.GetRepository<Tutorial>().GetManyQueryable(t => t.Status == TutorialStatus.Confirmed && t.Id != self && !picked.Contains(t.Id));
                    if (item.Grade > 0)
                    {
                        query = query.Where(t => t.CoreGradeId == item.Grade);
                    }

                    if (subject is not null)
                    {
                        query = query.Where(t => t.CoreSubjectId == subject);
                    }

                    picked.AddRange(await query.OrderByDescending(t => t.LastModifyDate).ThenByDescending(t => t.Id)
                        .Select(t => t.Id).Take(RelatedTutorialCount - picked.Count).ToListAsync());

                    // Not enough of the subject: the rest from the whole grade (gama-api's second round).
                    if (subject is null)
                    {
                        break;
                    }

                    subject = null;
                }

                var tutorials = await uow.GetRepository<Tutorial>().GetManyQueryable(t => picked.Contains(t.Id)).ToDictionaryAsync(t => t.Id);
                var context = await LoadContextAsync(uow, [.. tutorials.Values]);
                return new(OperationResult.Succeeded)
                {
                    Data = [.. picked.Where(tutorials.ContainsKey).Select(t => (JsonNode)SearchItem(tutorials[t], context))],
                };
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return new(OperationResult.Failed) { Errors = [new() { Message = exc.Message }] };
            }
        }

        public async Task<ResultData<JsonObject>> SearchTutorialsAsync([NotNull] string query)
        {
            try
            {
                var search = QueryHelpers.ParseQuery(query.StartsWith('?') ? query : "?" + query)
                    .ToDictionary(t => t.Key, t => (string?)t.Value.ToString(), StringComparer.Ordinal);
                var perPageLimit = search.ContainsKey("ineedmore") ? MaxPerPageMore : MaxPerPage;
                var perPage = Number(search, "perpage") is long p and > 0 ? (int)Math.Min(p, perPageLimit) : DefaultPerPage;
                var page = Number(search, "page") is long p2 and > 0 ? p2 : 1;

                var uow = UnitOfWorkProvider.Value.CreateUnitOfWork();
                var tutorials = await FilterAsync(uow, search);
                var total = await tutorials.CountAsync();
                var list = await Sorted(tutorials, search.GetValueOrDefault("sortby")).Skip((int)((page - 1) * perPage)).Take(perPage).ToListAsync();
                var context = await LoadContextAsync(uow, list);

                JsonArray items = [];
                foreach (var tutorial in list)
                {
                    items.Add(SearchItem(tutorial, context));
                }

                return new(OperationResult.Succeeded) { Data = new() { ["num"] = total, ["list"] = items } };
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return new(OperationResult.Failed) { Errors = [new() { Message = exc.Message }] };
            }
        }

        /// <summary>Published tutorials matching gama-api's search filters: a topic (with its linked topics), else the
        /// subject, else the board, course and grade (each with the topics imported into it); and, as this backend adds, a
        /// title search (the title, description or keywords contain it).</summary>
        private static async Task<IQueryable<Tutorial>> FilterAsync(IUnitOfWork uow, Dictionary<string, string?> search)
        {
            var query = uow.GetRepository<Tutorial>().GetManyQueryable(t => t.Status == TutorialStatus.Confirmed);

            if (search.GetValueOrDefault("title") is { Length: > 0 } title)
            {
                var text = title.Trim();
                query = query.Where(t => t.Title!.Contains(text) || (t.Description != null && t.Description.Contains(text))
                    || (t.Keywords != null && t.Keywords.Contains(text)));
            }

            if (Number(search, "topic") is long topicId)
            {
                List<long> topics = [topicId, .. await TopicMatching.LinkedAsync(uow, topicId)];
                return query.Where(TopicMatching.TopicIn<Tutorial>(nameof(Tutorial.CoreTopicIds), [.. topics.Select(t => t.ToString(CultureInfo.InvariantCulture))]));
            }

            var filterId = Number(search, "lesson") ?? Number(search, "base") ?? Number(search, "section");
            var imported = filterId is long id ? await TopicMatching.ImportedTopicsAsync(uow, id) : [];

            var own = query;
            if (Number(search, "lesson") is long lesson)
            {
                own = own.Where(t => t.CoreSubjectId == lesson);
            }
            else
            {
                if (Number(search, "section") is long section)
                {
                    own = own.Where(t => t.CoreBoardId == section);
                }

                if (Number(search, "course") is long course)
                {
                    own = own.Where(t => t.CoreCourseId == course);
                }

                if (Number(search, "base") is long grade)
                {
                    own = own.Where(t => t.CoreGradeId == grade);
                }
            }

            return imported.Count == 0
                ? own
                : own.Union(query.Where(TopicMatching.TopicIn<Tutorial>(nameof(Tutorial.CoreTopicIds), [.. imported.Select(t => t.ToString(CultureInfo.InvariantCulture))])));
        }

        /// <summary>gama-api's tutorial order (<c>Tutorial_model::orders</c>): newest change first unless asked otherwise.</summary>
        private static IQueryable<Tutorial> Sorted(IQueryable<Tutorial> query, string? sortBy) => sortBy switch
        {
            "idDesc" => query.OrderByDescending(t => t.Id),
            "subdateDesc" or "subdatedesc" or "subdate" => query.OrderByDescending(t => t.CreationDate).ThenByDescending(t => t.Id),
            "subdateAsc" => query.OrderBy(t => t.CreationDate).ThenBy(t => t.Id),
            "view" or "viewsDesc" => query.OrderByDescending(t => t.ViewCount).ThenByDescending(t => t.Id),
            "topic" or "topicAsc" => query.OrderBy(t => t.TopicOrder).ThenBy(t => t.Id),
            _ => query.OrderByDescending(t => t.LastModifyDate).ThenByDescending(t => t.Id),
        };

        /// <summary>Up to 20 published tutorials of each topic (gama-api's <c>getListByTopic</c>, newest change first).</summary>
        private static async Task<Dictionary<long, List<(long Id, string? Title)>>> TutorialsByTopicAsync(IUnitOfWork uow, IEnumerable<long> topicIds)
        {
            List<string> keys = [.. topicIds.Select(t => t.ToString(CultureInfo.InvariantCulture))];
            Dictionary<long, List<(long, string?)>> byTopic = [];
            if (keys.Count == 0)
            {
                return byTopic;
            }

            var rows = await uow.GetRepository<Tutorial>().GetManyQueryable(t => t.Status == TutorialStatus.Confirmed)
                .Where(TopicMatching.TopicIn<Tutorial>(nameof(Tutorial.CoreTopicIds), keys))
                .OrderByDescending(t => t.LastModifyDate).ThenByDescending(t => t.Id)
                .Select(t => new { t.Id, t.Title, t.CoreTopicIds }).ToListAsync();
            HashSet<string> wanted = [.. keys];
            foreach (var row in rows)
            {
                foreach (var topic in (row.CoreTopicIds ?? string.Empty).Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Where(wanted.Contains))
                {
                    var id = long.Parse(topic, CultureInfo.InvariantCulture);
                    if (!byTopic.TryGetValue(id, out var list))
                    {
                        byTopic[id] = list = [];
                    }

                    if (list.Count < TreeTutorialsPerTopic)
                    {
                        list.Add((row.Id, row.Title));
                    }
                }
            }

            return byTopic;
        }

        private static JsonArray Tutorials(Dictionary<long, List<(long Id, string? Title)>> byTopic, long topicId) =>
            [.. (byTopic.GetValueOrDefault(topicId) ?? []).Select(t => (JsonNode)new JsonObject
            {
                ["id"] = t.Id.ToString(CultureInfo.InvariantCulture),
                ["title"] = t.Title,
                ["title_url"] = PastPaperOutput.TitleUrl(t.Title),
            })];

        /// <summary>A tutorial in the search list (gama-api's search item).</summary>
        private JsonObject SearchItem(Tutorial tutorial, Context context)
        {
            var owner = tutorial.DisplayOwnerUserId is long ownerId ? context.Owners.GetValueOrDefault(ownerId) : null;
            JsonObject data = new()
            {
                ["id"] = Invariant(tutorial.Id),
                ["user_id"] = Invariant(tutorial.DisplayOwnerUserId),
                ["section"] = Invariant(tutorial.CoreBoardId ?? 0),
                ["base"] = Invariant(tutorial.CoreGradeId ?? 0),
                ["course"] = Invariant(tutorial.CoreCourseId ?? 0),
                ["lesson"] = Invariant(tutorial.CoreSubjectId ?? 0),
                ["topic"] = tutorial.CoreTopicIds ?? string.Empty,
                ["title"] = tutorial.Title,
                ["description"] = PastPaperOutput.Escaped(tutorial.Description),
                ["status"] = Invariant(tutorial.Status.Value),
                ["views"] = Invariant(tutorial.ViewCount),
                ["subdate"] = PastPaperOutput.Written(tutorial.CreationDate),
                ["up_date"] = Written(tutorial.LastModifyDate),
                ["first_name"] = owner?.FirstName,
                ["last_name"] = owner?.LastName,
                // Our user's avatar; none means null, and the website shows its own default picture.
                ["avatar"] = fileService.Value.GetStaticFileUrl(new() { FileId = owner?.AvatarId, ContainerType = ContainerType.User }),
            };
            AddTitles(data, tutorial, context);
            data["title_url"] = PastPaperOutput.TitleUrl(tutorial.Title);
            return data;
        }

        /// <summary>The board, grade, course and subject titles, the subject's picture and the decoded topics, as gama-api
        /// adds them to a tutorial (a missing type id adds nothing).</summary>
        private void AddTitles(JsonObject data, Tutorial tutorial, Context context)
        {
            string? Title(int? id) => id is > 0 && context.Items.TryGetValue(id.Value, out var item) ? item.Title : null;
            Put(data, "section_title", Title(tutorial.CoreBoardId));
            Put(data, "base_title", Title(tutorial.CoreGradeId));
            Put(data, "course_title", Title(tutorial.CoreCourseId));
            if (tutorial.CoreSubjectId is > 0)
            {
                data["lesson_title"] = Title(tutorial.CoreSubjectId);
                data["lesson_pic"] = context.Items.TryGetValue(tutorial.CoreSubjectId.Value, out var subject) && !string.IsNullOrWhiteSpace(subject.Picture)
                    ? fileService.Value.GetStaticFileUrl(new() { FileId = subject.Picture, ContainerType = ContainerType.SubjectPicture })
                    : null;
            }

            if (!string.IsNullOrEmpty(tutorial.CoreTopicIds))
            {
                data["topic_info"] = TopicInfo(tutorial.CoreTopicIds, context);
            }
        }

        /// <summary>gama-api's <c>topicsDecoder</c>: each topic's id, order, title (season marks removed) and page range.</summary>
        internal static JsonArray TopicInfo(string topicIds, Context context)
        {
            List<(int Order, JsonObject Node)> topics = [];
            foreach (var part in topicIds.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (!int.TryParse(part, NumberStyles.None, CultureInfo.InvariantCulture, out var id) || !context.Items.TryGetValue(id, out var item))
                {
                    continue;
                }

                var title = item.Title ?? string.Empty;
                var season = PastPaperOutput.IsSeason(item.Metadata);
                if (season || title.StartsWith("**", StringComparison.Ordinal))
                {
                    season = true;
                    title = title.Replace("**", string.Empty, StringComparison.Ordinal);
                }

                var (from, to) = PageRange(item.Metadata);
                topics.Add((item.ListOrder, new JsonObject
                {
                    ["id"] = part,
                    ["order"] = item.ListOrder.ToString(CultureInfo.InvariantCulture),
                    ["title"] = title,
                    ["season"] = season,
                    ["pages"] = new JsonObject { ["from"] = from, ["to"] = to },
                }));
            }

            return [.. topics.OrderBy(t => t.Order).Select(t => (JsonNode)t.Node)];
        }

        // gama-api's topicsDecoder: the metadata's startPage/endPage, null when the topic has none.
        private static (string? From, string? To) PageRange(string? metadata)
        {
            try
            {
                var json = string.IsNullOrWhiteSpace(metadata) ? null : JsonNode.Parse(metadata) as JsonObject;
                return (json?["startPage"]?.ToString(), json?["endPage"]?.ToString());
            }
            catch (System.Text.Json.JsonException)
            {
                return (null, null);
            }
        }

        /// <summary>gama-api's <c>pageMeta</c> description: "درسنامه آموزشی {topic} {book} شامل {headings} در گاما", the book and
        /// topic from a "book | topic" title (or the subject and grade), the headings from the "|"-separated description.</summary>
        internal static string PageDescription(string? title, string? description, string? subjectTitle, string? gradeTitle)
        {
            var parts = (title ?? string.Empty).Split('|', 2).Select(t => t.Trim()).ToArray();
            var book = parts[0];
            var topic = parts.Length > 1 ? parts[1] : string.Empty;
            if (topic.Length == 0)
            {
                topic = book;
                book = $"{LessonNumberRegex().Replace(subjectTitle ?? string.Empty, string.Empty).Trim()} {gradeTitle}".Trim();
            }
            else
            {
                book = (book.StartsWith(PagePrefix, StringComparison.Ordinal) ? book[PagePrefix.Length..] : book).Trim();
            }

            List<string> headings = [];
            foreach (var heading in TagRegex().Replace(description ?? string.Empty, string.Empty).Split('|'))
            {
                var text = LeadingNonWordRegex().Replace(SpacesRegex().Replace(heading, " "), string.Empty).Trim();
                if (text.Length > 0 && !headings.Contains(text, StringComparer.Ordinal))
                {
                    headings.Add(text);
                }
            }

            return $"{PagePrefix} {topic} {book}".Trim() + (headings.Count > 0 ? " شامل " + string.Join("، ", headings) : string.Empty) + " در گاما";
        }

        private string Content(string? content) => TutorialContent.ForPage(content, fileService.Value);

        internal sealed record Context(IReadOnlyDictionary<int, LegacyTaxonomyItem> Items, IReadOnlyDictionary<long, OwnerInfo> Owners);

        internal sealed record OwnerInfo(string? FirstName, string? LastName, string? AvatarId);

        private static async Task<Context> LoadContextAsync(IUnitOfWork uow, IReadOnlyCollection<Tutorial> tutorials)
        {
            HashSet<int> ids = [];
            foreach (var tutorial in tutorials)
            {
                ids.UnionWith(new int?[] { tutorial.CoreBoardId, tutorial.CoreGradeId, tutorial.CoreCourseId, tutorial.CoreSubjectId }.OfType<int>().Where(t => t > 0));
                ids.UnionWith((tutorial.CoreTopicIds ?? string.Empty).Split('+', StringSplitOptions.RemoveEmptyEntries)
                    .Select(t => int.TryParse(t, NumberStyles.None, CultureInfo.InvariantCulture, out var n) ? n : 0).Where(t => t > 0));
            }

            List<int> itemIds = [.. ids];
            var items = await uow.GetRepository<LegacyTaxonomyItem, int>().GetManyQueryable(t => itemIds.Contains(t.Id)).ToDictionaryAsync(t => t.Id);

            List<long> ownerIds = [.. tutorials.Select(t => t.DisplayOwnerUserId).OfType<long>().Distinct()];
            var owners = await uow.GetRepository<ApplicationUser>().GetManyQueryable(t => ownerIds.Contains(t.Id))
                .Select(t => new { t.Id, t.FirstName, t.LastName, t.AvatarId })
                .ToDictionaryAsync(t => t.Id, t => new OwnerInfo(t.FirstName, t.LastName, t.AvatarId));
            return new(items, owners);
        }

        /// <summary>Adds one view in one statement. Best effort: a failed count is logged and never fails the page.</summary>
        private async Task<bool> CountViewAsync(IUnitOfWork uow, long id)
        {
            try
            {
                _ = await uow.GetRepository<Tutorial>().GetManyQueryable(t => t.Id == id)
                    .ExecuteUpdateAsync(t => t.SetProperty(p => p.ViewCount, p => p.ViewCount + 1));
                return true;
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return false;
            }
        }

        private static ResultData<T> NoResult<T>() => new(OperationResult.Failed) { Errors = [new() { Message = "No result for this request." }] };

        private static string? Written(DateTimeOffset? date) => date is { } value ? PastPaperOutput.Written(value) : null;

        private static string? Invariant(long? value) => value?.ToString(CultureInfo.InvariantCulture);

        private static void Put(JsonObject data, string key, string? value)
        {
            if (value is not null)
            {
                data[key] = value;
            }
        }

        private static long? Number(Dictionary<string, string?> search, string key) =>
            search.TryGetValue(key, out var value) && long.TryParse(value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var number) && number != 0
                ? number
                : null;

        [System.Text.RegularExpressions.GeneratedRegex(@"\s*\(\d+\)")]
        private static partial System.Text.RegularExpressions.Regex LessonNumberRegex();

        [System.Text.RegularExpressions.GeneratedRegex("<[^>]*>")]
        private static partial System.Text.RegularExpressions.Regex TagRegex();

        [System.Text.RegularExpressions.GeneratedRegex(@"[\s ]+")]
        private static partial System.Text.RegularExpressions.Regex SpacesRegex();

        [System.Text.RegularExpressions.GeneratedRegex(@"^[^\p{L}\p{N}]+")]
        private static partial System.Text.RegularExpressions.Regex LeadingNonWordRegex();
    }
}
