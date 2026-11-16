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
        private static readonly string[] LimitedEditFields = ["description", "file_pdf", "file_word", "file_answer", "file_extra"];

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

                // The owner also sees the papers an admin deleted (status Deleted), not the ones they deleted themselves.
                var papers = uow.GetRepository<PastPaper>()
                    .GetManyQueryable(t => t.OwnerUserId == requestDto.UserId && (!t.IsDeleted || t.Status == PastPaperStatus.Deleted));
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
                var list = await papers.OrderByDescending(t => t.CreationDate).ThenByDescending(t => t.Id)
                    .Skip((int)((page - 1) * perPage)).Take(perPage)
                    .Include(t => t.Files)
                    .ToListAsync();
                var context = await LoadContextAsync(uow, list);

                JsonArray items = [];
                foreach (var paper in list)
                {
                    items.Add(OwnListItem(paper, context));
                }

                return new(OperationResult.Succeeded) { Data = new() { ["list"] = items, ["num"] = total } };
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
                    .GetManyQueryable(t => t.Id == requestDto.Id && (!t.IsDeleted || t.Status == PastPaperStatus.Deleted), includes: t => t.Include(p => p.Files))
                    .FirstOrDefaultAsync();
                if (paper is null)
                {
                    return NoResult<JsonObject>();
                }

                var owner = paper.OwnerUserId == requestDto.UserId;
                if (paper.Status != PastPaperStatus.Confirmed && !owner)
                {
                    return new(OperationResult.Failed) { Errors = [new() { Message = "Permission denied.", Reference = "permissionDenied" }] };
                }

                var context = await LoadContextAsync(uow, [paper]);
                var data = Detail(paper, context);
                await AddExamsAsync(data, paper.CoreExamIds);
                data["uid"] = Invariant(requestDto.UserId);
                data["owner"] = owner;
                if (owner)
                {
                    data["edit"] = EditInfo(paper);
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
        internal static JsonObject EditInfo(PastPaper paper)
        {
            var status = paper.Status.Value;
            var fullEdit = FullEditStatuses.Contains(status);
            return new()
            {
                ["mode"] = fullEdit ? "full" : "limited",
                ["editableFields"] = fullEdit ? null : new JsonArray([.. LimitedEditFields.Select(t => (JsonNode)t)]),
                ["canDeleteFiles"] = !LockedStatuses.Contains(status),
                ["message"] = fullEdit ? null : LimitedEditMessage,
                ["reviewNotes"] = status is 2 or 3 or 4 ? new JsonArray([.. ReviewNotes(paper.ReviewNote).Select(t => (JsonNode)t)]) : [],
            };
        }

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
        private JsonObject OwnListItem(PastPaper paper, Context context)
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
            };
            AddTitles(data, paper, context, detail: true);
            _ = data.Remove("lesson_pic");
            _ = data.Remove("is_paper");
            _ = data.Remove("area_title");
            return data;
        }
    }
}
