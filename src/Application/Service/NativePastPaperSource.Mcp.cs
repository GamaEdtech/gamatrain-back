namespace GamaEdtech.Application.Service
{
    using System.Diagnostics.CodeAnalysis;
    using System.Linq;

    using GamaEdtech.Common.Core;
    using GamaEdtech.Common.Data;
    using GamaEdtech.Common.DataAccess.UnitOfWork;
    using GamaEdtech.Data.Dto.ExamImport;
    using GamaEdtech.Domain.Entity;
    using GamaEdtech.Domain.Entity.Identity;
    using GamaEdtech.Domain.Enumeration;

    using Microsoft.EntityFrameworkCore;

    using static GamaEdtech.Common.Core.Constants;

    /// <summary>
    /// The MCP's paper reads (find a paper, the directory's search, a paper's files, a file's link) on this backend's copy,
    /// in the shape gama-api gave them (<c>ExamImportPastPaperDto</c>). The caller is <c>UserId</c> on each request. A file's
    /// link is given only when the file is free for the caller (their paper, an admin, no price, or already bought): the
    /// MCP never buys. See docs/business/exams-migration.md, "Native exam building".
    /// </summary>
    public sealed partial class NativePastPaperSource
    {
        /// <summary>gama-api's <c>ineedmore</c>: the directory search's candidates in one page.</summary>
        private const int MaxSearchPageSize = 300;

        public async Task<ResultData<IEnumerable<ExamImportPastPaperDto>>> GetPastPapersAsync([NotNull] ExamImportPastPapersRequestDto requestDto)
        {
            try
            {
                var uow = UnitOfWorkProvider.Value.CreateUnitOfWork();
                var query = PublishedPapers(uow);
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

                if (requestDto.ClassificationId is int classification)
                {
                    query = query.Where(t => t.CorePaperTypeId == classification);
                }

                if (requestDto.Year is int year)
                {
                    query = query.Where(t => t.EduYear == year);
                }

                if (requestDto.SessionMonth is int month)
                {
                    query = query.Where(t => t.EduMonth == month);
                }

                return await PageAsync(uow, query, requestDto.Page, Math.Clamp(requestDto.PageSize, 1, MaxPerPage), requestDto.UserId);
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return new(OperationResult.Failed) { Errors = [new() { Message = exc.Message }] };
            }
        }

        public async Task<ResultData<IEnumerable<ExamImportPastPaperDto>>> SearchPastPapersAsync([NotNull] ExamImportPastPapersRequestDto requestDto)
        {
            try
            {
                var uow = UnitOfWorkProvider.Value.CreateUnitOfWork();
                var query = PublishedPapers(uow);
                if (!string.IsNullOrWhiteSpace(requestDto.Title))
                {
                    var title = requestDto.Title.Trim();
                    query = query.Where(t => t.Title != null && t.Title.Contains(title));
                }

                if (requestDto.Year is int year)
                {
                    query = query.Where(t => t.EduYear == year);
                }

                return await PageAsync(uow, query, requestDto.Page, Math.Clamp(requestDto.PageSize, 1, MaxSearchPageSize), requestDto.UserId);
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return new(OperationResult.Failed) { Errors = [new() { Message = exc.Message }] };
            }
        }

        public async Task<ResultData<ExamImportPastPaperDto>> GetPastPaperAsync([NotNull] ExamImportRequestDto requestDto)
        {
            try
            {
                var uow = UnitOfWorkProvider.Value.CreateUnitOfWork();
                var paper = await uow.GetRepository<PastPaper>()
                    .GetManyQueryable(t => t.Id == requestDto.Id && !t.IsDeleted, includes: t => t.Include(p => p.Files))
                    .FirstOrDefaultAsync();
                var managed = paper is not null && await ManagesAsync(uow, paper, requestDto.UserId);
                if (paper is null || (paper.Status != PastPaperStatus.Confirmed && !managed))
                {
                    return NoResult<ExamImportPastPaperDto>();
                }

                var context = await LoadContextAsync(uow, [paper]);
                var item = McpPaper(paper, context, managed);

                // Which files are free for the caller: their own or an admin's, no price, or bought (here or on gama-api).
                var bought = requestDto.UserId > 0
                    ? (await contentPurchaseService.Value.GetPurchasedVariantsAsync(requestDto.UserId, PurchasableContentType.PastPaper, paper.Id)).Data ?? []
                    : [];
                List<ExamImportPastPaperDto.FileDto> files = [];
                foreach (var file in item.Files ?? [])
                {
                    var found = await pastPaperDownloadService.Value.GetFileAsync(requestDto.UserId, paper.Id, file.Id ?? 0);
                    files.Add(new()
                    {
                        Id = file.Id,
                        Type = file.Type,
                        Label = file.Label,
                        Extension = file.Extension,
                        Free = found.Data is { } data && (data.Privileged || data.Points == 0 || bought.Contains(data.Variant)),
                    });
                }

                item.Files = files;
                return new(OperationResult.Succeeded) { Data = item };
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return new(OperationResult.Failed) { Errors = [new() { Message = exc.Message }] };
            }
        }

        public async Task<ResultData<Uri>> GetPastPaperFileUrlAsync([NotNull] ExamImportPaperFileRequestDto requestDto)
        {
            try
            {
                if (requestDto.FileId is not long fileId)
                {
                    return new(OperationResult.NotValid) { Errors = [new() { Message = "The file's id is required." }] };
                }

                var file = await pastPaperDownloadService.Value.GetFileAsync(requestDto.UserId, requestDto.PaperId, fileId);
                if (file.OperationResult is not OperationResult.Succeeded || file.Data is null)
                {
                    return new(file.OperationResult) { Errors = file.Errors };
                }

                var free = file.Data.Privileged || file.Data.Points == 0;
                if (!free && requestDto.UserId > 0)
                {
                    var bought = await contentPurchaseService.Value.GetPurchasedVariantsAsync(requestDto.UserId, PurchasableContentType.PastPaper, requestDto.PaperId);
                    free = bought.Data?.Contains(file.Data.Variant) == true;
                }

                // The MCP only reads files it can have for free; a paid file is bought on the website.
                return free
                    ? await pastPaperDownloadService.Value.GetLinkAsync(file.Data)
                    : new(OperationResult.Failed) { Errors = [new() { Message = "This file must be bought on Gamatrain first.", Reference = "notPaid" }] };
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return new(OperationResult.Failed) { Errors = [new() { Message = exc.Message }] };
            }
        }

        private static IQueryable<PastPaper> PublishedPapers(IUnitOfWork uow) => uow.GetRepository<PastPaper>()
            .GetManyQueryable(t => t.Status == PastPaperStatus.Confirmed && !t.IsDeleted && t.IsPaper, includes: t => t.Include(p => p.Files));

        private static async Task<ResultData<IEnumerable<ExamImportPastPaperDto>>> PageAsync(IUnitOfWork uow, IQueryable<PastPaper> query, int page, int size, long userId)
        {
            var papers = await query.OrderByDescending(t => t.Id).Skip((Math.Max(page, 1) - 1) * size).Take(size).ToListAsync();
            var context = await LoadContextAsync(uow, papers);
            var admin = userId > 0 && await IsAdminAsync(uow, userId);
            return new(OperationResult.Succeeded) { Data = [.. papers.Select(t => McpPaper(t, context, admin || (t.OwnerUserId is long owner && owner == userId)))] };
        }

        private static ExamImportPastPaperDto McpPaper(PastPaper paper, Context context, bool managed)
        {
            string? Title(long? id) => id is long key && context.Items.TryGetValue(key, out var item) ? item.Title?.Trim() : null;
            List<ExamImportPastPaperDto.FileDto> files = [.. StoredFiles(paper).Select(t => new ExamImportPastPaperDto.FileDto
            {
                Id = t.Id,
                Type = PastPaperFileRoles.Of(t.Kind),
                Label = t.Kind == PastPaperFileKind.Extra ? Title(t.CoreTypeId) : null,
                Extension = Extension(t.Path),
            })];
            return new()
            {
                Id = paper.Id,
                Title = paper.Title,
                BoardId = paper.CoreBoardId,
                Board = Title(paper.CoreBoardId),
                GradeId = paper.CoreGradeId,
                Grade = Title(paper.CoreGradeId),
                CourseId = paper.CoreCourseId is > 0 ? paper.CoreCourseId : null,
                SubjectId = paper.CoreSubjectId,
                Subject = Title(paper.CoreSubjectId),
                Classification = Title(paper.CorePaperTypeId),
                Year = paper.EduYear is > 0 ? paper.EduYear : null,
                Month = paper.EduMonth is > 0 ? paper.EduMonth : null,
                ExamLinked = !string.IsNullOrWhiteSpace(paper.CoreExamIds) && paper.CoreExamIds.Any(char.IsAsciiDigit),
                Managed = managed,
                Files = files,
            };
        }

        private static async Task<bool> ManagesAsync(IUnitOfWork uow, PastPaper paper, long userId) =>
            userId > 0 && ((paper.OwnerUserId is long owner && owner == userId) || await IsAdminAsync(uow, userId));

        private static Task<bool> IsAdminAsync(IUnitOfWork uow, long userId) =>
            uow.GetRepository<ApplicationUser>().GetManyQueryable(t => t.Id == userId && t.UserRoles!.Any(r => r.Role!.Name == nameof(Role.Admin))).AnyAsync();
    }
}
