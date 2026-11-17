namespace GamaEdtech.Application.Service
{
    using System.Diagnostics.CodeAnalysis;
    using System.Globalization;
    using System.Linq;
    using System.Text;
    using System.Text.Json;

    using GamaEdtech.Application.Interface;
    using GamaEdtech.Common.Core;
    using GamaEdtech.Common.Data;
    using GamaEdtech.Common.DataAccess.UnitOfWork;
    using GamaEdtech.Common.Service;
    using GamaEdtech.Data.Dto.Taxonomy;
    using GamaEdtech.Data.Dto.TopicMatch;
    using GamaEdtech.Domain.Entity;
    using GamaEdtech.Domain.Entity.Identity;
    using GamaEdtech.Domain.Enumeration;

    using Microsoft.AspNetCore.Http;
    using Microsoft.EntityFrameworkCore;
    using Microsoft.Extensions.Logging;

    using static GamaEdtech.Common.Core.Constants;

    public sealed class TopicMatchAdminService(Lazy<IUnitOfWorkProvider> unitOfWorkProvider, Lazy<IHttpContextAccessor> httpContextAccessor,
        Lazy<ILogger<TopicMatchAdminService>> logger, Lazy<IMigrationRouter> migrationRouter, Lazy<IAiService> aiService)
        : ServiceBase<TopicMatchAdminService>(unitOfWorkProvider, httpContextAccessor, logger), ITopicMatchAdminService
    {
        private const int MaxPageSize = 200;

        // gama-api keeps only the AI's confident matches: below this one is not worth a person's review.
        internal const int MinConfidence = 90;
        internal const string ManualModel = "manual";
        private const string AiModelName = "ai";
        private const int AiMaxTokens = 1500;
        private const double AiTemperature = 0.1;

        /// <summary>gama-api's topic match prompt (its note 17); the placeholders are filled per candidate subject.</summary>
        internal const string SuggestPrompt = """
            You are an expert curriculum-comparison assistant helping match topics between the GAMA curriculum and other international education boards (Cambridge, Edexcel, AQA, OCR, CXC, Nigerian boards, etc).

            You will be given ONE source topic and a list of CANDIDATE topics from the opposite side of a confirmed board correspondence: if the source topic is from GAMA, every candidate belongs to one external board's matching lesson; if the source topic is from an external board, every candidate belongs to GAMA's matching lesson. Matching is always between GAMA and one external board, never between two external boards.

            Your task: decide which candidate topic(s), if any, cover essentially the same learning content as the source topic - same core concept, similar depth, not just a similar-sounding title.

            Source topic:
            - Board: {sourceBoardTitle}
            - Lesson: {sourceLessonTitle}
            - Topic: {sourceTopicTitle}

            Candidate topics (Board: {candidateBoardTitle}, Lesson: {candidateLessonTitle}):
            {candidateTopicsList}

            Rules:
            - Only propose a match when the content genuinely overlaps - do not force a match just because it's the closest option available.
            - A topic may match zero, one, or (rarely) more than one candidate.
            - "confidence" is an integer 0-100 reflecting how closely the topics overlap in content and depth, not title wording.
            - "reasoning" is one short sentence explaining the match.
            - If no candidate is a reasonable match, return an empty "matches" array.
            - Return ONLY valid JSON, no markdown fences, no commentary, in exactly this shape:

            {
              "matches": [
                { "candidate_topic_id": 1234, "confidence": 87, "reasoning": "short one-sentence justification" }
              ]
            }
            """;

        public async Task<ResultData<TopicMatchListDto>> GetListAsync([NotNull] TopicMatchListRequestDto requestDto)
        {
            try
            {
                var uow = UnitOfWorkProvider.Value.CreateUnitOfWork();
                var query = uow.GetRepository<TopicMatch>().GetManyQueryable(t => true);
                if (requestDto.TopicId is int topic)
                {
                    query = query.Where(t => t.GamaTopicId == topic || t.ExternalTopicId == topic);
                }

                if (requestDto.GamaSubjectId is int gamaSubject)
                {
                    query = query.Where(t => t.GamaSubjectId == gamaSubject);
                }

                if (requestDto.ExternalSubjectId is int externalSubject)
                {
                    query = query.Where(t => t.ExternalSubjectId == externalSubject);
                }

                if (requestDto.Status is { } status)
                {
                    query = query.Where(t => t.Status == status);
                }

                var size = Math.Clamp(requestDto.PageSize, 1, MaxPageSize);
                var total = await query.CountAsync();
                var page = await query.OrderBy(t => t.GamaTopicId).ThenByDescending(t => t.Confidence).ThenBy(t => t.Id)
                    .Skip((Math.Max(requestDto.Page, 1) - 1) * size).Take(size).ToListAsync();
                return new(OperationResult.Succeeded) { Data = new() { Total = total, Items = await ToDtosAsync(uow, page) } };
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return new(OperationResult.Failed) { Errors = [new() { Message = exc.Message }] };
            }
        }

        public async Task<ResultData<long>> AddAsync([NotNull] TopicMatchAddDto requestDto)
        {
            try
            {
                if (NotNative<long>() is { } refused)
                {
                    return refused;
                }

                var uow = UnitOfWorkProvider.Value.CreateUnitOfWork();
                var subjects = await SubjectsOfAsync(uow, [requestDto.GamaTopicId, requestDto.ExternalTopicId]);
                if (requestDto.GamaTopicId == requestDto.ExternalTopicId || !subjects.TryGetValue(requestDto.GamaTopicId, out var gamaSubject)
                    || !subjects.TryGetValue(requestDto.ExternalTopicId, out var externalSubject))
                {
                    return new(OperationResult.NotValid) { Errors = [new() { Message = "Both topics must exist, each under a subject.", Reference = "noResult" }] };
                }

                var repository = uow.GetRepository<TopicMatch>();
                if (await repository.AnyAsync(t => t.GamaTopicId == requestDto.GamaTopicId && t.ExternalTopicId == requestDto.ExternalTopicId))
                {
                    return new(OperationResult.Duplicate) { Errors = [new() { Message = "This match is already suggested.", Reference = "duplicateItem" }] };
                }

                var now = DateTimeOffset.UtcNow;
                TopicMatch match = new()
                {
                    Id = await NextIdAsync(uow),
                    GamaTopicId = requestDto.GamaTopicId,
                    GamaSubjectId = gamaSubject,
                    ExternalTopicId = requestDto.ExternalTopicId,
                    ExternalSubjectId = externalSubject,
                    Confidence = Math.Min(requestDto.Confidence ?? 0, (byte)100),
                    Reasoning = Cut(requestDto.Reasoning?.Trim(), 2000),
                    AiModel = ManualModel,
                    Status = TopicMatchStatus.Pending,
                    CreationDate = now,
                    LastModifyDate = now,
                    SyncDate = now,
                };
                repository.Add(match);
                _ = await uow.SaveChangesAsync();
                return new(OperationResult.Succeeded) { Data = match.Id };
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return new(OperationResult.Failed) { Errors = [new() { Message = exc.Message }] };
            }
        }

        public async Task<ResultData<bool>> ApproveAsync(long id, long userId)
        {
            try
            {
                if (NotNative<bool>() is { } refused)
                {
                    return refused;
                }

                var uow = UnitOfWorkProvider.Value.CreateUnitOfWork();
                var match = await uow.GetRepository<TopicMatch>().GetManyQueryable(t => t.Id == id, tracking: true).FirstOrDefaultAsync();
                if (match is null)
                {
                    return NotFound<bool>();
                }

                // gama-api's addRelation: the link between the two topics, unless it is there already (either way round).
                var relations = uow.GetRepository<LegacyTaxonomyRelation, int>();
                if (!await relations.AnyAsync(t => (t.FromId == match.GamaTopicId && t.ToId == match.ExternalTopicId)
                    || (t.FromId == match.ExternalTopicId && t.ToId == match.GamaTopicId)))
                {
                    var next = (await relations.GetManyQueryable(t => true).MaxAsync(t => (int?)t.Id) ?? 0) + 1;
                    relations.Add(new() { Id = next, FromId = match.GamaTopicId, ToId = match.ExternalTopicId, SyncDate = DateTimeOffset.UtcNow });
                }

                match.Status = TopicMatchStatus.Approved;
                match.ReviewedByUserId = userId;
                match.LastModifyDate = DateTimeOffset.UtcNow;
                match.SyncDate = match.LastModifyDate.Value;
                _ = await uow.SaveChangesAsync();
                return new(OperationResult.Succeeded) { Data = true };
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return new(OperationResult.Failed) { Errors = [new() { Message = exc.Message }] };
            }
        }

        public async Task<ResultData<bool>> UnapproveAsync(long id)
        {
            try
            {
                if (NotNative<bool>() is { } refused)
                {
                    return refused;
                }

                var uow = UnitOfWorkProvider.Value.CreateUnitOfWork();
                var match = await uow.GetRepository<TopicMatch>().GetManyQueryable(t => t.Id == id && t.Status == TopicMatchStatus.Approved, tracking: true).FirstOrDefaultAsync();
                if (match is null)
                {
                    return NotFound<bool>();
                }

                // gama-api's removeRelation: the link goes (either way round); the suggestion waits again.
                var relations = uow.GetRepository<LegacyTaxonomyRelation, int>();
                foreach (var relation in await relations.GetManyQueryable(t => (t.FromId == match.GamaTopicId && t.ToId == match.ExternalTopicId)
                    || (t.FromId == match.ExternalTopicId && t.ToId == match.GamaTopicId), tracking: true).ToListAsync())
                {
                    relations.Remove(relation);
                }

                match.Status = TopicMatchStatus.Pending;
                match.ReviewedByUserId = null;
                match.CoreReviewedBy = null;
                match.LastModifyDate = DateTimeOffset.UtcNow;
                match.SyncDate = match.LastModifyDate.Value;
                _ = await uow.SaveChangesAsync();
                return new(OperationResult.Succeeded) { Data = true };
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return new(OperationResult.Failed) { Errors = [new() { Message = exc.Message }] };
            }
        }

        public async Task<ResultData<bool>> RemoveAsync(long id)
        {
            try
            {
                if (NotNative<bool>() is { } refused)
                {
                    return refused;
                }

                var uow = UnitOfWorkProvider.Value.CreateUnitOfWork();
                var repository = uow.GetRepository<TopicMatch>();
                var match = await repository.GetManyQueryable(t => t.Id == id, tracking: true).FirstOrDefaultAsync();
                if (match is null)
                {
                    return NotFound<bool>();
                }

                // gama-api deletes even an approved suggestion and leaves its link with no way to take it back here; an approval
                // must be taken back first.
                if (match.Status == TopicMatchStatus.Approved)
                {
                    return new(OperationResult.NotValid) { Errors = [new() { Message = "Take the approval back before removing the match.", Reference = "approved" }] };
                }

                repository.Remove(match);
                _ = await uow.SaveChangesAsync();
                return new(OperationResult.Succeeded) { Data = true };
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return new(OperationResult.Failed) { Errors = [new() { Message = exc.Message }] };
            }
        }

        public async Task<ResultData<TopicMatchSuggestResultDto>> SuggestAsync(int topicId)
        {
            try
            {
                if (NotNative<TopicMatchSuggestResultDto>() is { } refused)
                {
                    return refused;
                }

                var uow = UnitOfWorkProvider.Value.CreateUnitOfWork();
                var items = uow.GetRepository<LegacyTaxonomyItem, int>();
                var source = await items.GetManyQueryable(t => t.Id == topicId && t.Kind == LegacyTaxonomyKinds.Topic && !t.IsDeleted).FirstOrDefaultAsync();
                var subjects = await SubjectsOfAsync(uow, [topicId]);
                if (source is null || !subjects.TryGetValue(topicId, out var sourceSubject))
                {
                    return NotFound<TopicMatchSuggestResultDto>();
                }

                var candidateSubjects = GamaSubjects.CandidateSubjects(sourceSubject);
                if (candidateSubjects.Count == 0)
                {
                    return new(OperationResult.NotValid) { Errors = [new() { Message = "This topic's subject has no corresponding subject to compare with.", Reference = "noCandidates" }] };
                }

                // Everything is read first; the AI calls then all run at once (the wait is the slowest answer, as gama-api's).
                var sourceIsGama = GamaSubjects.IsGama(sourceSubject);
                var sourceSeason = IsSeason(source);
                var paths = await PathsAsync(uow, [sourceSubject, .. candidateSubjects]);
                var candidateKeys = candidateSubjects.Select(t => t.ToString(CultureInfo.InvariantCulture)).ToList();
                var candidateTopics = await items.GetManyQueryable(t => t.Kind == LegacyTaxonomyKinds.Topic && !t.IsDeleted && t.Parents!.Any(p => candidateKeys.Contains(p.ParentKey!)))
                    .Include(t => t.Parents).OrderBy(t => t.ListOrder).ThenBy(t => t.Id).ToListAsync();

                List<(int Subject, Data.Dto.Provider.Ai.ChatCompletionRequestDto Request, HashSet<int> Ids)> calls = [];
                List<string> errors = [];
                foreach (var subject in candidateSubjects)
                {
                    var key = subject.ToString(CultureInfo.InvariantCulture);
                    var topics = candidateTopics.Where(t => t.Parents!.Any(p => p.ParentKey == key) && IsSeason(t) == sourceSeason).ToList();
                    if (topics.Count == 0)
                    {
                        continue;
                    }

                    StringBuilder list = new();
                    foreach (var topic in topics)
                    {
                        _ = list.Append(CultureInfo.InvariantCulture, $"- id: {topic.Id}, title: {Clean(topic.Title)}\n");
                    }

                    var prompt = SuggestPrompt
                        .Replace("{sourceBoardTitle}", paths.GetValueOrDefault(sourceSubject)?.Board?.Title ?? string.Empty, StringComparison.Ordinal)
                        .Replace("{sourceLessonTitle}", paths.GetValueOrDefault(sourceSubject)?.Subject.Title ?? string.Empty, StringComparison.Ordinal)
                        .Replace("{sourceTopicTitle}", Clean(source.Title), StringComparison.Ordinal)
                        .Replace("{candidateBoardTitle}", paths.GetValueOrDefault(subject)?.Board?.Title ?? string.Empty, StringComparison.Ordinal)
                        .Replace("{candidateLessonTitle}", paths.GetValueOrDefault(subject)?.Subject.Title ?? string.Empty, StringComparison.Ordinal)
                        .Replace("{candidateTopicsList}", list.ToString(), StringComparison.Ordinal);
                    calls.Add((subject, new()
                    {
                        SystemPrompt = prompt,
                        UserPrompt = "Return the JSON now.",
                        MaxTokens = AiMaxTokens,
                        Temperature = AiTemperature,
                    }, [.. topics.Select(t => t.Id)]));
                }

                var answers = await Task.WhenAll(calls.Select(t => aiService.Value.CompleteAsync(t.Request)));

                var repository = uow.GetRepository<TopicMatch>();
                var existing = (await repository.GetManyQueryable(t => sourceIsGama ? t.GamaTopicId == topicId : t.ExternalTopicId == topicId)
                    .Select(t => sourceIsGama ? t.ExternalTopicId : t.GamaTopicId).ToListAsync()).ToHashSet();
                var next = await NextIdAsync(uow);
                var added = 0;
                var now = DateTimeOffset.UtcNow;
                foreach (var ((subject, _, ids), result) in calls.Zip(answers))
                {
                    if (result.OperationResult is not OperationResult.Succeeded || string.IsNullOrWhiteSpace(result.Data?.Content))
                    {
                        errors.Add($"subject {subject}: {result.Errors?.Select(t => t.Message).FirstOrDefault() ?? "no answer"}");
                        continue;
                    }

                    var parsed = ParseMatches(result.Data.Content);
                    if (parsed is null)
                    {
                        errors.Add($"subject {subject}: the answer was not valid JSON");
                        continue;
                    }

                    // Only the AI's confident matches, of a topic it was actually given, not suggested already.
                    foreach (var (candidate, confidence, reasoning) in parsed.Where(t => t.Confidence >= MinConfidence && ids.Contains(t.CandidateId)))
                    {
                        if (!existing.Add(candidate))
                        {
                            continue;
                        }

                        repository.Add(new()
                        {
                            Id = next++,
                            GamaTopicId = sourceIsGama ? topicId : candidate,
                            GamaSubjectId = sourceIsGama ? sourceSubject : subject,
                            ExternalTopicId = sourceIsGama ? candidate : topicId,
                            ExternalSubjectId = sourceIsGama ? subject : sourceSubject,
                            Confidence = (byte)Math.Clamp(confidence, 0, 100),
                            Reasoning = Cut(reasoning, 2000),
                            AiModel = AiModelName,
                            Status = TopicMatchStatus.Pending,
                            CreationDate = now,
                            LastModifyDate = now,
                            SyncDate = now,
                        });
                        added++;
                    }
                }

                _ = await uow.SaveChangesAsync();
                var list2 = await GetListAsync(new() { TopicId = topicId, PageSize = MaxPageSize });
                return new(OperationResult.Succeeded) { Data = new() { Added = added, Items = list2.Data?.Items ?? [], Errors = errors } };
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return new(OperationResult.Failed) { Errors = [new() { Message = exc.Message }] };
            }
        }

        /// <summary>The AI's <c>{"matches":[{"candidate_topic_id":1,"confidence":95,"reasoning":"..."}]}</c>, also inside other
        /// text; null when it is not that JSON. Internal for the tests.</summary>
        internal static List<(int CandidateId, int Confidence, string? Reasoning)>? ParseMatches(string answer)
        {
            var start = answer.IndexOf('{', StringComparison.Ordinal);
            var end = answer.LastIndexOf('}');
            if (start < 0 || end <= start)
            {
                return null;
            }

            try
            {
                using var json = JsonDocument.Parse(answer[start..(end + 1)]);
                if (!json.RootElement.TryGetProperty("matches", out var matches) || matches.ValueKind != JsonValueKind.Array)
                {
                    return [];
                }

                List<(int, int, string?)> result = [];
                foreach (var match in matches.EnumerateArray().Where(t => t.ValueKind == JsonValueKind.Object))
                {
                    if (Number(match, "candidate_topic_id") is int id and > 0)
                    {
                        result.Add((id, Number(match, "confidence") ?? 0,
                            match.TryGetProperty("reasoning", out var reasoning) && reasoning.ValueKind == JsonValueKind.String ? reasoning.GetString()?.Trim() : null));
                    }
                }

                return result;
            }
            catch (JsonException)
            {
                return null;
            }

            static int? Number(JsonElement element, string name) => element.TryGetProperty(name, out var value) switch
            {
                true when value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var n) => n,
                true when value.ValueKind == JsonValueKind.String && int.TryParse(value.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var s) => s,
                _ => null,
            };
        }

        private static async Task<List<TopicMatchDto>> ToDtosAsync(IUnitOfWork uow, List<TopicMatch> matches)
        {
            var paths = await PathsAsync(uow, [.. matches.SelectMany(t => new[] { t.GamaSubjectId, t.ExternalSubjectId })]);
            List<int> topicIds = [.. matches.SelectMany(t => new[] { t.GamaTopicId, t.ExternalTopicId }).Distinct()];
            var topics = await uow.GetRepository<LegacyTaxonomyItem, int>().GetManyQueryable(t => topicIds.Contains(t.Id))
                .Select(t => new { t.Id, t.Title }).ToDictionaryAsync(t => t.Id, t => t.Title);
            var tutorials = await FirstTutorialsAsync(uow, [.. matches.Select(t => t.GamaTopicId).Distinct()]);
            List<long> reviewerIds = [.. matches.Select(t => t.ReviewedByUserId).OfType<long>().Distinct()];
            var reviewers = await uow.GetRepository<ApplicationUser>().GetManyQueryable(t => reviewerIds.Contains(t.Id))
                .Select(t => new { t.Id, t.FirstName, t.LastName, t.UserName })
                .ToDictionaryAsync(t => t.Id, t => $"{t.FirstName} {t.LastName}".Trim() is { Length: > 0 } name ? name : t.UserName);

            return [.. matches.Select(t => new TopicMatchDto
            {
                Id = t.Id,
                Confidence = t.Confidence,
                Reasoning = t.Reasoning,
                AiModel = t.AiModel,
                Status = t.Status,
                ReviewedBy = t.ReviewedByUserId is long reviewer ? reviewers.GetValueOrDefault(reviewer) : null,
                CreationDate = t.CreationDate,
                Gama = Path(t.GamaSubjectId, t.GamaTopicId, tutorials.GetValueOrDefault(t.GamaTopicId)),
                External = Path(t.ExternalSubjectId, t.ExternalTopicId, null),
            })];

            TopicPathDto Path(int subject, int topic, TopicPathItemDto? tutorial)
            {
                var path = paths.GetValueOrDefault(subject);
                return new()
                {
                    Board = path?.Board,
                    Grade = path?.Grade,
                    Subject = path?.Subject ?? new() { Id = subject },
                    Topic = new() { Id = topic, Title = Clean(topics.GetValueOrDefault(topic)) },
                    Tutorial = tutorial,
                };
            }
        }

        private sealed record SubjectPath(TopicPathItemDto? Board, TopicPathItemDto? Grade, TopicPathItemDto Subject);

        /// <summary>gama-api's <c>getCurriculumPath</c> for each subject: its board and grade among its ancestors.</summary>
        private static async Task<Dictionary<int, SubjectPath>> PathsAsync(IUnitOfWork uow, IEnumerable<int> subjectIds)
        {
            List<long> ids = [.. subjectIds.Distinct().Select(t => (long)t)];
            var ancestors = await TopicMatching.AncestorsAsync(uow, ids);
            List<int> all = [.. ids.Select(t => (int)t).Concat(ancestors.Values.SelectMany(t => t).Select(t => (int)t)).Distinct()];
            var items = await uow.GetRepository<LegacyTaxonomyItem, int>().GetManyQueryable(t => all.Contains(t.Id))
                .Select(t => new { t.Id, t.Kind, t.Title }).ToDictionaryAsync(t => t.Id);
            Dictionary<int, SubjectPath> paths = [];
            foreach (var id in ids.Select(t => (int)t))
            {
                TopicPathItemDto? Of(short kind) => ancestors.GetValueOrDefault(id)?.Select(t => items.GetValueOrDefault((int)t)).FirstOrDefault(t => t?.Kind == kind) is { } item
                    ? new() { Id = item.Id, Title = item.Title }
                    : null;
                paths[id] = new(Of(LegacyTaxonomyKinds.Board), Of(LegacyTaxonomyKinds.Grade), new() { Id = id, Title = items.GetValueOrDefault(id)?.Title });
            }

            return paths;
        }

        /// <summary>Each topic's subject: the first of its parents that is a subject (a topic's parent is its subject).</summary>
        private static async Task<Dictionary<int, int>> SubjectsOfAsync(IUnitOfWork uow, List<int> topicIds)
        {
            var items = uow.GetRepository<LegacyTaxonomyItem, int>();
            var topics = await items.GetManyQueryable(t => topicIds.Contains(t.Id) && t.Kind == LegacyTaxonomyKinds.Topic && !t.IsDeleted)
                .Select(t => new { t.Id, t.Parent }).ToListAsync();
            var parents = topics.ToDictionary(t => t.Id, t => (t.Parent ?? string.Empty)
                .Split(['+', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(p => int.TryParse(p, NumberStyles.None, CultureInfo.InvariantCulture, out var n) ? n : 0).Where(p => p > 0).ToList());
            List<int> parentIds = [.. parents.Values.SelectMany(t => t).Distinct()];
            var subjects = (await items.GetManyQueryable(t => parentIds.Contains(t.Id) && t.Kind == LegacyTaxonomyKinds.Subject).Select(t => t.Id).ToListAsync()).ToHashSet();
            Dictionary<int, int> result = [];
            foreach (var (topic, list) in parents)
            {
                if (list.FirstOrDefault(subjects.Contains) is int subject and > 0)
                {
                    result[topic] = subject;
                }
            }

            return result;
        }

        /// <summary>gama-api's <c>getFirstByTopics</c>: each topic's first published tutorial by its order.</summary>
        private static async Task<Dictionary<int, TopicPathItemDto>> FirstTutorialsAsync(IUnitOfWork uow, List<int> topicIds)
        {
            Dictionary<int, TopicPathItemDto> result = [];
            if (topicIds.Count == 0)
            {
                return result;
            }

            var rows = await uow.GetRepository<Tutorial>().GetManyQueryable(t => t.Status == TutorialStatus.Confirmed)
                .Where(TopicMatching.TopicIn<Tutorial>(nameof(Tutorial.CoreTopicIds), [.. topicIds.Select(t => t.ToString(CultureInfo.InvariantCulture))]))
                .OrderBy(t => t.TopicOrder).ThenBy(t => t.Id).Select(t => new { t.Id, t.Title, t.CoreTopicIds }).ToListAsync();
            HashSet<int> wanted = [.. topicIds];
            foreach (var row in rows)
            {
                foreach (var part in (row.CoreTopicIds ?? string.Empty).Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                {
                    if (int.TryParse(part, NumberStyles.None, CultureInfo.InvariantCulture, out var topic) && wanted.Contains(topic))
                    {
                        _ = result.TryAdd(topic, new() { Id = row.Id, Title = row.Title });
                    }
                }
            }

            return result;
        }

        private static async Task<long> NextIdAsync(IUnitOfWork uow) =>
            (await uow.GetRepository<TopicMatch>().GetManyQueryable(t => true).MaxAsync(t => (long?)t.Id) ?? 0) + 1;

        // A season (a chapter or unit header) only matches another season, a topic only another topic.
        private static bool IsSeason(LegacyTaxonomyItem item) => (bool?)NativeTaxonomySource.ListItem(item)["season"] ?? false;

        private static string Clean(string? title) => (title ?? string.Empty).Replace("**", string.Empty, StringComparison.Ordinal).Trim();

        private static string? Cut(string? value, int length) => value is { Length: > 0 } && value.Length > length ? value[..length] : value;

        /// <summary>Reviews link topics (<c>Types</c>): refused until both the types' writes and the topic matches' are here, or
        /// the types copy from gama-api would remove the links made here.</summary>
        private ResultData<T>? NotNative<T>() =>
            migrationRouter.Value.GetWriteRoute(MigrationDomain.Types) == MigrationRoute.Native
                && migrationRouter.Value.GetWriteRoute(MigrationDomain.Types, MigrationFeatures.TopicMatches) == MigrationRoute.Native
                ? null
                : new(OperationResult.NotValid)
                {
                    Errors = [new() { Message = "Topic matches are reviewed on gama-api until Migration:WriteRoutes:Types.TopicMatches is Native.", Reference = "notNative" }],
                };

        private static ResultData<T> NotFound<T>() => new(OperationResult.NotFound) { Errors = [new() { Message = "No such topic match or topic.", Reference = "notFound" }] };
    }
}
