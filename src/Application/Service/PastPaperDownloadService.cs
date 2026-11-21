namespace GamaEdtech.Application.Service
{
    using System.Globalization;

    using GamaEdtech.Application.Interface;
    using GamaEdtech.Common.Core;
    using GamaEdtech.Common.Data;
    using GamaEdtech.Common.DataAccess.UnitOfWork;
    using GamaEdtech.Common.Service;
    using GamaEdtech.Data.Dto.PastPaper;
    using GamaEdtech.Data.Dto.Provider.ContentDelivery;
    using GamaEdtech.Domain.Entity;
    using GamaEdtech.Domain.Entity.Identity;
    using GamaEdtech.Domain.Enumeration;

    using Microsoft.AspNetCore.Http;
    using Microsoft.EntityFrameworkCore;
    using Microsoft.Extensions.Logging;

    using static GamaEdtech.Common.Core.Constants;

    /// <inheritdoc cref="IPastPaperDownloadService"/>
    public sealed class PastPaperDownloadService(Lazy<IUnitOfWorkProvider> unitOfWorkProvider, Lazy<IHttpContextAccessor> httpContextAccessor,
        Lazy<ILogger<PastPaperDownloadService>> logger, Lazy<IFileService> fileService)
        : ServiceBase<PastPaperDownloadService>(unitOfWorkProvider, httpContextAccessor, logger), IPastPaperDownloadService
    {
        public async Task<ResultData<PastPaperDownloadDto>> GetFileAsync(long userId, long paperId, string? fileType, long? extraId)
        {
            try
            {
                var uow = UnitOfWorkProvider.Value.CreateUnitOfWork();
                var paper = await uow.GetRepository<PastPaper>()
                    .GetManyQueryable(t => t.Id == paperId && !t.IsDeleted, includes: t => t.Include(p => p.Files))
                    .FirstOrDefaultAsync();
                if (paper is null)
                {
                    return Refused("noResult");
                }

                // gama-api: the owner and managers download free; anyone else only a published paper.
                var privileged = (paper.OwnerUserId is long owner && owner == userId) || await IsAdminAsync(uow, userId);
                if (!privileged && paper.Status != PastPaperStatus.Confirmed)
                {
                    return Refused("accessDenied");
                }

                var kind = KindOf(fileType);
                if (kind is null)
                {
                    return new(OperationResult.NotValid) { Errors = [new() { Message = "typeIsNotValid" }] };
                }

                // A file counts only when it is stored (its checked size), as gama-api's is_file.
                var file = paper.Files?.FirstOrDefault(t => t.Kind == kind && t.Size > 0 && (kind != PastPaperFileKind.Extra || t.CoreExtraFileId == extraId));
                if (file?.Path is null)
                {
                    return Refused("fileNotExist");
                }

                var variant = kind == PastPaperFileKind.Extra ? $"{PastPaperFileKind.Extra.Name}:{Invariant(file.CoreExtraFileId)}" : kind.Name;
                return new(OperationResult.Succeeded)
                {
                    Data = new()
                    {
                        PaperId = paper.Id,
                        Variant = variant,
                        Points = await PriceAsync(uow, paper, file),
                        Privileged = privileged,
                        Path = file.Path,
                        DownloadName = DownloadName(paper.Id, kind, file.Path),
                        OwnerUserId = paper.OwnerUserId,
                    },
                };
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return new(OperationResult.Failed) { Errors = [new() { Message = exc.Message }] };
            }
        }

        public async Task<ResultData<Uri>> GetLinkAsync(PastPaperDownloadDto file)
        {
            ArgumentNullException.ThrowIfNull(file);
            try
            {
                var url = await fileService.Value.GetFileUrlAsync(new() { FileId = file.Path, ContainerType = ContainerType.PastPaper, DownloadName = file.DownloadName });
                if (url is null)
                {
                    return new(OperationResult.Failed) { Errors = [new() { Message = "fileNotExist" }] };
                }

                if (!file.Privileged)
                {
                    await CountDownloadAsync(file.PaperId);
                }

                return new(OperationResult.Succeeded) { Data = url };
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return new(OperationResult.Failed) { Errors = [new() { Message = exc.Message }] };
            }
        }

        public async Task<ResultData<GetPaperOwnershipResponseDto>> GetOwnershipAsync(long userId, long paperId)
        {
            try
            {
                var uow = UnitOfWorkProvider.Value.CreateUnitOfWork();
                var owner = await uow.GetRepository<PastPaper>().GetManyQueryable(t => t.Id == paperId).Select(t => t.OwnerUserId).FirstOrDefaultAsync();
                if (owner == userId || await IsAdminAsync(uow, userId))
                {
                    return new(OperationResult.Succeeded) { Data = new() { Pdf = true, Word = true, Answer = true } };
                }

                var owned = await uow.GetRepository<ContentPurchase>()
                    .GetManyQueryable(t => t.UserId == userId && t.ContentType == PurchasableContentType.PastPaper && t.ContentId == paperId)
                    .Select(t => t.Variant)
                    .ToListAsync();
                return new(OperationResult.Succeeded)
                {
                    Data = new()
                    {
                        Pdf = owned.Contains(PastPaperFileKind.Question.Name),
                        Word = owned.Contains(PastPaperFileKind.QuestionWord.Name),
                        Answer = owned.Contains(PastPaperFileKind.Answer.Name),
                    },
                };
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return new(OperationResult.Failed) { Errors = [new() { Message = exc.Message }] };
            }
        }

        /// <summary>gama-api's download name: <c>{id}_{qp|qm|ms|attached}_Gamatrain.com.{ext}</c> (it also adds a random
        /// part, which only kept its own temporary links apart). Internal for the tests.</summary>
        internal static string DownloadName(long paperId, PastPaperFileKind kind, string path)
        {
            var label = kind switch
            {
                _ when kind == PastPaperFileKind.QuestionWord => "qm",
                _ when kind == PastPaperFileKind.Answer => "ms",
                _ when kind == PastPaperFileKind.Extra => "attached",
                _ => "qp",
            };
            var extension = System.IO.Path.GetExtension(path);
            return string.Create(CultureInfo.InvariantCulture, $"{paperId}_{label}_Gamatrain.com{extension}");
        }

        /// <summary>A download's file type (<c>pdf</c>, <c>word</c>, <c>answer</c>, <c>extra</c>) as a file kind, or null.
        /// Internal for the admin panel's file links.</summary>
        internal static PastPaperFileKind? KindOf(string? fileType) => fileType switch
        {
            "pdf" => PastPaperFileKind.Question,
            "word" => PastPaperFileKind.QuestionWord,
            "answer" => PastPaperFileKind.Answer,
            "extra" => PastPaperFileKind.Extra,
            _ => null,
        };

        /// <summary>The price the paper page shows for this file (<c>PastPaperPricing</c>): a main file by its pages (Word by
        /// the question's) and the paper's multiplier, an extra file by its length and its type's multiplier.</summary>
        private static async Task<int> PriceAsync(IUnitOfWork uow, PastPaper paper, PastPaperFile file)
        {
            var rows = await uow.GetRepository<PastPaperFilePrice>().GetManyQueryable(t => t.CoreType == 1).OrderBy(t => t.Id)
                .Select(t => new { t.Extensions, t.BasePrice }).ToListAsync();
            var basePrices = PastPaperPricing.BasePrices(rows.Select(t => (t.Extensions, t.BasePrice)));
            var extension = System.IO.Path.GetExtension(file.Path)?.TrimStart('.');

            if (file.Kind == PastPaperFileKind.Extra)
            {
                var typeId = file.CoreTypeId ?? 0;
                var metadata = await uow.GetRepository<LegacyTaxonomyItem, int>().GetManyQueryable(t => t.Id == typeId).Select(t => t.Metadata).FirstOrDefaultAsync();
                return PastPaperPricing.Price(basePrices, extension, file.Length, paper.RefereeScore, PastPaperPricing.ExtraTypeMultiplier(metadata));
            }

            var pages = file.Kind == PastPaperFileKind.Answer
                ? file.PageCount
                : paper.Files?.FirstOrDefault(t => t.Kind == PastPaperFileKind.Question && t.Size > 0)?.PageCount;
            return PastPaperPricing.Price(basePrices, extension, pages, paper.RefereeScore, paper.PriceMultiplier);
        }

        private static Task<bool> IsAdminAsync(IUnitOfWork uow, long userId) =>
            uow.GetRepository<ApplicationUser>().GetManyQueryable(t => t.Id == userId && t.UserRoles!.Any(r => r.Role!.Name == nameof(Role.Admin))).AnyAsync();

        /// <summary>One more download, in one statement. Best effort: a failed count never fails the download.</summary>
        private async Task CountDownloadAsync(long paperId)
        {
            try
            {
                var uow = UnitOfWorkProvider.Value.CreateUnitOfWork();
                _ = await uow.GetRepository<PastPaper>().GetManyQueryable(t => t.Id == paperId)
                    .ExecuteUpdateAsync(t => t.SetProperty(p => p.DownloadCount, p => p.DownloadCount + 1));
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
            }
        }

        private static ResultData<PastPaperDownloadDto> Refused(string reason) => new(OperationResult.Failed) { Errors = [new() { Message = reason }] };

        private static string Invariant(long? value) => value?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
    }
}
