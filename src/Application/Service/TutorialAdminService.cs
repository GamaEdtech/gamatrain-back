namespace GamaEdtech.Application.Service
{
    using System.Diagnostics.CodeAnalysis;
    using System.Globalization;
    using System.Linq;

    using GamaEdtech.Application.Interface;
    using GamaEdtech.Common.Caching;
    using GamaEdtech.Common.Core;
    using GamaEdtech.Common.Data;
    using GamaEdtech.Common.DataAccess.UnitOfWork;
    using GamaEdtech.Common.Security;
    using GamaEdtech.Common.Service;
    using GamaEdtech.Data.Dto.Taxonomy;
    using GamaEdtech.Data.Dto.Tutorial;
    using GamaEdtech.Domain.Entity;
    using GamaEdtech.Domain.Entity.Identity;
    using GamaEdtech.Domain.Enumeration;

    using Microsoft.AspNetCore.Http;
    using Microsoft.EntityFrameworkCore;
    using Microsoft.Extensions.Logging;

    using static GamaEdtech.Common.Core.Constants;

    public sealed class TutorialAdminService(Lazy<IUnitOfWorkProvider> unitOfWorkProvider, Lazy<IHttpContextAccessor> httpContextAccessor,
        Lazy<ILogger<TutorialAdminService>> logger, Lazy<IMigrationRouter> migrationRouter, Lazy<IFileService> fileService,
        Lazy<ICacheProvider> cacheProvider, Lazy<IAiService> aiService)
        : ServiceBase<TutorialAdminService>(unitOfWorkProvider, httpContextAccessor, logger), ITutorialAdminService
    {
        private const int MaxPageSize = 100;
        private const int TitleLength = 1000;
        private const int SaveAttempts = 3;

        // The tutorial's text sent to the AI; a long tutorial is cut (its keywords are mostly in the first part anyway).
        private const int AiTextLength = 24_000;
        private const int AiMaxTokens = 1500;

        /// <summary>gama-api's keyword prompt (its note 16), with the tutorial's text in place of <c>{{content}}</c>.</summary>
        internal const string KeywordsPrompt = """
            Extract educational TOPICS and KEYWORDS from the following educational text.

            Analyze the text based on its learning objectives. Read the entire text first, then divide it into meaningful learning clusters and extract all important scientific and educational keywords.

            TOPICS Rules: Create clusters based on the text's core educational concepts. Read the entire text before clustering. Group together sections that teach the same concept. Create clusters based on the underlying concept, not on headings, examples, stories, or activities. The complete set of clusters must partition the educational content so that every meaningful educational part belongs to one cluster. Keep overlap between clusters to a minimum. Use concise, descriptive names with 2-6 words per cluster. Examples: `Quadratic Functions`, `Newton's Laws`, `Chemical Bonding`, `Cell Division`, `Probability Rules`.

            KEYWORDS Rules: Extract all important scientific and educational terms from the entire text. Include technical terms, scientific and mathematical concepts, laws, principles, rules and relationships, processes and mechanisms, methods and techniques, and units or measurements when educationally relevant. Do not extract formulas, equations, or symbols as keywords. If a formula or symbol represents a scientific or mathematical concept, extract the name of the law, principle, rule, or relationship instead. Examples: `Newton's Second Law`, `Mass-Energy Equivalence`, `Pythagorean Theorem`, `Ohm's Law`. Extract each concept only once, choose the most common educational term when synonyms appear, avoid duplicates, do not extract complete sentences, generic words, or activity titles, and do not invent concepts that are not present in the text. Use 1-4 words per keyword. Examples: `vector`, `acceleration`, `quadratic equation`, `covalent bond`, `mitosis`, `standard deviation`, `molar concentration`, `DNA replication`.

            Concept-Based Keyword Rule: Generate keywords as educational concepts, not as isolated words. If a term has little educational value on its own but gains meaning only within a broader scientific idea, extract the broader concept instead. Prefer meaningful conceptual phrases that represent what the student is expected to learn, rather than copying individual words from the text. Each keyword should represent a complete scientific or educational concept in 1-4 words.

            Review the entire text before finalizing the keywords so that each concept appears only once and the final set is comprehensive and non-redundant.

            Output rules: Output exactly two lines and nothing else.

            KEYWORDS: keyword 1 | keyword 2 | keyword 3
            TOPICS: topic 1 | topic 2 | topic 3

            No Markdown. No table. No explanation. No headings other than KEYWORDS: and TOPICS:. No text before or after these two lines.
            """;

        public async Task<ResultData<TutorialAdminListDto>> GetListAsync([NotNull] TutorialAdminListRequestDto requestDto)
        {
            try
            {
                var uow = UnitOfWorkProvider.Value.CreateUnitOfWork();
                var query = uow.GetRepository<Tutorial>().GetManyQueryable(t => true);
                if (requestDto.Status is { } status)
                {
                    query = query.Where(t => t.Status == status);
                }

                if (requestDto.BoardId is int board)
                {
                    query = query.Where(t => t.CoreBoardId == board);
                }

                if (requestDto.GradeId is int grade)
                {
                    query = query.Where(t => t.CoreGradeId == grade);
                }

                if (requestDto.SubjectId is int subject)
                {
                    query = query.Where(t => t.CoreSubjectId == subject);
                }

                if (requestDto.TopicId is int topic)
                {
                    query = query.Where(TopicMatching.TopicIn<Tutorial>(nameof(Tutorial.CoreTopicIds), [topic.ToString(CultureInfo.InvariantCulture)]));
                }

                if (requestDto.OwnerUserId is long owner)
                {
                    query = query.Where(t => t.OwnerUserId == owner);
                }

                if (!string.IsNullOrWhiteSpace(requestDto.Title))
                {
                    var text = requestDto.Title.Trim();
                    query = long.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var id)
                        ? query.Where(t => t.Id == id || t.Title!.Contains(text))
                        : query.Where(t => t.Title!.Contains(text) || (t.Description != null && t.Description.Contains(text))
                            || (t.Keywords != null && t.Keywords.Contains(text)));
                }

                var size = Math.Clamp(requestDto.PageSize, 1, MaxPageSize);
                var total = await query.CountAsync();
                var page = await query.OrderByDescending(t => t.LastModifyDate).ThenByDescending(t => t.Id)
                    .Skip((Math.Max(requestDto.Page, 1) - 1) * size).Take(size)
                    .Select(t => new Tutorial
                    {
                        Id = t.Id,
                        Title = t.Title,
                        Status = t.Status,
                        CoreBoardId = t.CoreBoardId,
                        CoreGradeId = t.CoreGradeId,
                        CoreCourseId = t.CoreCourseId,
                        CoreSubjectId = t.CoreSubjectId,
                        CoreTopicIds = t.CoreTopicIds,
                        Keywords = t.Keywords,
                        OwnerUserId = t.OwnerUserId,
                        DisplayOwnerUserId = t.DisplayOwnerUserId,
                        ViewCount = t.ViewCount,
                        CreationDate = t.CreationDate,
                        LastModifyDate = t.LastModifyDate,
                    }).ToListAsync();

                var names = await NamesAsync(uow, page);
                return new(OperationResult.Succeeded)
                {
                    Data = new() { Total = total, Items = [.. page.Select(t => Fill(new TutorialAdminItemDto(), t, names))] },
                };
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return new(OperationResult.Failed) { Errors = [new() { Message = exc.Message }] };
            }
        }

        public async Task<ResultData<TutorialAdminDto>> GetAsync(long id)
        {
            try
            {
                var uow = UnitOfWorkProvider.Value.CreateUnitOfWork();
                var tutorial = await uow.GetRepository<Tutorial>().GetManyQueryable(t => t.Id == id).FirstOrDefaultAsync();
                if (tutorial is null)
                {
                    return NotFound<TutorialAdminDto>();
                }

                var names = await NamesAsync(uow, [tutorial]);
                var data = Fill(new TutorialAdminDto(), tutorial, names);
                data.Content = TutorialContent.ForPage(tutorial.Content, fileService.Value);
                data.Description = tutorial.Description;
                return new(OperationResult.Succeeded) { Data = data };
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return new(OperationResult.Failed) { Errors = [new() { Message = exc.Message }] };
            }
        }

        public async Task<ResultData<long>> CreateAsync(long userId, [NotNull] TutorialEditDto requestDto)
        {
            try
            {
                if (NotNative<long>() is { } refused)
                {
                    return refused;
                }

                var uow = UnitOfWorkProvider.Value.CreateUnitOfWork();
                var checkedEdit = await CheckAsync(uow, requestDto);
                if (checkedEdit.Errors is not null)
                {
                    return new(OperationResult.NotValid) { Errors = checkedEdit.Errors };
                }

                var now = DateTimeOffset.UtcNow;
                Tutorial tutorial = new()
                {
                    CoreOwnerId = 0,
                    OwnerUserId = userId,
                    DisplayOwnerUserId = userId,
                    Status = requestDto.Status ?? TutorialStatus.Pending,
                    CreationDate = now,
                    LastModifyDate = now,
                    SyncDate = now,
                };
                Apply(tutorial, requestDto, checkedEdit.TopicOrder);

                var id = await SaveNewAsync(tutorial);
                await ForgetAsync(id, tutorial.CoreSubjectId);
                return new(OperationResult.Succeeded) { Data = id };
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return new(OperationResult.Failed) { Errors = [new() { Message = exc.Message }] };
            }
        }

        public async Task<ResultData<bool>> UpdateAsync(long id, [NotNull] TutorialEditDto requestDto)
        {
            try
            {
                if (NotNative<bool>() is { } refused)
                {
                    return refused;
                }

                var uow = UnitOfWorkProvider.Value.CreateUnitOfWork();
                var tutorial = await uow.GetRepository<Tutorial>().GetManyQueryable(t => t.Id == id, tracking: true).FirstOrDefaultAsync();
                if (tutorial is null)
                {
                    return NotFound<bool>();
                }

                var checkedEdit = await CheckAsync(uow, requestDto);
                if (checkedEdit.Errors is not null)
                {
                    return new(OperationResult.NotValid) { Errors = checkedEdit.Errors };
                }

                var oldSubject = tutorial.CoreSubjectId;
                Apply(tutorial, requestDto, checkedEdit.TopicOrder);
                if (requestDto.Status is { } status)
                {
                    tutorial.Status = status;
                }

                // The save hook does not stamp it on this entity (CLAUDE.md, "LastModifyDate"); the lists sort by it.
                tutorial.LastModifyDate = DateTimeOffset.UtcNow;
                tutorial.SyncDate = tutorial.LastModifyDate.Value;
                _ = await uow.SaveChangesAsync();
                await ForgetAsync(id, oldSubject, tutorial.CoreSubjectId);
                return new(OperationResult.Succeeded) { Data = true };
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return new(OperationResult.Failed) { Errors = [new() { Message = exc.Message }] };
            }
        }

        public async Task<ResultData<bool>> ChangeStatusAsync(long id, [NotNull] TutorialStatus status)
        {
            try
            {
                if (NotNative<bool>() is { } refused)
                {
                    return refused;
                }

                var uow = UnitOfWorkProvider.Value.CreateUnitOfWork();
                var tutorial = await uow.GetRepository<Tutorial>().GetManyQueryable(t => t.Id == id, tracking: true).FirstOrDefaultAsync();
                if (tutorial is null)
                {
                    return NotFound<bool>();
                }

                tutorial.Status = status;
                tutorial.LastModifyDate = DateTimeOffset.UtcNow;
                _ = await uow.SaveChangesAsync();
                await ForgetAsync(id, tutorial.CoreSubjectId);
                return new(OperationResult.Succeeded) { Data = true };
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return new(OperationResult.Failed) { Errors = [new() { Message = exc.Message }] };
            }
        }

        public async Task<ResultData<bool>> DeleteAsync(long id)
        {
            try
            {
                if (NotNative<bool>() is { } refused)
                {
                    return refused;
                }

                var uow = UnitOfWorkProvider.Value.CreateUnitOfWork();
                var repository = uow.GetRepository<Tutorial>();
                var tutorial = await repository.GetManyQueryable(t => t.Id == id, tracking: true).FirstOrDefaultAsync();
                if (tutorial is null)
                {
                    return NotFound<bool>();
                }

                var images = uow.GetRepository<TutorialImage>();
                foreach (var image in await images.GetManyQueryable(t => t.TutorialId == id, tracking: true).ToListAsync())
                {
                    images.Remove(image);
                }

                repository.Remove(tutorial);
                _ = await uow.SaveChangesAsync();
                await ForgetAsync(id, tutorial.CoreSubjectId);
                return new(OperationResult.Succeeded) { Data = true };
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return new(OperationResult.Failed) { Errors = [new() { Message = exc.Message }] };
            }
        }

        public async Task<ResultData<TutorialKeywordsDto>> GenerateKeywordsAsync(long id)
        {
            try
            {
                if (NotNative<TutorialKeywordsDto>() is { } refused)
                {
                    return refused;
                }

                var uow = UnitOfWorkProvider.Value.CreateUnitOfWork();
                var tutorial = await uow.GetRepository<Tutorial>().GetManyQueryable(t => t.Id == id, tracking: true).FirstOrDefaultAsync();
                if (tutorial is null)
                {
                    return NotFound<TutorialKeywordsDto>();
                }

                var text = TutorialContent.PlainText(tutorial.Content, AiTextLength);
                if (text.Length == 0)
                {
                    return new(OperationResult.NotValid) { Errors = [new() { Message = "The tutorial has no text to read.", Reference = "emptyContent" }] };
                }

                var completion = await aiService.Value.CompleteAsync(new()
                {
                    SystemPrompt = KeywordsPrompt,
                    UserPrompt = "Tutorial:\n" + text,
                    MaxTokens = AiMaxTokens,
                });
                if (completion.OperationResult is not OperationResult.Succeeded || string.IsNullOrWhiteSpace(completion.Data?.Content))
                {
                    return new(OperationResult.Failed) { Errors = completion.Errors ?? [new() { Message = "The AI did not answer." }] };
                }

                var found = ParseKeywords(completion.Data.Content);
                if (found.Keywords.Count == 0)
                {
                    return new(OperationResult.Failed) { Errors = [new() { Message = "The AI's answer had no keywords.", Reference = "noKeywords" }] };
                }

                tutorial.Keywords = TutorialImportService.JoinKeywords(found.Keywords);
                _ = await uow.SaveChangesAsync();
                return new(OperationResult.Succeeded) { Data = found };
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return new(OperationResult.Failed) { Errors = [new() { Message = exc.Message }] };
            }
        }

        /// <summary>The AI's two lines (<c>KEYWORDS: a | b</c>, <c>TOPICS: c | d</c>), each list without duplicates. Internal for
        /// the tests.</summary>
        internal static TutorialKeywordsDto ParseKeywords(string answer)
        {
            List<string> keywords = [];
            List<string> clusters = [];
            // Markdown marks (bold, code) are dropped: the prompt asks for none, but a model may still add them.
            foreach (var line in answer.Split('\n').Select(t => t.Replace("*", string.Empty, StringComparison.Ordinal).Replace("`", string.Empty, StringComparison.Ordinal).Trim()))
            {
                if (line.StartsWith("KEYWORDS:", StringComparison.OrdinalIgnoreCase))
                {
                    keywords = Split(line["KEYWORDS:".Length..]);
                }
                else if (line.StartsWith("TOPICS:", StringComparison.OrdinalIgnoreCase))
                {
                    clusters = Split(line["TOPICS:".Length..]);
                }
            }

            return new() { Keywords = keywords, Clusters = clusters };

            static List<string> Split(string value) => [.. value.Split('|').Select(t => t.Trim()).Where(t => t.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase)];
        }

        private sealed record CheckedEdit(List<Error>? Errors, int? TopicOrder);

        /// <summary>gama-api's required fields (title, content, board, subject) and that the types exist: the board, grade and
        /// subject in their lists, each topic a topic. The topic order is the last topic's list order, as gama-api's.</summary>
        private static async Task<CheckedEdit> CheckAsync(IUnitOfWork uow, TutorialEditDto edit)
        {
            List<string> missing = [];
            if (string.IsNullOrWhiteSpace(edit.Title))
            {
                missing.Add("title");
            }

            if (string.IsNullOrWhiteSpace(TutorialContent.PlainText(edit.Content, 1)) && !(edit.Content ?? string.Empty).Contains("<img", StringComparison.OrdinalIgnoreCase))
            {
                missing.Add("content");
            }

            if (edit.BoardId is not > 0)
            {
                missing.Add("board");
            }

            if (edit.SubjectId is not > 0)
            {
                missing.Add("subject");
            }

            if (missing.Count > 0)
            {
                return new([new() { Message = $"Required: {string.Join(", ", missing)}.", Reference = "requiredField", Value = missing }], null);
            }

            if (edit.Title!.Trim().Length > TitleLength)
            {
                return new([new() { Message = $"The title can have at most {TitleLength} characters.", Reference = "titleTooLong" }], null);
            }

            List<int> topicIds = [.. (edit.TopicIds ?? []).Distinct()];
            List<int> typeIds = [edit.BoardId!.Value, edit.SubjectId!.Value, .. topicIds];
            if (edit.GradeId is > 0)
            {
                typeIds.Add(edit.GradeId.Value);
            }

            var items = await uow.GetRepository<LegacyTaxonomyItem, int>().GetManyQueryable(t => typeIds.Contains(t.Id) && !t.IsDeleted)
                .Select(t => new { t.Id, t.Kind, t.ListOrder }).ToDictionaryAsync(t => t.Id);
            bool Is(int? id, short kind) => id is int value && items.TryGetValue(value, out var item) && item.Kind == kind;
            var valid = Is(edit.BoardId, LegacyTaxonomyKinds.Board) && Is(edit.SubjectId, LegacyTaxonomyKinds.Subject)
                && (edit.GradeId is not > 0 || Is(edit.GradeId, LegacyTaxonomyKinds.Grade)) && topicIds.All(t => Is(t, LegacyTaxonomyKinds.Topic));
            int? topicOrder = valid && topicIds.Count > 0 ? items[topicIds[^1]].ListOrder : null;
            return valid
                ? new(null, topicOrder)
                : new([new() { Message = "A board, grade, subject or topic does not exist.", Reference = "wrongType" }], null);
        }

        /// <summary>The edit's fields on the tutorial: the HTML sanitized, its size, and the description from its headings
        /// unless one is given.</summary>
        private static void Apply(Tutorial tutorial, TutorialEditDto edit, int? topicOrder)
        {
            var content = edit.Content.SanitizeHtml() ?? string.Empty;
            tutorial.Title = edit.Title!.Trim();
            tutorial.Content = content;
            tutorial.ContentSize = content.Length;
            tutorial.Description = string.IsNullOrWhiteSpace(edit.Description)
                ? TutorialContent.Description(content)
                : edit.Description.Trim()[..Math.Min(edit.Description.Trim().Length, TutorialContent.DescriptionLength)];
            tutorial.CoreBoardId = edit.BoardId;
            tutorial.CoreGradeId = edit.GradeId is > 0 ? edit.GradeId : null;
            tutorial.CoreCourseId = edit.CourseId is > 0 ? edit.CourseId : null;
            tutorial.CoreSubjectId = edit.SubjectId;
            List<int> topics = [.. (edit.TopicIds ?? []).Distinct()];
            tutorial.CoreTopicIds = topics.Count > 0 ? string.Join('+', topics) : null;
            tutorial.TopicOrder = topicOrder;
            if (edit.Keywords is not null)
            {
                var keywords = TutorialImportService.JoinKeywords(edit.Keywords);
                tutorial.Keywords = keywords.Length > 0 ? keywords : null;
            }
        }

        /// <summary>Saves a new tutorial under the next id after the highest one (gama-api's ids are kept, so new ones continue
        /// above them), trying again if another add took the same id meanwhile.</summary>
        private async Task<long> SaveNewAsync(Tutorial tutorial)
        {
            var attempt = 1;
            while (true)
            {
                var uow = UnitOfWorkProvider.Value.CreateUnitOfWork();
                var repository = uow.GetRepository<Tutorial>();
                tutorial.Id = (await repository.GetManyQueryable(t => true).MaxAsync(t => (long?)t.Id) ?? 0) + 1;
                repository.Add(tutorial);
                try
                {
                    _ = await uow.SaveChangesAsync();
                    return tutorial.Id;
                }
                catch (DbUpdateException exc) when (attempt < SaveAttempts)
                {
                    Logger.Value.LogWarning(exc, "Tutorial id {Id} was taken meanwhile; trying the next one", tutorial.Id);
                    attempt++;
                }
            }
        }

        /// <summary>Drops the website's cached page and lesson trees of a changed tutorial, so the change shows at once (the
        /// search lists follow within their few minutes of cache).</summary>
        private async Task ForgetAsync(long id, params int?[] subjects)
        {
            try
            {
                await cacheProvider.Value.RemoveAsync(SearchService.TutorialCacheKey(id));
                foreach (var subject in subjects.OfType<int>().Distinct())
                {
                    await cacheProvider.Value.RemoveAsync(SearchService.LessonTreeCacheKey(subject));
                }
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
            }
        }

        private sealed record Names(IReadOnlyDictionary<int, string?> Types, IReadOnlyDictionary<long, string> Owners);

        private static async Task<Names> NamesAsync(IUnitOfWork uow, IReadOnlyCollection<Tutorial> tutorials)
        {
            HashSet<int> ids = [];
            foreach (var tutorial in tutorials)
            {
                ids.UnionWith(new int?[] { tutorial.CoreBoardId, tutorial.CoreGradeId, tutorial.CoreSubjectId }.OfType<int>());
                ids.UnionWith(TopicIds(tutorial.CoreTopicIds));
            }

            List<int> typeIds = [.. ids];
            var types = await uow.GetRepository<LegacyTaxonomyItem, int>().GetManyQueryable(t => typeIds.Contains(t.Id))
                .Select(t => new { t.Id, t.Title }).ToDictionaryAsync(t => t.Id, t => t.Title);

            List<long> ownerIds = [.. tutorials.Select(t => t.OwnerUserId ?? t.DisplayOwnerUserId).OfType<long>().Distinct()];
            var owners = await uow.GetRepository<ApplicationUser>().GetManyQueryable(t => ownerIds.Contains(t.Id))
                .Select(t => new { t.Id, t.FirstName, t.LastName, t.UserName })
                .ToDictionaryAsync(t => t.Id, t => $"{t.FirstName} {t.LastName}".Trim() is { Length: > 0 } name ? name : t.UserName ?? string.Empty);
            return new(types, owners);
        }

        private static T Fill<T>(T data, Tutorial tutorial, Names names)
            where T : TutorialAdminItemDto
        {
            var ownerId = tutorial.OwnerUserId ?? tutorial.DisplayOwnerUserId;
            data.Id = tutorial.Id;
            data.Title = tutorial.Title;
            data.Status = tutorial.Status;
            data.BoardId = tutorial.CoreBoardId;
            data.BoardTitle = Title(tutorial.CoreBoardId);
            data.GradeId = tutorial.CoreGradeId;
            data.GradeTitle = Title(tutorial.CoreGradeId);
            data.CourseId = tutorial.CoreCourseId;
            data.SubjectId = tutorial.CoreSubjectId;
            data.SubjectTitle = Title(tutorial.CoreSubjectId);
            data.Topics = [.. TopicIds(tutorial.CoreTopicIds).Select(t => new TutorialTopicDto { Id = t, Title = Title(t)?.Replace("**", string.Empty, StringComparison.Ordinal).Trim() })];
            data.Keywords = [.. (tutorial.Keywords ?? string.Empty).Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)];
            data.OwnerUserId = ownerId;
            data.OwnerName = ownerId is long owner ? names.Owners.GetValueOrDefault(owner) : null;
            data.ViewCount = tutorial.ViewCount;
            data.CreationDate = tutorial.CreationDate;
            data.LastModifyDate = tutorial.LastModifyDate;
            return data;

            string? Title(int? id) => id is int value ? names.Types.GetValueOrDefault(value) : null;
        }

        private static IEnumerable<int> TopicIds(string? topics) =>
            (topics ?? string.Empty).Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(t => int.TryParse(t, NumberStyles.None, CultureInfo.InvariantCulture, out var id) ? id : 0).Where(t => t > 0);

        private ResultData<T>? NotNative<T>() =>
            migrationRouter.Value.GetWriteRoute(MigrationDomain.Tutorials) == MigrationRoute.Native
                ? null
                : new(OperationResult.NotValid)
                {
                    Errors = [new() { Message = "Tutorials are edited on gama-api until Migration:WriteRoutes:Tutorials is Native.", Reference = "notNative" }],
                };

        private static ResultData<T> NotFound<T>() => new(OperationResult.NotFound) { Errors = [new() { Message = "The tutorial does not exist.", Reference = "notFound" }] };
    }
}
