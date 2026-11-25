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
        /// <summary>The link to the paper's file <paramref name="fileId"/> (its id in the paper's <c>files</c>); <c>{"url"}</c>.
        /// With <paramref name="pending"/>, the file of the edit waiting for review instead: the new version of that published
        /// file, or (a negative id) a new file of the edit.</summary>
        public static async Task<ResultData<JsonObject>> GetAsync(IUnitOfWork uow, IFileService fileService, long paperId, long fileId, bool pending = false)
        {
            var live = fileId > 0
                ? await uow.GetRepository<PastPaperFile>().GetManyQueryable(t => t.Id == fileId && t.PastPaperId == paperId)
                    .Select(t => new { t.Path, t.Kind, t.CoreExtraFileId }).FirstOrDefaultAsync()
                : null;
            var file = live is null ? null : new { live.Path, live.Kind };
            if (pending)
            {
                var rows = uow.GetRepository<PastPaperPendingFile>().GetManyQueryable(t => t.PastPaperId == paperId && t.Path != null && !t.IsRemoval);
                var kind = live?.Kind;
                var extraId = live?.CoreExtraFileId;
                rows = (fileId < 0, kind) switch
                {
                    (true, _) => rows.Where(t => t.Id == -fileId),
                    (_, null) => rows.Where(t => false),
                    (_, { Value: 4 }) => rows.Where(t => t.Kind == PastPaperFileKind.Extra && t.CoreExtraFileId == extraId),
                    _ => rows.Where(t => t.Kind == kind),
                };
                file = await rows.Select(t => new { t.Path, t.Kind }).FirstOrDefaultAsync();
            }

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
