namespace GamaEdtech.Application.Service
{
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

        /// <summary>Keeps the owner's edit of a published paper for review: the description and the file changes, merged into an
        /// edit already waiting. The paper and its files are not changed.</summary>
        private async Task<ResultData<JsonNode>> StageEditAsync(IUnitOfWork uow, PastPaper paper, JsonObject body, List<FileChange> changes, long userId, List<string> ignored)
        {
            var pending = paper.PendingFiles ??= [];
            List<DroppedFile> dropped = [];
            var changed = false;
            if (Field(body, "description") is { } description)
            {
                paper.PendingDescription = CorrectDescription(description);
                changed = true;
            }

            foreach (var change in changes)
            {
                PastPaperPendingFile? row = null;
                PastPaperFileKind kind;
                if (change.Id is < 0)
                {
                    // A new file of the edit, by -its row id.
                    row = pending.FirstOrDefault(t => t.Id == -change.Id.Value && !t.IsRemoval);
                    if (row is null)
                    {
                        continue;
                    }

                    if (change.Remove)
                    {
                        Drop(row, dropped);
                        _ = pending.Remove(row);
                        changed = true;
                        continue;
                    }

                    kind = row.Kind;
                }
                else if (change.Id is long id)
                {
                    if (paper.Files?.FirstOrDefault(t => t.Id == id) is not { } live)
                    {
                        continue;
                    }

                    kind = live.Kind;
                    if (change.Remove)
                    {
                        if (RemovalRefused(kind, mayRemove: true) is { } refused)
                        {
                            return refused;
                        }

                        StageRemoval(paper, kind, kind == PastPaperFileKind.Extra ? live.CoreExtraFileId : null, dropped);
                        changed = true;
                        continue;
                    }

                    var typeChanged = kind == PastPaperFileKind.Extra && change.TypeId is int t && t != live.CoreTypeId;
                    if (change.UploadKey is null && !typeChanged)
                    {
                        continue;
                    }

                    row = kind == PastPaperFileKind.Extra ? PendingRow(pending, paper.Id, kind, live.CoreExtraFileId, live.CoreTypeId) : MainRow(pending, paper.Id, kind);
                    row.IsRemoval = false;
                }
                else if (change.Kind is { } newKind)
                {
                    kind = newKind;
                    if (ReadUpload(change.UploadKey, userId, ExtensionsFor(kind)) is null)
                    {
                        continue;
                    }

                    row = kind == PastPaperFileKind.Extra
                        ? PendingRow(pending, paper.Id, kind, null, change.TypeId ?? 0, alwaysNew: true)
                        : MainRow(pending, paper.Id, kind);
                }
                else
                {
                    continue;
                }

                if (ReadUpload(change.UploadKey, userId, ExtensionsFor(kind)) is { } upload)
                {
                    Stage(row, upload, dropped);
                }

                if (kind == PastPaperFileKind.Extra && change.TypeId is int typeId)
                {
                    row.CoreTypeId = typeId;
                }

                changed = true;
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

        /// <summary>The edit waiting for review, for the edit form and the admin review: its description and its file changes
        /// (<c>id</c> the published file's, negative for a new file; <c>change</c> added, replaced, changed (an extra file's
        /// type) or removed), or a rejected edit's notes. Each file opens with <c>files/{id}?pending=true</c>. Null when there is
        /// neither.</summary>
        internal static JsonObject? PendingEditInfo(PastPaper paper)
        {
            if (paper.PendingEditDate is null && string.IsNullOrEmpty(paper.PendingEditNote))
            {
                return null;
            }

            JsonArray files = [];
            foreach (var row in (paper.PendingFiles ?? []).OrderBy(t => PastPaperFileRoles.Order(t.Kind)).ThenBy(t => t.Id))
            {
                // A change to a published file carries that file's id; a new file -its row id.
                var live = PublishedFile(paper.Files ?? [], row);
                var change = (row.IsRemoval, live is null, row.Path is null) switch
                {
                    (true, _, _) => "removed",
                    (_, true, _) => "added",
                    (_, _, true) => "changed",
                    _ => "replaced",
                };

                files.Add(new JsonObject
                {
                    ["id"] = live?.Id ?? -row.Id,
                    ["role"] = PastPaperFileRoles.Of(row.Kind),
                    ["change"] = change,
                    ["type_id"] = row.Kind == PastPaperFileKind.Extra ? row.CoreTypeId : null,
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

        /// <summary>The edit's row for an extra file: the one for that published extra file (<paramref name="extraId"/>), or a
        /// new one.</summary>
        private static PastPaperPendingFile PendingRow(ICollection<PastPaperPendingFile> pending, long paperId, PastPaperFileKind kind, long? extraId, int? typeId, bool alwaysNew = false)
        {
            var row = alwaysNew ? null : pending.FirstOrDefault(t => t.Kind == kind && t.CoreExtraFileId == extraId);
            if (row is null)
            {
                row = new() { PastPaperId = paperId, Kind = kind, CoreExtraFileId = extraId, CoreTypeId = typeId, CreationDate = DateTimeOffset.UtcNow };
                pending.Add(row);
            }

            return row;
        }

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
    }
}
