namespace GamaEdtech.Application.Service
{
    using System.Diagnostics.CodeAnalysis;
    using System.Globalization;
    using System.Linq;
    using System.Text.Json.Nodes;

    using GamaEdtech.Common.Core;
    using GamaEdtech.Common.Data;
    using GamaEdtech.Common.Data.Enumeration;
    using GamaEdtech.Data.Dto.PastPaper;
    using GamaEdtech.Domain.Entity;
    using GamaEdtech.Domain.Enumeration;

    using Microsoft.AspNetCore.WebUtilities;
    using Microsoft.EntityFrameworkCore;

    using static GamaEdtech.Common.Core.Constants;

    /// <summary>A teacher's own papers (gama-api's <c>GET tests</c> and <c>GET tests/{id}?for=edit</c> for a teacher). The owner
    /// is <c>OwnerUserId</c>, this backend's user; a paper whose owner had no account here when it was copied has none.</summary>
    public sealed partial class NativePastPaperSource
    {
        // Test_lib::LIMITED_EDIT_FIELDS: what the owner may still change once a paper is past review.
        private static readonly string[] LimitedEditFields = ["description", "files"];

        private const string PublishedEditMessage = "Only the description and the files of a published paper can be edited. Your changes are reviewed, and the website keeps showing the published version until they are approved.";

        private const string LimitedEditMessage = "In its current status only the description and the files of this paper can be edited, and it is reviewed again after the edit.";

        // Statuses in which the owner may still edit everything (Pending, Rejected, NeedsChanges), and in which they may no
        // longer delete the paper or its files (Confirmed, Rejected, Deactivated, Edited).
        private static readonly byte[] FullEditStatuses = [0, 2, 3];
        private static readonly byte[] LockedStatuses = [1, 2, 4, 6];

        // gama-api's list filters for a teacher (Test_model::getList), each an exact match.
        private static readonly (string Key, Func<IQueryable<PastPaper>, long, IQueryable<PastPaper>> Filter)[] OwnListFilters =
        [
            ("id", (q, v) => q.Where(t => t.Id == v)),
            ("section", (q, v) => q.Where(t => t.CoreBoardId == v)),
            ("base", (q, v) => q.Where(t => t.CoreGradeId == v)),
            ("course", (q, v) => q.Where(t => t.CoreCourseId == v)),
            ("lesson", (q, v) => q.Where(t => t.CoreSubjectId == v)),
            ("edu_year", (q, v) => q.Where(t => t.EduYear == v)),
            ("edu_month", (q, v) => q.Where(t => t.EduMonth == v)),
            ("variant", (q, v) => q.Where(t => t.Variant == v)),
            ("test_type", (q, v) => q.Where(t => t.CorePaperTypeId == v)),
        ];

        public async Task<ResultData<JsonObject>> GetOwnPapersAsync([NotNull] TeacherPaperRequestDto requestDto)
        {
            try
            {
                var query = requestDto.Query ?? string.Empty;
                var search = QueryHelpers.ParseQuery(query.StartsWith('?') ? query : "?" + query)
                    .ToDictionary(t => t.Key, t => (string?)t.Value.ToString(), StringComparer.Ordinal);
                var perPage = Number(search, "perpage") is long p and > 0 ? (int)Math.Min(p, MaxPerPage) : DefaultPerPage;
                var page = Number(search, "page") is long p2 and > 0 ? p2 : 1;

                var uow = UnitOfWorkProvider.Value.CreateUnitOfWork();

                // The owner also sees the papers an admin deleted (status Deleted), not the ones they deleted themselves. The
                // admin panel sees every paper that is not deleted, and filters by owner with uid (this backend's user id).
                var papers = requestDto.Manager
                    ? uow.GetRepository<PastPaper>().GetManyQueryable(t => !t.IsDeleted)
                    : uow.GetRepository<PastPaper>().GetManyQueryable(t => t.OwnerUserId == requestDto.UserId && (!t.IsDeleted || t.Status == PastPaperStatus.Deleted));
                if (requestDto.Manager && Number(search, "uid") is long ownerId)
                {
                    papers = papers.Where(t => t.OwnerUserId == ownerId);
                }

                if (requestDto.Manager && Number(search, "refScore") is long score)
                {
                    papers = papers.Where(t => t.RefereeScore == score);
                }

                if (requestDto.Manager && search.GetValueOrDefault("title") is { Length: > 0 } title)
                {
                    papers = papers.Where(t => t.Title!.Contains(title));
                }

                // The admin panel's other filters (gama-api's panel had them): the paper's code, and paper or not (is_paper 1/0).
                if (requestDto.Manager && search.GetValueOrDefault("code") is { Length: > 0 } code)
                {
                    var trimmed = code.Trim();
                    papers = papers.Where(t => t.Code == trimmed);
                }

                // Papers whose owner's edit waits for review (pending_edit=1), for the admin panel and the owner alike.
                if (Number(search, "pending_edit") == 1)
                {
                    papers = papers.Where(t => t.PendingEditDate != null);
                }

                if (requestDto.Manager && Number(search, "is_paper") is long isPaper and (0 or 1))
                {
                    var wantPaper = isPaper == 1;
                    papers = papers.Where(t => t.IsPaper == wantPaper);
                }
                foreach (var (key, filter) in OwnListFilters)
                {
                    if (Number(search, key) is long value)
                    {
                        papers = filter(papers, value);
                    }
                }

                // Unlike the other filters, status 0 (waiting for review) is a value gama-api filters on.
                if (search.TryGetValue("status", out var statusText) && byte.TryParse(statusText, NumberStyles.None, CultureInfo.InvariantCulture, out var status))
                {
                    var wanted = EnumerationExtensions.GetAll<PastPaperStatus, byte>()?.FirstOrDefault(t => t!.Value == status);
                    papers = wanted is null ? papers.Where(t => false) : papers.Where(t => t.Status == wanted);
                }

                var total = await papers.CountAsync();
                // The admin panel may sort by id (order=idasc or iddesc) or downloads (downloadsasc or downloadsdesc, the id
                // breaking a tie); otherwise the newest first, as gama-api lists.
                var ordered = requestDto.Manager && search.GetValueOrDefault("order") is { } order
                    ? order switch
                    {
                        "idasc" => papers.OrderBy(t => t.Id),
                        "iddesc" => papers.OrderByDescending(t => t.Id),
                        "downloadsasc" => papers.OrderBy(t => t.DownloadCount).ThenBy(t => t.Id),
                        "downloadsdesc" => papers.OrderByDescending(t => t.DownloadCount).ThenByDescending(t => t.Id),
                        _ => papers.OrderByDescending(t => t.CreationDate).ThenByDescending(t => t.Id),
                    }
                    : papers.OrderByDescending(t => t.CreationDate).ThenByDescending(t => t.Id);
                var list = await ordered
                    .Skip((int)((page - 1) * perPage)).Take(perPage)
                    .Include(t => t.Files)
                    .ToListAsync();
                var context = await LoadContextAsync(uow, list);

                JsonArray items = [];
                foreach (var paper in list)
                {
                    items.Add(OwnListItem(paper, context, requestDto.Manager));
                }

                return new(OperationResult.Succeeded) { Data = new() { ["list"] = items, ["num"] = total } };
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return new(OperationResult.Failed) { Errors = [new() { Message = exc.Message }] };
            }
        }

        public async Task<ResultData<JsonObject>> GetOwnPaperFileLinkAsync([NotNull] TeacherPaperRequestDto requestDto)
        {
            try
            {
                // The owner's own paper only (a deleted one they can still see too), whatever its status.
                var uow = UnitOfWorkProvider.Value.CreateUnitOfWork();
                var own = await uow.GetRepository<PastPaper>().AnyAsync(t => t.Id == requestDto.Id && t.OwnerUserId == requestDto.UserId
                    && (!t.IsDeleted || t.Status == PastPaperStatus.Deleted));
                return own
                    ? await PastPaperFileLink.GetAsync(uow, fileService.Value, requestDto.Id, requestDto.FileId, requestDto.Pending)
                    : new(OperationResult.Failed) { Errors = [new() { Message = "Permission denied.", Reference = "permissionDenied" }] };
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return new(OperationResult.Failed) { Errors = [new() { Message = exc.Message }] };
            }
        }

        public async Task<ResultData<JsonObject>> GetOwnPaperAsync([NotNull] TeacherPaperRequestDto requestDto)
        {
            try
            {
                var uow = UnitOfWorkProvider.Value.CreateUnitOfWork();
                var paper = await uow.GetRepository<PastPaper>()
                    .GetManyQueryable(t => t.Id == requestDto.Id && (!t.IsDeleted || t.Status == PastPaperStatus.Deleted), includes: t => t.Include(p => p.Files).Include(p => p.PendingFiles))
                    .FirstOrDefaultAsync();
                if (paper is null)
                {
                    return NoResult<JsonObject>();
                }

                var owner = paper.OwnerUserId == requestDto.UserId;
                if (paper.Status != PastPaperStatus.Confirmed && !owner && !requestDto.Manager)
                {
                    return new(OperationResult.Failed) { Errors = [new() { Message = "Permission denied.", Reference = "permissionDenied" }] };
                }

                var context = await LoadContextAsync(uow, [paper]);
                var data = Detail(paper, context);
                await AddExamsAsync(data, paper.CoreExamIds);
                data["uid"] = Invariant(requestDto.UserId);
                data["owner"] = owner;
                if (owner || requestDto.Manager)
                {
                    data["edit"] = EditInfo(paper, requestDto.Manager);

                    // The owner's edit of the published paper, waiting for review (or rejected, with notes); data above is
                    // still the published version.
                    data["pending_edit"] = PendingEditInfo(paper);
                }

                if (requestDto.Manager)
                {
                    data["admin"] = true;
                    data["ownerUserId"] = paper.OwnerUserId;
                    data["reviewNote"] = paper.ReviewNote;
                    data["is_deleted"] = paper.IsDeleted;
                }

                return new(OperationResult.Succeeded) { Data = data };
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return new(OperationResult.Failed) { Errors = [new() { Message = exc.Message }] };
            }
        }

        /// <summary>How the owner may edit the paper (gama-api's <c>editInfo</c> for an owner): everything while it waits for
        /// review, then only the description and files; files are deleted only until it is past review. A paper the review sent
        /// back carries the review's notes.</summary>
        internal static JsonObject EditInfo(PastPaper paper, bool manager = false)
        {
            var status = paper.Status.Value;
            var fullEdit = manager || FullEditStatuses.Contains(status);
            return new()
            {
                ["mode"] = fullEdit ? "full" : "limited",
                ["editableFields"] = fullEdit ? null : new JsonArray([.. LimitedEditFields.Select(t => (JsonNode)t)]),
                // A published paper's files are removed as part of an edit that waits for review.
                ["canDeleteFiles"] = manager || status == PastPaperStatus.Confirmed.Value || !LockedStatuses.Contains(status),
                ["message"] = fullEdit ? null : LimitedMessage(status),
                ["reviewNotes"] = status is 2 or 3 or 4 ? new JsonArray([.. ReviewNotes(paper.ReviewNote).Select(t => (JsonNode)t)]) : [],
            };
        }

        private static string LimitedMessage(byte status) => status == PastPaperStatus.Confirmed.Value ? PublishedEditMessage : LimitedEditMessage;

        /// <summary>gama-api's <c>reviewNotes</c>: each line that starts with "- " is a note; a text without such lines is one
        /// note. A copied note that is still a gama-api ticket id (its ticket could not be read) has none.</summary>
        internal static List<string> ReviewNotes(string? note)
        {
            if (string.IsNullOrWhiteSpace(note) || long.TryParse(note, NumberStyles.None, CultureInfo.InvariantCulture, out _))
            {
                return [];
            }

            List<string> notes = [.. note.Split('\n').Select(t => t.Trim()).Where(t => t.StartsWith("- ", StringComparison.Ordinal)).Select(t => t[2..].Trim())];
            return notes.Count > 0 ? notes : [note.Trim()];
        }

        /// <summary>A paper in the owner's list (gama-api's list item for a teacher).</summary>
        private JsonObject OwnListItem(PastPaper paper, Context context, bool manager = false)
        {
            JsonObject data = new()
            {
                ["id"] = Invariant(paper.Id),
                ["title"] = paper.Title,
                ["section"] = Invariant(paper.CoreBoardId),
                ["base"] = Invariant(paper.CoreGradeId),
                ["course"] = Invariant(paper.CoreCourseId ?? 0),
                ["lesson"] = Invariant(paper.CoreSubjectId),
                ["topic"] = paper.CoreTopicIds ?? string.Empty,
                ["test_type"] = Invariant(paper.CorePaperTypeId ?? 0),
                ["ref_score"] = Invariant(paper.RefereeScore),
                ["downloads"] = Invariant(paper.DownloadCount),
                ["status"] = Invariant(paper.Status.Value),
                ["msg"] = paper.ReviewNote,
                ["subdate"] = PastPaperOutput.Written(paper.CreationDate),
                ["up_date"] = paper.LastModifyDate is { } modified ? PastPaperOutput.Written(modified) : null,
                ["q_file"] = File(paper, PastPaperFileKind.Question) is not null,
                ["q_file_word"] = File(paper, PastPaperFileKind.QuestionWord) is not null,
                ["a_file"] = File(paper, PastPaperFileKind.Answer) is not null,
                ["pending_edit"] = paper.PendingEditDate is not null,
            };
            AddTitles(data, paper, context, detail: true);
            _ = data.Remove("lesson_pic");
            _ = data.Remove("is_paper");
            _ = data.Remove("area_title");

            // The admin list also names the owner and the paper's session (gama-api's admin list on Gamatrain).
            if (manager)
            {
                data["user_"] = paper.OwnerUserId;
                data["owner_name"] = paper.DisplayOwnerUserId is long display && context.Owners.TryGetValue(display, out var info)
                    ? $"{info.FirstName} {info.LastName}".Trim()
                    : null;
                data["edu_year"] = Invariant(paper.EduYear);
                data["edu_month"] = Invariant(paper.EduMonth);
                data["variant"] = Invariant(paper.Variant);
            }

            return data;
        }
    }
}
