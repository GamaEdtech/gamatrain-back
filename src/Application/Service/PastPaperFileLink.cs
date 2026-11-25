namespace GamaEdtech.Application.Service
{
    using System.Text.Json.Nodes;

    using GamaEdtech.Application.Interface;
    using GamaEdtech.Common.Data;
    using GamaEdtech.Common.DataAccess.UnitOfWork;
    using GamaEdtech.Domain.Entity;
    using GamaEdtech.Domain.Enumeration;

    using Microsoft.EntityFrameworkCore;

    using static GamaEdtech.Common.Core.Constants;

    /// <summary>
    /// A short signed link to one of a paper's stored files, for someone who may see it in any status: an admin reviewing it
    /// (<c>PastPaperAdminService</c>) or its teacher checking what they uploaded (<c>NativePastPaperSource</c>). Not a
    /// download: nothing is charged or counted. Who may ask is the caller's check, not this one. See docs/business/past-papers.md.
    /// </summary>
    internal static class PastPaperFileLink
    {
        /// <summary>The link to the paper's file of <paramref name="fileType"/>: <c>pdf</c>, <c>word</c>, <c>answer</c> or
        /// <c>extra</c> (with <paramref name="extraId"/>); <c>{"url"}</c>. With <paramref name="pending"/>, the file of the edit
        /// waiting for review instead of the published one.</summary>
        public static async Task<ResultData<JsonObject>> GetAsync(IUnitOfWork uow, IFileService fileService, long paperId, string? fileType, long? extraId, bool pending = false)
        {
            if (PastPaperDownloadService.KindOf(fileType) is not { } kind)
            {
                return new(OperationResult.NotValid) { Errors = [new() { Message = "typeIsNotValid" }] };
            }

            // With pending, the file of the owner's edit waiting for review (a new extra file of it by -its row id).
            var pendingRowId = -(extraId ?? 0);
            var file = pending
                ? await uow.GetRepository<PastPaperPendingFile>()
                    .GetManyQueryable(t => t.PastPaperId == paperId && t.Kind == kind && t.Path != null && !t.IsRemoval
                        && (kind != PastPaperFileKind.Extra || (extraId < 0 ? t.CoreExtraFileId == null && t.Id == pendingRowId : t.CoreExtraFileId == extraId)))
                    .Select(t => new { t.Path, t.Kind })
                    .FirstOrDefaultAsync()
                : await uow.GetRepository<PastPaperFile>()
                    .GetManyQueryable(t => t.PastPaperId == paperId && t.Kind == kind && t.Path != null && (kind != PastPaperFileKind.Extra || t.CoreExtraFileId == extraId))
                    .Select(t => new { t.Path, t.Kind })
                    .FirstOrDefaultAsync();
            if (file?.Path is null)
            {
                return new(OperationResult.NotFound) { Errors = [new() { Message = "fileNotExist" }] };
            }

            var url = await fileService.GetFileUrlAsync(new()
            {
                FileId = file.Path,
                ContainerType = ContainerType.PastPaper,
                DownloadName = PastPaperDownloadService.DownloadName(paperId, file.Kind, file.Path),
            });
            return url is null
                ? new(OperationResult.Failed) { Errors = [new() { Message = "fileNotExist" }] }
                : new(OperationResult.Succeeded) { Data = new() { ["url"] = url.ToString() } };
        }
    }
}
