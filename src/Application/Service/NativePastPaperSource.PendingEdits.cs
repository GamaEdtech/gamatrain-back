namespace GamaEdtech.Application.Service
{
    using System.Globalization;
    using System.Linq;
    using System.Text.Json.Nodes;

    using GamaEdtech.Application.Interface;
    using GamaEdtech.Common.Core;
    using GamaEdtech.Common.Data;
    using GamaEdtech.Common.DataAccess.UnitOfWork;
    using GamaEdtech.Domain.Entity;
    using GamaEdtech.Domain.Enumeration;

    using Microsoft.Extensions.Logging;

    using static GamaEdtech.Common.Core.Constants;

    /// <summary>The owner's edits to a published paper (since 2026-11-25). They wait for review apart from the paper
    /// (<see cref="PastPaper.PendingDescription"/>, <see cref="PastPaperPendingFile"/>), so the published version stays on the
    /// website until an admin approves the edit (<see cref="ApplyPendingEditAsync"/>) or rejects it
    /// (<see cref="DiscardPendingEdit"/>). See docs/business/past-papers.md, "Edits to a published paper".</summary>
    public sealed partial class NativePastPaperSource
    {
        /// <summary>A stored file a change replaced or removed, to remove from storage once nothing names it.</summary>
        internal readonly record struct DroppedFile(string Path, ContainerType Container);

        private const string PendingEditMessage = "Your changes were saved and will be reviewed. Until they are approved, the website keeps showing the published version.";

        /// <summary>Keeps the owner's edit of a published paper for review: the description and files, merged into an edit
        /// already waiting. The paper and its files are not changed.</summary>
        private async Task<ResultData<JsonNode>> StageEditAsync(IUnitOfWork uow, PastPaper paper, JsonObject body, long userId, List<string> ignored)
        {
            var pending = paper.PendingFiles ??= [];
            List<DroppedFile> dropped = [];
            var changed = false;
            if (Field(body, "description") is { } description)
            {
                paper.PendingDescription = CorrectDescription(description);
                changed = true;
            }

            if (ReadUpload(Field(body, "file_pdf"), userId, PdfExtensions) is { } question)
            {
                Stage(MainRow(pending, paper.Id, PastPaperFileKind.Question), question, dropped);
                changed = true;
            }

            if (ReadUpload(Field(body, "file_word"), userId, WordExtensions) is { } word)
            {
                Stage(MainRow(pending, paper.Id, PastPaperFileKind.QuestionWord), word, dropped);
                changed = true;
            }

            if (ReadUpload(Field(body, "file_answer"), userId, AnswerExtensions) is { } answer)
            {
                Stage(MainRow(pending, paper.Id, PastPaperFileKind.Answer), answer, dropped);
                changed = true;
            }

            foreach (var extra in (body["file_extra"] as JsonArray ?? []).OfType<JsonObject>())
            {
                int? type = int.TryParse(Field(extra, "type"), NumberStyles.None, CultureInfo.InvariantCulture, out var t) && t > 0 ? t : null;
                var upload = ReadUpload(Field(extra, "file"), userId, ExtraExtensions);
                PastPaperPendingFile? row = null;
                if (long.TryParse(Field(extra, "id"), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var extraId))
                {
                    // A negative id is a new extra file of this edit (-its row id); a positive one a published extra file.
                    if (extraId < 0)
                    {
                        row = pending.FirstOrDefault(f => f.Kind == PastPaperFileKind.Extra && f.CoreExtraFileId is null && f.Id == -extraId);
                    }
                    else if (paper.Files?.FirstOrDefault(f => f.Kind == PastPaperFileKind.Extra && f.CoreExtraFileId == extraId) is { } live)
                    {
                        row = pending.FirstOrDefault(f => f.Kind == PastPaperFileKind.Extra && f.CoreExtraFileId == extraId);
                        if (row is null)
                        {
                            row = new() { PastPaperId = paper.Id, Kind = PastPaperFileKind.Extra, CoreExtraFileId = extraId, CoreTypeId = live.CoreTypeId, CreationDate = DateTimeOffset.UtcNow };
                            pending.Add(row);
                        }

                        row.IsRemoval = false;
                    }
                }
                else if (upload is not null)
                {
                    row = new() { PastPaperId = paper.Id, Kind = PastPaperFileKind.Extra, CoreTypeId = type ?? 0, CreationDate = DateTimeOffset.UtcNow };
                    pending.Add(row);
                }

                if (row is not null)
                {
                    row.CoreTypeId = type ?? row.CoreTypeId;
                    if (upload is not null)
                    {
                        Stage(row, upload, dropped);
                    }

                    changed = true;
                }
            }

            if (changed)
            {
                paper.PendingEditDate = DateTimeOffset.UtcNow;
                paper.PendingEditNote = null;
                _ = await uow.SaveChangesAsync();
                await RemoveUnusedFilesAsync(UnitOfWorkProvider.Value, fileService, Logger.Value, dropped);
            }

            JsonObject result = new() { ["id"] = paper.Id, ["status"] = (int)paper.Status.Value, ["editMode"] = "limited", ["pendingEdit"] = paper.PendingEditDate is not null };
            List<string> messages = [];
            if (ignored.Count > 0)
            {
                result["ignoredFields"] = new JsonArray([.. ignored.Select(t => (JsonNode)t)]);
                messages.Add("In its current status only the description and the files of this paper were saved, the other changes were not applied.");
            }

            if (changed)
            {
                messages.Add(PendingEditMessage);
            }

            if (messages.Count > 0)
            {
                result["message"] = string.Join(' ', messages);
            }

            return new(OperationResult.Succeeded) { Data = result };
        }

        /// <summary>Keeps the owner's removal of a published paper's Word file, answer or extra file for review; a file the edit
        /// itself added is simply dropped. The paper and its files are not changed.</summary>
        private static void StageRemoval(PastPaper paper, PastPaperFileKind kind, long? extraId, ICollection<DroppedFile> dropped)
        {
            var pending = paper.PendingFiles ??= [];
            var live = kind == PastPaperFileKind.Extra
                ? paper.Files?.FirstOrDefault(f => f.Kind == kind && f.CoreExtraFileId == extraId)
                : paper.Files?.FirstOrDefault(f => f.Kind == kind);
            var row = kind == PastPaperFileKind.Extra
                ? pending.FirstOrDefault(f => f.Kind == kind && f.CoreExtraFileId == extraId)
                : pending.FirstOrDefault(f => f.Kind == kind);
            if (live is null)
            {
                if (row is not null)
                {
                    Drop(row, dropped);
                    _ = pending.Remove(row);
                }
            }
            else
            {
                if (row is null)
                {
                    row = new() { PastPaperId = paper.Id, Kind = kind, CoreExtraFileId = extraId, CreationDate = DateTimeOffset.UtcNow };
                    pending.Add(row);
                }

                Drop(row, dropped);
                (row.IsRemoval, row.Path, row.Extension, row.Size, row.PageCount, row.Thumbnail) = (true, null, null, null, null, null);
            }

            Touch(paper);
        }

        /// <summary>Drops a new extra file of the edit (its row), whoever asks; the caller checks that they may.</summary>
        private static void DropPendingExtra(PastPaper paper, long rowId, ICollection<DroppedFile> dropped)
        {
            if (paper.PendingFiles?.FirstOrDefault(f => f.Id == rowId) is { } row)
            {
                Drop(row, dropped);
                _ = paper.PendingFiles.Remove(row);
            }

            Touch(paper);
        }

        /// <summary>Applies the edit waiting for review to the published paper (the admin approved it): the description, then
        /// each file replaced, added or removed. Needs the paper tracked with its files and pending files. The published files
        /// it replaces or removes are added to <paramref name="dropped"/>, to remove from storage after the save.</summary>
        internal static async Task ApplyPendingEditAsync(IUnitOfWork uow, PastPaper paper, ICollection<DroppedFile> dropped)
        {
            var now = DateTimeOffset.UtcNow;
            if (paper.PendingDescription is { } description)
            {
                paper.Description = description;
            }

            var files = paper.Files ??= [];
            var mainChanged = false;
            foreach (var row in paper.PendingFiles?.ToList() ?? [])
            {
                var live = PublishedFile(files, row);
                if (row.IsRemoval)
                {
                    if (live is not null)
                    {
                        Drop(live.Path, ContainerType.PastPaper, dropped);
                        _ = files.Remove(live);
                    }

                    continue;
                }

                if (live is null)
                {
                    // A type change of an extra file removed meanwhile has nothing to change.
                    if (row.Path is null)
                    {
                        continue;
                    }

                    live = new() { PastPaperId = paper.Id, Kind = row.Kind };
                    files.Add(live);
                }

                if (row.Kind == PastPaperFileKind.Extra)
                {
                    live.CoreTypeId = row.CoreTypeId ?? live.CoreTypeId ?? 0;
                }

                if (row.Path is not null)
                {
                    Drop(live.Path, ContainerType.PastPaper, dropped);
                    (live.Path, live.Extension, live.Size, live.PageCount, live.CreationDate, live.CheckDate) = (row.Path, row.Extension, row.Size, row.PageCount, now, now);
                    if (row.Kind == PastPaperFileKind.Question && row.Thumbnail is not null)
                    {
                        Drop(paper.Thumbnail, ContainerType.Thumbnail, dropped);
                        paper.Thumbnail = row.Thumbnail;
                    }

                    mainChanged |= row.Kind == PastPaperFileKind.Question || row.Kind == PastPaperFileKind.QuestionWord;
                }
            }

            // The save hook doesn't stamp LastModifyDate (see CLAUDE.md); a new question or Word file is a new version.
            if (mainChanged)
            {
                paper.LastModifyDate = now;
            }

            // The rows' files are the paper's now: clear the rows without dropping them.
            paper.PendingFiles?.Clear();
            DiscardPendingEdit(paper, null, dropped);
            await AssignExtraIdsAsync(uow, paper);
        }

        /// <summary>Drops the edit waiting for review; with <paramref name="notes"/> (a rejection) the owner sees them on the
        /// edit form until their next edit. The published paper is not changed; the edit's uploads are added to
        /// <paramref name="dropped"/>.</summary>
        internal static void DiscardPendingEdit(PastPaper paper, string? notes, ICollection<DroppedFile> dropped)
        {
            foreach (var row in paper.PendingFiles ?? [])
            {
                Drop(row, dropped);
            }

            paper.PendingFiles?.Clear();
            paper.PendingDescription = null;
            paper.PendingEditDate = null;
            paper.PendingEditNote = notes;
        }

        /// <summary>The edit waiting for review, for the edit form and the admin review: its description and files (a removal
        /// as <c>removed</c>; an extra file by <c>extraId</c>, negative for a new one), or a rejected edit's notes. Each file
        /// opens with <c>files/{type}?pending=true</c>. Null when there is neither.</summary>
        internal static JsonObject? PendingEditInfo(PastPaper paper)
        {
            if (paper.PendingEditDate is null && string.IsNullOrEmpty(paper.PendingEditNote))
            {
                return null;
            }

            JsonArray files = [];
            foreach (var row in (paper.PendingFiles ?? []).OrderBy(t => t.Kind.Value).ThenBy(t => t.Id))
            {
                files.Add(new JsonObject
                {
                    ["type"] = TypeName(row.Kind),
                    ["extraId"] = row.Kind == PastPaperFileKind.Extra ? row.CoreExtraFileId ?? -row.Id : null,
                    ["extraType"] = row.Kind == PastPaperFileKind.Extra ? row.CoreTypeId : null,
                    ["removed"] = row.IsRemoval,
                    ["newFile"] = row.Path is not null,
                    ["ext"] = row.Extension,
                    ["size"] = row.Size is long size ? PastPaperOutput.FormatSize(size) : null,
                    ["pages"] = row.PageCount,
                });
            }

            return new()
            {
                ["waiting"] = paper.PendingEditDate is not null,
                ["date"] = paper.PendingEditDate is { } date ? PastPaperOutput.Written(date) : null,
                ["description"] = paper.PendingDescription,
                ["files"] = files,
                ["reviewNotes"] = new JsonArray([.. ReviewNotes(paper.PendingEditNote).Select(t => (JsonNode)t)]),
            };
        }

        /// <summary>The published file a pending row changes: the paper's file of its kind, or the extra file it names (none
        /// for a new extra file).</summary>
        private static PastPaperFile? PublishedFile(ICollection<PastPaperFile> files, PastPaperPendingFile row)
            => (row.Kind == PastPaperFileKind.Extra, row.CoreExtraFileId) switch
            {
                (false, _) => files.FirstOrDefault(f => f.Kind == row.Kind),
                (true, long id) => files.FirstOrDefault(f => f.Kind == PastPaperFileKind.Extra && f.CoreExtraFileId == id),
                _ => null,
            };

        private static PastPaperPendingFile MainRow(ICollection<PastPaperPendingFile> pending, long paperId, PastPaperFileKind kind)
        {
            var row = pending.FirstOrDefault(t => t.Kind == kind);
            if (row is null)
            {
                row = new() { PastPaperId = paperId, Kind = kind, CreationDate = DateTimeOffset.UtcNow };
                pending.Add(row);
            }

            return row;
        }

        private static void Stage(PastPaperPendingFile row, UploadedFile upload, ICollection<DroppedFile> dropped)
        {
            Drop(row, dropped);
            row.IsRemoval = false;
            row.Path = upload.StoredPath;
            row.Extension = upload.FileExtension;
            row.Size = upload.Size;
            row.PageCount = upload.Pages;
            row.Thumbnail = upload.Thumbnail;
            row.CreationDate = DateTimeOffset.UtcNow;
        }

        /// <summary>After a change to the edit: it waits for review while anything is left in it, and a rejection's notes go.</summary>
        private static void Touch(PastPaper paper)
        {
            paper.PendingEditDate = paper.PendingDescription is not null || paper.PendingFiles?.Count > 0 ? DateTimeOffset.UtcNow : null;
            paper.PendingEditNote = null;
        }

        private static void Drop(PastPaperPendingFile row, ICollection<DroppedFile> dropped)
        {
            Drop(row.Path, ContainerType.PastPaper, dropped);
            Drop(row.Thumbnail, ContainerType.Thumbnail, dropped);
        }

        private static void Drop(string? path, ContainerType container, ICollection<DroppedFile> dropped)
        {
            if (!string.IsNullOrEmpty(path))
            {
                dropped.Add(new(path, container));
            }
        }

        /// <summary>Removes from storage the files a saved change replaced or removed, each only when no paper, file or edit
        /// still names it (a copied path could be shared). Called after the save; a failure is logged, never the caller's.
        /// A deleted paper keeps its files (it can be restored), so this is never called for one.</summary>
        internal static async Task RemoveUnusedFilesAsync(IUnitOfWorkProvider uowProvider, Lazy<IFileService> files, ILogger logger, IReadOnlyCollection<DroppedFile> dropped)
        {
            if (dropped.Count == 0)
            {
                return;
            }

            try
            {
                var uow = uowProvider.CreateUnitOfWork();
                foreach (var file in dropped.Distinct())
                {
                    var used = file.Container == ContainerType.Thumbnail
                        ? await uow.GetRepository<PastPaper>().AnyAsync(t => t.Thumbnail == file.Path)
                            || await uow.GetRepository<PastPaperPendingFile>().AnyAsync(t => t.Thumbnail == file.Path)
                        : await uow.GetRepository<PastPaperFile>().AnyAsync(t => t.Path == file.Path)
                            || await uow.GetRepository<PastPaperPendingFile>().AnyAsync(t => t.Path == file.Path);
                    if (!used)
                    {
                        _ = await files.Value.RemoveFileAsync(new() { FileId = file.Path, ContainerType = file.Container });
                    }
                }
            }
            catch (Exception exc)
            {
                logger.LogException(exc);
            }
        }

        private static string TypeName(PastPaperFileKind kind) => kind.Value switch
        {
            var v when v == PastPaperFileKind.Question.Value => "pdf",
            var v when v == PastPaperFileKind.QuestionWord.Value => "word",
            var v when v == PastPaperFileKind.Answer.Value => "answer",
            _ => "extra",
        };
    }
}
