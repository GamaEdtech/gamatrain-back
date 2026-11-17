namespace GamaEdtech.Application.Service
{
    using System.Diagnostics.CodeAnalysis;
    using System.Globalization;
    using System.Linq;
    using System.Text.Json;
    using System.Text.Json.Nodes;

    using GamaEdtech.Application.Interface;
    using GamaEdtech.Common.Core;
    using GamaEdtech.Common.Data;
    using GamaEdtech.Common.DataAccess.UnitOfWork;
    using GamaEdtech.Common.Service;
    using GamaEdtech.Data.Dto.Exam;
    using GamaEdtech.Data.Dto.ExamImport;
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
    /// Past papers from this backend's copy (<c>PastPapers</c>, <c>PastPaperFiles</c>, the types copy, the base prices and
    /// our users), answering the website's three calls exactly as gama-api does: a paper's page (<c>tests/{id}</c>), the
    /// paper lists and the subject directory (<c>tests/search</c>), and the exam page's paper link. Used only when
    /// <c>Migration:Routes:PastPapers</c> (or <c>PastPapers.Website</c>) says so. The MCP reads are not native yet: their
    /// route stays <c>Legacy</c>. Reads never use <c>CoreId</c>; the owner is <c>DisplayOwnerUserId</c>. Pictures are our
    /// CDN's. See docs/business/past-papers.md, "Native paper page and lists".
    /// </summary>
    public sealed partial class NativePastPaperSource(Lazy<IUnitOfWorkProvider> unitOfWorkProvider, Lazy<IHttpContextAccessor> httpContextAccessor,
        Lazy<ILogger<NativePastPaperSource>> logger, Lazy<IFileService> fileService, Lazy<IMigrationRouter> migrationRouter,
        Lazy<Microsoft.AspNetCore.DataProtection.IDataProtectionProvider> dataProtectionProvider)
        : ServiceBase<NativePastPaperSource>(unitOfWorkProvider, httpContextAccessor, logger), IPastPaperSource
    {
        private const int DefaultPerPage = 10;
        private const int MaxPerPage = 50;
        private const int MaxPerPageMore = 1000;
        private const int InsertFileType = 6351;
        private const int AudioFileType = 6705;

        public MigrationRoute ProviderType => MigrationRoute.Native;

        public async Task<ResultData<JsonObject>> GetPaperAsync(long id)
        {
            try
            {
                var uow = UnitOfWorkProvider.Value.CreateUnitOfWork();
                var paper = await uow.GetRepository<PastPaper>()
                    .GetManyQueryable(t => t.Id == id && t.Status == PastPaperStatus.Confirmed && !t.IsDeleted, includes: t => t.Include(p => p.Files))
                    .FirstOrDefaultAsync();
                if (paper is null)
                {
                    return NoResult<JsonObject>();
                }

                // gama-api counts a view each time a paper's page is fetched (here: once per cache refresh of the page). A
                // shadow comparison answers like it would but writes nothing: gama-api has counted this view already.
                if (MigrationRouter.InShadowCall || await CountViewAsync(uow, id))
                {
                    paper.ViewCount++;
                }

                var context = await LoadContextAsync(uow, [paper]);
                var data = Detail(paper, context);
                await AddExamsAsync(data, paper.CoreExamIds);
                return new(OperationResult.Succeeded) { Data = data };
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return new(OperationResult.Failed) { Errors = [new() { Message = exc.Message }] };
            }
        }

        public async Task<ResultData<JsonObject>> SearchPapersAsync([NotNull] string query)
        {
            try
            {
                var search = QueryHelpers.ParseQuery(query.StartsWith('?') ? query : "?" + query)
                    .ToDictionary(t => t.Key, t => (string?)t.Value.ToString(), StringComparer.Ordinal);
                var directory = Flag(search, "directory");
                var isPaper = search.ContainsKey("is_paper") ? Flag(search, "is_paper") : (bool?)null;
                var uow = UnitOfWorkProvider.Value.CreateUnitOfWork();

                return directory && isPaper != true
                    ? new(OperationResult.Succeeded) { Data = await ResourcesDirectoryAsync(uow, search) }
                    : new(OperationResult.Succeeded) { Data = await ListAsync(uow, search, directory && isPaper == true) };
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return new(OperationResult.Failed) { Errors = [new() { Message = exc.Message }] };
            }
        }

        /// <summary>
        /// gama-api's related papers for a paper page (<c>Recommendation_lib::tests</c>, source <c>test</c>), from the copy:
        /// up to 2 papers that buyers of this one bought next (in the last day, for a paper refereed B or better), up to 2 A+
        /// papers of its grade, subject and type, then papers of its grade and subject within a month of its session, newest
        /// update first, dropping the month and then the subject until there are 10. Published papers only, never the paper
        /// itself or one twice. gama-api's "trend" picks are left out: they come from its homepage's trend file, not from
        /// paper data.
        /// </summary>
        public async Task<ResultData<JsonArray>> GetRelatedPapersAsync(long paperId)
        {
            try
            {
                var uow = UnitOfWorkProvider.Value.CreateUnitOfWork();
                var paper = await uow.GetRepository<PastPaper>().GetManyQueryable(t => t.Id == paperId && !t.IsDeleted)
                    .Select(t => new { t.Id, t.CoreGradeId, t.CoreSubjectId, t.EduMonth, t.CorePaperTypeId, t.RefereeScore })
                    .FirstOrDefaultAsync();
                if (paper is null)
                {
                    return NoResult<JsonArray>();
                }

                List<long> picked = [];
                if (paper.RefereeScore >= 3)
                {
                    picked.AddRange(await AlsoBoughtAsync(uow, paper.Id, paper.CoreGradeId, paper.CoreSubjectId));
                }

                var aPlus = Related(uow, paper.Id, picked).Where(t => t.RefereeScore == 5 && t.CorePaperTypeId == paper.CorePaperTypeId);
                picked.AddRange(await TakeRelatedAsync(BySession(aPlus, paper.CoreGradeId, paper.CoreSubjectId, null), 2));

                var month = paper.EduMonth;
                int? subject = paper.CoreSubjectId;
                for (var round = 1; round <= 3 && picked.Count < RelatedCount; round++)
                {
                    var wanted = RelatedCount - picked.Count;
                    var found = await TakeRelatedAsync(BySession(Related(uow, paper.Id, picked), paper.CoreGradeId, subject, month), wanted);
                    picked.AddRange(found);
                    if (found.Count < wanted)
                    {
                        // Not enough: the first round drops the month range, the second the subject.
                        if (round == 1)
                        {
                            month = null;
                        }
                        else
                        {
                            subject = null;
                        }
                    }
                }

                var papers = await uow.GetRepository<PastPaper>().GetManyQueryable(t => picked.Contains(t.Id), includes: t => t.Include(p => p.Files)).ToListAsync();
                var byId = papers.ToDictionary(t => t.Id);
                var context = await LoadContextAsync(uow, papers);
                JsonArray list = [];
                foreach (var id in picked.Where(byId.ContainsKey))
                {
                    list.Add(RelatedItem(byId[id], context));
                }

                return new(OperationResult.Succeeded) { Data = list };
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return new(OperationResult.Failed) { Errors = [new() { Message = exc.Message }] };
            }
        }

        public async Task<ResultData<ExamPaperLinkDto>> GetPaperLinkAsync(long paperId)
        {
            try
            {
                var uow = UnitOfWorkProvider.Value.CreateUnitOfWork();
                var paper = await uow.GetRepository<PastPaper>()
                    .GetManyQueryable(t => t.Id == paperId && t.Status == PastPaperStatus.Confirmed && !t.IsDeleted)
                    .Select(t => new { t.Id, t.Title, t.IsPaper })
                    .FirstOrDefaultAsync();
                return paper is null || string.IsNullOrWhiteSpace(paper.Title)
                    ? NoResult<ExamPaperLinkDto>()
                    : new(OperationResult.Succeeded) { Data = new() { Id = paper.Id, Title = paper.Title.Trim(), Slug = PastPaperOutput.TitleUrl(paper.Title), IsPastPaper = paper.IsPaper } };
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return new(OperationResult.Failed) { Errors = [new() { Message = exc.Message }] };
            }
        }

        // The MCP import's reads stay on gama-api (Migration:Routes:PastPapers.Mcp is Legacy) until they are built here.
        public Task<ResultData<IEnumerable<ExamImportPastPaperDto>>> GetPastPapersAsync([NotNull] ExamImportPastPapersRequestDto requestDto) => NotNativeAsync<IEnumerable<ExamImportPastPaperDto>>();

        public Task<ResultData<IEnumerable<ExamImportPastPaperDto>>> SearchPastPapersAsync([NotNull] ExamImportPastPapersRequestDto requestDto) => NotNativeAsync<IEnumerable<ExamImportPastPaperDto>>();

        public Task<ResultData<ExamImportPastPaperDto>> GetPastPaperAsync([NotNull] ExamImportRequestDto requestDto) => NotNativeAsync<ExamImportPastPaperDto>();

        public Task<ResultData<Uri>> GetPastPaperFileUrlAsync([NotNull] ExamImportPaperFileRequestDto requestDto) => NotNativeAsync<Uri>();

        private static Task<ResultData<T>> NotNativeAsync<T>() =>
            Task.FromResult(new ResultData<T>(OperationResult.NotValid) { Errors = [new() { Message = "The MCP past paper reads are not native yet; keep Migration:Routes:PastPapers.Mcp on Legacy." }] });

        /// <summary>Adds one view to the paper in one statement (no read-modify-write race). Best effort: a failed count is
        /// logged and never fails the page.</summary>
        private async Task<bool> CountViewAsync(IUnitOfWork uow, long id)
        {
            try
            {
                _ = await uow.GetRepository<PastPaper>().GetManyQueryable(t => t.Id == id)
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

        // ---- Lists (tests/search) ----

        private async Task<JsonObject> ListAsync(IUnitOfWork uow, Dictionary<string, string?> search, bool paperDirectory)
        {
            var perPageLimit = Flag(search, "ineedmore") ? MaxPerPageMore : MaxPerPage;
            var perPage = Number(search, "perpage") is long p and > 0 ? (int)Math.Min(p, perPageLimit) : DefaultPerPage;
            var page = Number(search, "page") is long p2 and > 0 ? p2 : 1;
            var offset = Number(search, "ipp") ?? ((page - 1) * perPage);

            var query = await FilterAsync(uow, search);
            var total = await query.CountAsync();

            // Ordered in memory on the matching ids' sort keys: the default order uses the paper type's and variant's titles.
            var keys = await query.Select(t => new SortKey(t.Id, t.EduYear, t.EduMonth, t.CorePaperTypeId, t.Variant, t.IsPaper, t.RefereeScore, t.ViewCount,
                t.DownloadCount, t.LastModifyDate)).ToListAsync();
            var titles = await TitlesAsync(uow, [.. keys.Where(t => t.IsPaper).SelectMany(t => new long?[] { t.PaperType, t.Variant }).OfType<long>()]);
            var pageIds = Sort(keys, SortBy(search), titles).Skip((int)Math.Max(offset, 0)).Take(perPage).Select(t => t.Id).ToList();

            var papers = await uow.GetRepository<PastPaper>().GetManyQueryable(t => pageIds.Contains(t.Id), includes: t => t.Include(p => p.Files)).ToListAsync();
            var byId = papers.ToDictionary(t => t.Id);
            var ordered = pageIds.Select(t => byId[t]).ToList();
            var context = await LoadContextAsync(uow, ordered);

            JsonArray list = [];
            foreach (var paper in ordered)
            {
                list.Add(paperDirectory ? DirectoryItem(paper, context) : SearchItem(paper, context));
            }

            return new() { ["num"] = total, ["list"] = list };
        }

        /// <summary>The subject directory's resources (gama-api's <c>notPaperDirectory</c>): every non-paper classification
        /// with its first 50 items of this filter.</summary>
        private async Task<JsonObject> ResourcesDirectoryAsync(IUnitOfWork uow, Dictionary<string, string?> search)
        {
            var lesson = Number(search, "lesson");
            var classifications = await uow.GetRepository<LegacyTaxonomyItem, int>()
                .GetManyQueryable(t => t.Kind == LegacyTaxonomyKinds.Classification && !t.IsDeleted)
                .OrderBy(t => t.ListOrder).ThenBy(t => t.Id)
                .Select(t => new { t.Id, t.Title, t.IsPaper })
                .ToListAsync();

            JsonArray list = [];
            foreach (var classification in classifications.Where(t => t.IsPaper != true))
            {
                Dictionary<string, string?> groupSearch = new(search, StringComparer.Ordinal)
                {
                    ["test_type"] = Invariant(classification.Id),
                    ["is_paper"] = "0",
                };
                var query = await FilterAsync(uow, groupSearch);
                var keys = await query.Select(t => new SortKey(t.Id, t.EduYear, t.EduMonth, t.CorePaperTypeId, t.Variant, t.IsPaper, t.RefereeScore, t.ViewCount,
                    t.DownloadCount, t.LastModifyDate)).ToListAsync();
                var ids = Sort(keys, SortBy(groupSearch), new Dictionary<long, string>()).Take(MaxPerPage).Select(t => t.Id).ToList();
                var items = await uow.GetRepository<PastPaper>().GetManyQueryable(t => ids.Contains(t.Id)).Select(t => new { t.Id, t.Title }).ToListAsync();
                var byId = items.ToDictionary(t => t.Id);

                JsonArray groupItems = [];
                foreach (var id in ids)
                {
                    var item = byId[id];
                    JsonObject node = new() { ["id"] = Invariant(item.Id) };
                    if (!string.IsNullOrEmpty(item.Title))
                    {
                        node["title"] = item.Title;
                        node["title_url"] = PastPaperOutput.TitleUrl(item.Title);
                    }

                    groupItems.Add(node);
                }

                list.Add(new JsonObject { ["type_id"] = Invariant(classification.Id), ["title"] = classification.Title, ["items"] = groupItems });
            }

            string? lessonTitle = null;
            string? lessonPic = null;
            if (lesson is long subjectId)
            {
                var subject = await uow.GetRepository<LegacyTaxonomyItem, int>().GetManyQueryable(t => t.Id == subjectId).Select(t => new { t.Title, t.Picture }).FirstOrDefaultAsync();
                lessonTitle = subject?.Title;
                lessonPic = SubjectPictureUrl(subject?.Picture);
            }

            return new()
            {
                ["lesson"] = search.GetValueOrDefault("lesson"),
                ["lesson_title"] = lessonTitle,
                ["lesson_pic"] = lessonPic,
                ["num"] = 0,
                ["list"] = list,
            };
        }

        /// <summary>gama-api's paper filters (<c>Test_model::conditions</c> for a public search): published and not deleted,
        /// then each filter given. A board, grade, subject or topic also matches the topics linked to it from other boards.</summary>
        private static async Task<IQueryable<PastPaper>> FilterAsync(IUnitOfWork uow, Dictionary<string, string?> search)
        {
            var query = uow.GetRepository<PastPaper>().GetManyQueryable(t => t.Status == PastPaperStatus.Confirmed && !t.IsDeleted);

            if (Text(search, "title") is { } title)
            {
                query = query.Where(t => t.Title!.Contains(title));
            }

            if (Number(search, "uid") is long uid)
            {
                query = query.Where(t => t.CoreOwnerId == uid);
            }

            if (Number(search, "teacher") is long teacher)
            {
                query = query.Where(t => t.CoreTeacherId == teacher);
            }

            // The deepest place filter (topic, else subject, else grade, else board) brings in its linked topics.
            var topic = Number(search, "topic");
            if (topic is long topicId)
            {
                List<long> topics = [topicId, .. await TopicMatching.LinkedAsync(uow, topicId)];
                List<string> keys = [.. topics.Select(t => t.ToString(CultureInfo.InvariantCulture))];
                query = query.Where(TopicMatching.TopicIn<PastPaper>(nameof(PastPaper.CoreTopicIds), keys));
            }
            else
            {
                var filterId = Number(search, "lesson") ?? Number(search, "base") ?? Number(search, "section");
                var imported = filterId is long id ? await TopicMatching.ImportedTopicsAsync(uow, id) : [];
                List<string?> importedKeys = [.. imported.Select(t => t.ToString(CultureInfo.InvariantCulture))];

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

                query = importedKeys.Count > 0 ? own.Union(query.Where(t => importedKeys.Contains(t.CoreTopicIds!))) : own;
            }

            if (Number(search, "test_type") is long paperType)
            {
                query = query.Where(t => t.CorePaperTypeId == paperType);
            }

            if (Number(search, "level") is long level)
            {
                query = query.Where(t => t.Level == level);
            }

            if (Number(search, "q_file_pages") is long pages)
            {
                query = query.Where(t => t.Files!.Any(f => f.Kind == PastPaperFileKind.Question && f.PageCount == pages));
            }

            if (Number(search, "free") == 1)
            {
                query = query.Where(t => t.RefereeScore <= 1);
            }

            if (Number(search, "pdf") == 1)
            {
                query = query.Where(t => t.Files!.Any(f => f.Kind == PastPaperFileKind.Question));
            }

            if (Number(search, "word") == 1)
            {
                query = query.Where(t => t.Files!.Any(f => f.Kind == PastPaperFileKind.QuestionWord));
            }

            if (Number(search, "a_file") == 1)
            {
                query = query.Where(t => (t.AnswerType != null && t.AnswerType != 0) || t.Files!.Any(f => f.Kind == PastPaperFileKind.Answer));
            }

            if (Number(search, "edu_year") is long year)
            {
                query = query.Where(t => t.EduYear == year);
            }

            if (Number(search, "edu_month") is long month)
            {
                query = query.Where(t => t.EduMonth == month);
            }

            if (Number(search, "edu_session") is long session)
            {
                query = query.Where(t => t.EduSession == session);
            }

            if (Number(search, "variant") is long variant)
            {
                query = query.Where(t => t.Variant == variant);
            }

            if (Number(search, "holding_level") is long holding)
            {
                query = query.Where(t => t.HoldingLevel == holding);
            }

            if (Number(search, "school") is long school)
            {
                query = query.Where(t => t.CoreSchoolId == school);
            }
            else if (Text(search, "state") is { } state)
            {
                query = query.Where(t => t.State == state);
            }
            else if (Number(search, "area") is long area)
            {
                query = query.Where(t => t.CoreAreaId == area);
            }

            if (search.ContainsKey("is_paper"))
            {
                var paperOnly = Flag(search, "is_paper");
                query = query.Where(t => t.IsPaper == paperOnly);
            }

            return query;
        }

        // ---- gama-api's order (Test_model::orders) ----

        private sealed record SortKey(long Id, short? EduYear, byte? EduMonth, int? PaperType, short? Variant, bool IsPaper, byte RefereeScore, int ViewCount,
            int DownloadCount, DateTimeOffset? LastModifyDate);

        private static string SortBy(Dictionary<string, string?> search) => search.GetValueOrDefault("sortby") switch
        {
            "score" or "2" => "scoreDlDesc",
            "view" => "viewsDesc",
            "update" => "updateDesc",
            "best" => search.ContainsKey("base") && Number(search, "base") is not null ? "scoreTopUpdateDesc" : "scoreHighUpdateDesc",
            "eduYearMonthDesc" or null or "" => "eduYearMonthDesc",
            _ => "scoreHighUpdateDesc",
        };

        private static IEnumerable<SortKey> Sort(List<SortKey> keys, string sortBy, IReadOnlyDictionary<long, string> titles) => sortBy switch
        {
            "scoreDlDesc" => keys.OrderByDescending(t => t.RefereeScore).ThenByDescending(t => t.DownloadCount),
            "viewsDesc" => keys.OrderByDescending(t => t.ViewCount).ThenByDescending(t => t.DownloadCount),
            "updateDesc" => keys.OrderByDescending(t => t.LastModifyDate),
            "scoreTopUpdateDesc" => keys.OrderByDescending(t => t.RefereeScore == 5).ThenByDescending(t => t.RefereeScore == 4).ThenByDescending(t => t.LastModifyDate),
            "scoreHighUpdateDesc" => keys.OrderByDescending(t => t.RefereeScore == 4).ThenByDescending(t => t.LastModifyDate),

            // Year and month newest first; then (papers only) the paper number, the variant as a number, then its title; id last.
            _ => keys.OrderByDescending(t => t.EduYear ?? -1).ThenByDescending(t => t.EduMonth ?? -1)
                .ThenBy(t => PaperNumber(t, titles), NullsFirst.Instance)
                .ThenBy(t => VariantNumber(t, titles), NullsFirst.Instance)
                .ThenBy(t => t.IsPaper && t.Variant is short v ? titles.GetValueOrDefault(v) : null, StringComparer.OrdinalIgnoreCase)
                .ThenByDescending(t => t.Id),
        };

        // MySQL: CAST(REGEXP_SUBSTR(type title, '[0-9]+') AS UNSIGNED), NULL when the row is not a paper or has no number.
        private static long? PaperNumber(SortKey key, IReadOnlyDictionary<long, string> titles) =>
            key.IsPaper && key.PaperType is int type && titles.TryGetValue(type, out var title)
            && DigitsRegex().Match(title) is { Success: true } match
            && long.TryParse(match.Value, NumberStyles.None, CultureInfo.InvariantCulture, out var number)
                ? number
                : null;

        // MySQL: CAST(variant title AS UNSIGNED): its leading digits, 0 when there are none; NULL without a variant.
        private static long? VariantNumber(SortKey key, IReadOnlyDictionary<long, string> titles)
        {
            if (!key.IsPaper || key.Variant is not short variant || !titles.TryGetValue(variant, out var title))
            {
                return null;
            }

            var digits = new string([.. title.TrimStart().TakeWhile(char.IsAsciiDigit)]);
            return long.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out var number) ? number : 0;
        }

        private sealed class NullsFirst : IComparer<long?>
        {
            public static readonly NullsFirst Instance = new();

            public int Compare(long? x, long? y) => (x, y) switch
            {
                (null, null) => 0,
                (null, _) => -1,
                (_, null) => 1,
                _ => x.Value.CompareTo(y.Value),
            };
        }

        // ---- Related papers ----

        private const int RelatedCount = 10;

        // Published papers other than the paper itself and those already picked.
        private static IQueryable<PastPaper> Related(IUnitOfWork uow, long paperId, List<long> picked) => uow.GetRepository<PastPaper>()
            .GetManyQueryable(t => t.Status == PastPaperStatus.Confirmed && !t.IsDeleted && t.Id != paperId && !picked.Contains(t.Id));

        // gama-api's related filters: the grade and subject (when set), and the session's month give or take one.
        private static IQueryable<PastPaper> BySession(IQueryable<PastPaper> query, int grade, int? subject, byte? month)
        {
            if (grade > 0)
            {
                query = query.Where(t => t.CoreGradeId == grade);
            }

            if (subject is > 0)
            {
                query = query.Where(t => t.CoreSubjectId == subject);
            }

            if (month is > 0)
            {
                var from = month - 1;
                var to = month + 1;
                query = query.Where(t => t.EduMonth >= from && t.EduMonth <= to);
            }

            return query;
        }

        // gama-api's "updateDesc": the last update first (id breaks a tie, so pages are stable).
        private static Task<List<long>> TakeRelatedAsync(IQueryable<PastPaper> query, int count) =>
            query.OrderByDescending(t => t.LastModifyDate).ThenByDescending(t => t.Id).Select(t => t.Id).Take(count).ToListAsync();

        /// <summary>gama-api's "smart" picks: what this paper's buyers of the last day bought after it, of the same grade and
        /// subject, most bought first; at most 2 published ones, newest update first.</summary>
        private static async Task<List<long>> AlsoBoughtAsync(IUnitOfWork uow, long paperId, int grade, int subject)
        {
            var since = DateTimeOffset.UtcNow.AddDays(-1);
            var purchases = uow.GetRepository<ContentPurchase>().GetManyQueryable(t => t.ContentType == PurchasableContentType.PastPaper);
            var buyers = await purchases.Where(t => t.ContentId == paperId && t.CreationDate > since).Select(t => new { t.UserId, t.CreationDate }).ToListAsync();
            if (buyers.Count == 0)
            {
                return [];
            }

            List<long> buyerIds = [.. buyers.Select(t => t.UserId).Distinct()];
            var later = await purchases.Where(t => buyerIds.Contains(t.UserId) && t.ContentId != paperId)
                .Select(t => new { t.UserId, t.ContentId, t.CreationDate }).ToListAsync();
            var bought = later.Where(t => buyers.Any(b => b.UserId == t.UserId && t.CreationDate > b.CreationDate)).ToList();
            List<long> candidates = [.. bought.Select(t => t.ContentId).Distinct()];
            var sameSubject = (await uow.GetRepository<PastPaper>().GetManyQueryable(t => candidates.Contains(t.Id) && t.CoreGradeId == grade && t.CoreSubjectId == subject)
                .Select(t => t.Id).ToListAsync()).ToHashSet();
            List<long> top = [.. bought.Where(t => sameSubject.Contains(t.ContentId)).GroupBy(t => t.ContentId)
                .OrderByDescending(t => t.Count()).Take(2).Select(t => t.Key)];
            return top.Count == 0 ? [] : await TakeRelatedAsync(Related(uow, paperId, []).Where(t => top.Contains(t.Id)), 2);
        }

        /// <summary>A related paper as gama-api lists it (its <c>getRelatedList</c> columns), the owner and pictures ours.</summary>
        private JsonObject RelatedItem(PastPaper paper, Context context)
        {
            var owner = paper.DisplayOwnerUserId is long id ? context.Owners.GetValueOrDefault(id) : null;
            var thumbnail = ThumbnailUrl(paper.Thumbnail);
            return new()
            {
                ["id"] = Invariant(paper.Id),
                ["title"] = paper.Title,
                ["thumb_pic"] = thumbnail,
                ["test_type"] = Invariant(paper.CorePaperTypeId ?? 0),
                ["edu_month"] = Invariant(paper.EduMonth),
                ["q_file_pages"] = Invariant(paper.Files?.FirstOrDefault(t => t.Kind == PastPaperFileKind.Question)?.PageCount),
                ["referee_score"] = Invariant(paper.RefereeScore),
                ["first_name"] = owner?.FirstName,
                ["last_name"] = owner?.LastName,
                ["avatar"] = fileService.Value.GetStaticFileUrl(new() { FileId = owner?.AvatarId, ContainerType = ContainerType.User }),
                ["title_url"] = PastPaperOutput.TitleUrl(paper.Title),
                ["preview"] = thumbnail,
            };
        }

        // ---- What every answer needs: lookup items, owners, prices ----

        private sealed record Context(IReadOnlyDictionary<long, LegacyTaxonomyItem> Items, IReadOnlyDictionary<long, OwnerInfo> Owners,
            IReadOnlyDictionary<string, int> BasePrices);

        private sealed record OwnerInfo(string? FirstName, string? LastName, GenderType? Gender, string? AvatarId, string? Handle);

        private static async Task<Context> LoadContextAsync(IUnitOfWork uow, IReadOnlyCollection<PastPaper> papers)
        {
            HashSet<long> ids = [];
            foreach (var paper in papers)
            {
                ids.UnionWith(new long?[] { paper.CoreBoardId, paper.CoreGradeId, paper.CoreSubjectId, paper.CoreCourseId, paper.CorePaperTypeId, paper.Variant, paper.CoreAreaId }
                    .OfType<long>().Where(t => t > 0));
                ids.UnionWith(TopicIds(paper.CoreTopicIds));
                ids.UnionWith(paper.Files?.Where(t => t.CoreTypeId is not null).Select(t => (long)t.CoreTypeId!.Value) ?? []);
            }

            List<int> itemIds = [.. ids.Select(t => (int)t)];
            var items = await uow.GetRepository<LegacyTaxonomyItem, int>().GetManyQueryable(t => itemIds.Contains(t.Id)).ToDictionaryAsync(t => (long)t.Id);

            List<long> ownerIds = [.. papers.Select(t => t.DisplayOwnerUserId).OfType<long>().Distinct()];
            var owners = await uow.GetRepository<ApplicationUser>().GetManyQueryable(t => ownerIds.Contains(t.Id))
                .Select(t => new { t.Id, t.FirstName, t.LastName, t.Gender, t.AvatarId, t.Handle })
                .ToDictionaryAsync(t => t.Id, t => new OwnerInfo(t.FirstName, t.LastName, t.Gender, t.AvatarId, t.Handle));

            var prices = await uow.GetRepository<PastPaperFilePrice>().GetManyQueryable(t => t.CoreType == 1).OrderBy(t => t.Id)
                .Select(t => new { t.Extensions, t.BasePrice }).ToListAsync();
            return new(items, owners, PastPaperPricing.BasePrices(prices.Select(t => (t.Extensions, t.BasePrice))));
        }

        private static async Task<Dictionary<long, string>> TitlesAsync(IUnitOfWork uow, IReadOnlyCollection<long> ids)
        {
            List<int> itemIds = [.. ids.Distinct().Select(t => (int)t)];
            return itemIds.Count == 0
                ? []
                : await uow.GetRepository<LegacyTaxonomyItem, int>().GetManyQueryable(t => itemIds.Contains(t.Id)).ToDictionaryAsync(t => (long)t.Id, t => t.Title ?? string.Empty);
        }


        // ---- Answers ----

        /// <summary>A paper's page, as gama-api's <c>tests/{id}</c> answers an anonymous visitor.</summary>
        private JsonObject Detail(PastPaper paper, Context context)
        {
            var question = File(paper, PastPaperFileKind.Question);
            var answer = File(paper, PastPaperFileKind.Answer);
            var word = File(paper, PastPaperFileKind.QuestionWord);
            JsonObject data = new()
            {
                ["check_bit"] = "0",
                ["id"] = Invariant(paper.Id),
                ["teacher_id"] = Invariant(paper.CoreTeacherId),
                ["user_"] = Invariant(paper.CoreOwnerId),
                ["section"] = Invariant(paper.CoreBoardId),
                ["base"] = Invariant(paper.CoreGradeId),
                ["course"] = Invariant(paper.CoreCourseId ?? 0),
                ["lesson"] = Invariant(paper.CoreSubjectId),
                ["topic"] = paper.CoreTopicIds ?? string.Empty,
                ["level"] = Invariant(paper.Level),
                ["test_type"] = Invariant(paper.CorePaperTypeId ?? 0),
                ["answer_type"] = Invariant(paper.AnswerType),
                ["holding_level"] = Invariant(paper.HoldingLevel),
                ["state"] = paper.State,
                ["area"] = Invariant(paper.CoreAreaId),
                ["school"] = Invariant(paper.CoreSchoolId),
                ["edu_year"] = Invariant(paper.EduYear),
                ["edu_month"] = Invariant(paper.EduMonth),
                ["edu_session"] = Invariant(paper.EduSession),
                ["variant"] = Invariant(paper.Variant),
                ["title"] = paper.Title,
                ["description"] = PastPaperOutput.Escaped(paper.Description),
                ["q_file"] = question is not null,
                ["q_file_pages"] = Invariant(question?.PageCount),
                ["a_file_pages"] = Invariant(answer?.PageCount),
                ["q_file_word"] = word is not null,
                ["a_file"] = answer is not null,
                ["exam_id"] = paper.CoreExamIds,
                ["status"] = Invariant(paper.Status.Value),
                ["msg"] = paper.ReviewNote,
                ["thumb_pic"] = ThumbnailUrl(paper.Thumbnail),
                ["previews"] = null,
                ["downloads"] = Invariant(paper.DownloadCount),
                ["views"] = Invariant(paper.ViewCount),
                ["free_aggrement"] = paper.FreeAgreement ? "1" : "0",
                ["subdate"] = PastPaperOutput.Written(paper.CreationDate),
                ["up_date"] = paper.LastModifyDate is { } modified ? PastPaperOutput.Written(modified) : null,
                ["price_multiplier"] = PastPaperOutput.Number(paper.PriceMultiplier),
                ["ref_score"] = Invariant(paper.RefereeScore),
            };
            AddOwner(data, paper, context);
            data["bookmark"] = false;
            data["ownerIdentity"] = OwnerIdentity(paper, context);

            AddTitles(data, paper, context, detail: true);
            data["answer_type_title"] = paper.AnswerType switch { 1 => "پاسخنامه کلیدی", 2 => "پاسخنامه تشریحی", _ => "پاسخنامه" };
            if (PastPaperOutput.MonthTitle(paper.EduMonth) is { } monthTitle)
            {
                data["edu_month_title"] = monthTitle;
            }

            data["title_url"] = PastPaperOutput.TitleUrl(paper.Title);
            data["uid"] = false;
            data["admin"] = false;
            data["referee"] = false;
            data["refereeAccess"] = false;
            data["teacher"] = false;
            data["owner"] = false;
            data["files"] = Files(paper, context);
            data["singleSell"] = false;
            return data;
        }

        /// <summary>A paper in a normal list (gama-api's <c>tests/search</c> item).</summary>
        private JsonObject SearchItem(PastPaper paper, Context context)
        {
            JsonObject data = new()
            {
                ["id"] = Invariant(paper.Id),
                ["user_id"] = Invariant(paper.CoreOwnerId),
                ["teacher_id"] = Invariant(paper.CoreTeacherId),
                ["section"] = Invariant(paper.CoreBoardId),
                ["course"] = Invariant(paper.CoreCourseId ?? 0),
                ["base"] = Invariant(paper.CoreGradeId),
                ["lesson"] = Invariant(paper.CoreSubjectId),
                ["test_type"] = Invariant(paper.CorePaperTypeId ?? 0),
                ["edu_year"] = Invariant(paper.EduYear),
                ["edu_month"] = Invariant(paper.EduMonth),
                ["edu_session"] = Invariant(paper.EduSession),
                ["variant"] = Invariant(paper.Variant),
                ["title"] = paper.Title,
                ["description"] = PastPaperOutput.Escaped(paper.Description),
                ["level"] = Invariant(paper.Level),
                ["downloads"] = Invariant(paper.DownloadCount),
                ["views"] = Invariant(paper.ViewCount),
                ["subdate"] = PastPaperOutput.Written(paper.CreationDate),
                ["up_date"] = paper.LastModifyDate is { } modified ? PastPaperOutput.Written(modified) : null,
                ["referee_score"] = Invariant(paper.RefereeScore),
                ["q_file"] = File(paper, PastPaperFileKind.Question) is not null,
                ["q_file_word"] = File(paper, PastPaperFileKind.QuestionWord) is not null,
                ["a_file"] = File(paper, PastPaperFileKind.Answer) is not null,
            };
            AddOwner(data, paper, context);
            data["ownerIdentity"] = OwnerIdentity(paper, context);
            AddTitles(data, paper, context, detail: false);
            data["title_url"] = PastPaperOutput.TitleUrl(paper.Title);
            data["identity"] = OwnerIdentity(paper, context);
            return data;
        }

        /// <summary>A paper in the subject directory (gama-api's <c>tests/search</c> with <c>directory</c> and <c>is_paper</c>).</summary>
        private JsonObject DirectoryItem(PastPaper paper, Context context)
        {
            var question = File(paper, PastPaperFileKind.Question);
            var answer = File(paper, PastPaperFileKind.Answer);
            JsonObject data = new()
            {
                ["id"] = Invariant(paper.Id),
                ["test_type"] = Invariant(paper.CorePaperTypeId ?? 0),
                ["edu_year"] = Invariant(paper.EduYear),
                ["edu_month"] = Invariant(paper.EduMonth),
                ["edu_session"] = Invariant(paper.EduSession),
                ["variant"] = Invariant(paper.Variant),
                ["title"] = paper.Title,
                ["q_file"] = question is not null,
                ["q_file_word"] = File(paper, PastPaperFileKind.QuestionWord) is not null,
                ["a_file"] = answer is not null,
                ["exam_id"] = paper.CoreExamIds,
                ["q_file_pages"] = Invariant(question?.PageCount),
                ["a_file_pages"] = Invariant(answer?.PageCount),
                ["ref_score"] = Invariant(paper.RefereeScore),
                ["price_multiplier"] = PastPaperOutput.Number(paper.PriceMultiplier),
                ["ownerIdentity"] = null,
            };
            AddTitles(data, paper, context, detail: false, directory: true);
            data["title_url"] = PastPaperOutput.TitleUrl(paper.Title);
            var files = Files(paper, context);
            files["extra"] ??= new JsonArray();
            data["files"] = files;

            var extras = Extras(paper);
            var insert = extras.FirstOrDefault(t => t.CoreTypeId == InsertFileType);
            var audio = extras.FirstOrDefault(t => t.CoreTypeId == AudioFileType);
            data["in_file"] = insert is not null;
            data["in_file_id"] = IdOrFalse(insert?.CoreExtraFileId);
            data["sf_file"] = audio is not null;
            data["sf_file_id"] = IdOrFalse(audio?.CoreExtraFileId);
            data["identity"] = null;
            return data;
        }

        /// <summary>The owner shown: our user (<c>DisplayOwnerUserId</c>). The username is never published (ours is a sign-in
        /// name), so it is gama-api's "none" (<c>0</c>); the avatar is our user's, or null.</summary>
        private void AddOwner(JsonObject data, PastPaper paper, Context context)
        {
            var owner = paper.DisplayOwnerUserId is long id ? context.Owners.GetValueOrDefault(id) : null;
            string? sex = null;
            if (owner?.Gender == GenderType.Male)
            {
                sex = "1";
            }
            else if (owner?.Gender == GenderType.Female)
            {
                sex = "2";
            }

            data["username"] = "0";
            data["first_name"] = owner?.FirstName;
            data["last_name"] = owner?.LastName;
            data["sex"] = sex;
            // Our user's avatar; none means null, and the website shows its own default picture (as for any user here).
            data["avatar"] = fileService.Value.GetStaticFileUrl(new() { FileId = owner?.AvatarId, ContainerType = ContainerType.User });
        }

        /// <summary>gama-api's <c>ownerIdentity</c> is "@" and the owner's public handle; ours is our user's own
        /// <see cref="ApplicationUser.Handle"/>, and none (the website then shows nothing) when they have not chosen one.</summary>
        private static string? OwnerIdentity(PastPaper paper, Context context) =>
            paper.DisplayOwnerUserId is long id && context.Owners.GetValueOrDefault(id)?.Handle is { Length: > 0 } handle ? $"@{handle}" : null;

        /// <summary>
        /// gama-api's page lists the exams built from the paper with their status (the website links the published one) in
        /// place of the raw <c>exam_id</c>; with none found, <c>exam_id</c> stays as written. Exams are not moved here: their
        /// status is read from wherever <c>Migration:Routes:Exams</c> serves them (gama-api), and an exam that cannot be read is
        /// left out, as gama-api leaves out one that does not exist.
        /// </summary>
        private async Task AddExamsAsync(JsonObject data, string? examIds)
        {
            JsonArray exams = [];
            foreach (var examId in ExamIds(examIds))
            {
                var exam = await migrationRouter.Value.RunAsync(MigrationDomain.Exams, MigrationFeatures.Website,
                    (IExamSource source) => source.GetExamDetailsAsync(new() { ExamId = examId, SecretKey = null }));
                if (exam.OperationResult is OperationResult.Succeeded && exam.Data?["status"] is JsonNode status)
                {
                    exams.Add(new JsonObject { ["id"] = Invariant(examId), ["status"] = status.ToString() });
                }
            }

            if (exams.Count > 0)
            {
                data["exams"] = exams;
                _ = data.Remove("exam_id");
            }
        }

        // gama-api's exam_id: a JSON list of ids ("[2527]", ids as numbers or strings).
        private static List<long> ExamIds(string? examIds)
        {
            if (string.IsNullOrWhiteSpace(examIds))
            {
                return [];
            }

            try
            {
                return JsonNode.Parse(examIds) is JsonArray ids
                    ? [.. ids.Select(t => long.TryParse(t?.ToString(), NumberStyles.None, CultureInfo.InvariantCulture, out var id) ? id : 0).Where(t => t > 0)]
                    : [];
            }
            catch (JsonException)
            {
                return [];
            }
        }

        private void AddTitles(JsonObject data, PastPaper paper, Context context, bool detail, bool directory = false)
        {
            string? Title(long? id) => id is > 0 && context.Items.TryGetValue(id.Value, out var item) ? item.Title : null;

            if (!directory)
            {
                Put(data, "section_title", Title(paper.CoreBoardId));
                Put(data, "base_title", Title(paper.CoreGradeId));
                Put(data, "course_title", Title(paper.CoreCourseId));
                Put(data, "lesson_title", Title(paper.CoreSubjectId));
                data["lesson_pic"] = context.Items.TryGetValue(paper.CoreSubjectId, out var subject) ? SubjectPictureUrl(subject.Picture) : null;
                // Only the page decodes the topics; gama-api's list items carry the raw topic field alone.
                if (detail)
                {
                    var topics = Topics(paper.CoreTopicIds, context);
                    if (topics.Count > 0)
                    {
                        data["topics"] = topics;
                    }
                }
            }

            if (paper.CorePaperTypeId is int paperType and > 0)
            {
                Put(data, "test_type_title", Title(paperType));
                data["is_paper"] = context.Items.TryGetValue(paperType, out var type) && type.IsPaper == true;
            }

            Put(data, "area_title", Title(paper.CoreAreaId));
            Put(data, "variant_title", Title(paper.Variant));
        }

        /// <summary>gama-api's <c>topicsDecoder</c>: each topic's id, order, title (season marks removed) and page range,
        /// sorted by order.</summary>
        private static JsonArray Topics(string? field, Context context)
        {
            List<(int Order, JsonObject Node)> topics = [];
            foreach (var id in TopicIds(field))
            {
                if (!context.Items.TryGetValue(id, out var item))
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
                    ["id"] = Invariant(id),
                    ["order"] = Invariant(item.ListOrder),
                    ["title"] = title,
                    ["season"] = season,
                    ["pages"] = new JsonObject { ["from"] = from, ["to"] = to },
                }));
            }

            return [.. topics.OrderBy(t => t.Order).Select(t => (JsonNode)t.Node)];
        }

        private static (JsonNode? From, JsonNode? To) PageRange(string? metadata)
        {
            if (string.IsNullOrWhiteSpace(metadata))
            {
                return (null, null);
            }

            try
            {
                var json = JsonNode.Parse(metadata) as JsonObject;
                return (json?["startPage"]?.DeepClone(), json?["endPage"]?.DeepClone());
            }
            catch (System.Text.Json.JsonException)
            {
                return (null, null);
            }
        }

        private static IEnumerable<long> TopicIds(string? field) => (field ?? string.Empty)
            .Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(t => long.TryParse(t, NumberStyles.None, CultureInfo.InvariantCulture, out var n) ? n : 0)
            .Where(t => t > 0);

        /// <summary>gama-api's <c>files</c>: the question PDF, Word and answer (exists, size, type, price), then the extra
        /// files that exist. A file exists when it is stored and not empty (<c>Size</c> from the file check).</summary>
        private static JsonObject Files(PastPaper paper, Context context)
        {
            var questionPages = File(paper, PastPaperFileKind.Question)?.PageCount;
            JsonObject files = new()
            {
                ["pdf"] = MainFile(File(paper, PastPaperFileKind.Question), questionPages),
                ["word"] = MainFile(File(paper, PastPaperFileKind.QuestionWord), questionPages),
                ["answer"] = MainFile(File(paper, PastPaperFileKind.Answer), File(paper, PastPaperFileKind.Answer)?.PageCount),
            };

            var extras = Extras(paper);
            if (extras.Count > 0)
            {
                JsonArray list = [];
                foreach (var extra in extras)
                {
                    var type = extra.CoreTypeId is int t && context.Items.TryGetValue(t, out var item) ? item : null;
                    list.Add(new JsonObject
                    {
                        ["id"] = Invariant(extra.CoreExtraFileId),
                        ["type"] = Invariant(extra.CoreTypeId),
                        ["length"] = Invariant(extra.Length),
                        ["type_title"] = type?.Title,
                        ["size"] = PastPaperOutput.FormatSize(extra.Size!.Value),
                        ["ext"] = Extension(extra.Path),
                        ["price"] = PastPaperPricing.Price(context.BasePrices, Extension(extra.Path), extra.Length, paper.RefereeScore,
                            PastPaperPricing.ExtraTypeMultiplier(type?.Metadata)),
                    });
                }

                files["extra"] = list;
            }

            return files;

            JsonObject MainFile(PastPaperFile? file, int? pages) => file is null
                ? new() { ["exist"] = false, ["size"] = 0, ["ext"] = false, ["price"] = 0 }
                : new()
                {
                    ["exist"] = true,
                    ["size"] = PastPaperOutput.FormatSize(file.Size!.Value),
                    ["ext"] = Extension(file.Path),
                    ["price"] = PastPaperPricing.Price(context.BasePrices, Extension(file.Path), pages, paper.RefereeScore, paper.PriceMultiplier),
                };
        }

        /// <summary>One of the paper's own files, only when it is stored and not empty (gama-api's <c>check_file_exist</c>).</summary>
        private static PastPaperFile? File(PastPaper paper, PastPaperFileKind kind) =>
            paper.Files?.FirstOrDefault(t => t.Kind == kind && t.Size > 0);

        private static List<PastPaperFile> Extras(PastPaper paper) =>
            paper.Files is null ? [] : [.. paper.Files.Where(t => t.Kind == PastPaperFileKind.Extra && t.Size > 0).OrderBy(t => t.CoreExtraFileId)];

        private static string? Extension(string? path)
        {
            var ext = string.IsNullOrEmpty(path) ? string.Empty : System.IO.Path.GetExtension(path).TrimStart('.');
            return ext.Length > 0 ? ext : null;
        }

        private static JsonValue IdOrFalse(long? id) => id is long value ? JsonValue.Create(value.ToString(CultureInfo.InvariantCulture)) : JsonValue.Create(false);

        [System.Text.RegularExpressions.GeneratedRegex("[0-9]+")]
        private static partial System.Text.RegularExpressions.Regex DigitsRegex();

        private string? ThumbnailUrl(string? thumbnail) => string.IsNullOrWhiteSpace(thumbnail)
            ? null
            : fileService.Value.GetStaticFileUrl(new() { FileId = thumbnail.Replace("uploads/thumbs/", string.Empty, StringComparison.Ordinal), ContainerType = ContainerType.Thumbnail });

        private string? SubjectPictureUrl(string? picture) => string.IsNullOrWhiteSpace(picture)
            ? null
            : fileService.Value.GetStaticFileUrl(new() { FileId = picture, ContainerType = ContainerType.SubjectPicture });

        private static void Put(JsonObject data, string key, string? value)
        {
            if (value is not null)
            {
                data[key] = value;
            }
        }

        private static string? Invariant(long? value) => value?.ToString(CultureInfo.InvariantCulture);

        private static string? Invariant(int? value) => value?.ToString(CultureInfo.InvariantCulture);

        private static long? Number(Dictionary<string, string?> search, string key) =>
            search.TryGetValue(key, out var value) && long.TryParse(value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var number) && number != 0
                ? number
                : null;

        private static string? Text(Dictionary<string, string?> search, string key) =>
            search.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value) ? value : null;

        private static bool Flag(Dictionary<string, string?> search, string key) =>
            search.TryGetValue(key, out var value) && value is "1" or "true" or "True";
    }
}
