namespace GamaEdtech.Application.Service
{
    using System.Diagnostics.CodeAnalysis;
    using System.Linq;
    using System.Text.Json.Nodes;

    using GamaEdtech.Application.Interface;
    using GamaEdtech.Common.Core;
    using GamaEdtech.Common.Data;
    using GamaEdtech.Common.Data.Enumeration;
    using GamaEdtech.Common.DataAccess.UnitOfWork;
    using GamaEdtech.Common.Service;
    using GamaEdtech.Common.Service.Factory;
    using GamaEdtech.Data.Dto.PastPaper;
    using GamaEdtech.Domain.Entity;
    using GamaEdtech.Domain.Enumeration;
    using GamaEdtech.Infrastructure.Interface;

    using Microsoft.AspNetCore.Http;
    using Microsoft.EntityFrameworkCore;
    using Microsoft.Extensions.Logging;

    using static GamaEdtech.Common.Core.Constants;

    public sealed class PastPaperAdminService(Lazy<IUnitOfWorkProvider> unitOfWorkProvider, Lazy<IHttpContextAccessor> httpContextAccessor,
        Lazy<ILogger<PastPaperAdminService>> logger, Lazy<IMigrationRouter> migrationRouter, Lazy<IGenericFactory<IPastPaperSource, MigrationRoute>> sources,
        Lazy<IFileService> fileService)
        : ServiceBase<PastPaperAdminService>(unitOfWorkProvider, httpContextAccessor, logger), IPastPaperAdminService
    {
        // The statuses a review sets; Deleted and Edited come from deleting and editing.
        private static readonly byte[] ReviewStatuses = [0, 1, 2, 3, 4];

        public Task<ResultData<JsonObject>> GetPapersAsync(long userId, string? query) =>
            Native.GetOwnPapersAsync(new() { UserId = userId, Token = string.Empty, Query = query, Manager = true });

        public Task<ResultData<JsonObject>> GetPaperAsync(long userId, long id) =>
            Native.GetOwnPaperAsync(new() { UserId = userId, Token = string.Empty, Id = id, Manager = true });

        public async Task<ResultData<JsonObject>> GetFileLinkAsync(long id, string? fileType, long? extraId)
        {
            try
            {
                // Any status, deleted papers too: the review reads what the teacher sent. Not a download: nothing is charged or
                // counted, and it doesn't follow Migration:WriteRoutes:PastPapers.Downloads.
                return await PastPaperFileLink.GetAsync(UnitOfWorkProvider.Value.CreateUnitOfWork(), fileService.Value, id, fileType, extraId);
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return new(OperationResult.Failed) { Errors = [new() { Message = exc.Message }] };
            }
        }

        public Task<ResultData<JsonNode>> UploadAsync([NotNull] TeacherPaperUploadDto requestDto) =>
            WhenNativeAsync(() => Native.UploadPaperFileAsync(requestDto));

        public Task<ResultData<JsonNode>> AddAsync([NotNull] TeacherPaperRequestDto requestDto)
        {
            requestDto.Manager = true;
            return WhenNativeAsync(() => Native.AddPaperAsync(requestDto));
        }

        public Task<ResultData<JsonNode>> EditAsync([NotNull] TeacherPaperRequestDto requestDto)
        {
            requestDto.Manager = true;
            return WhenNativeAsync(() => Native.EditPaperAsync(requestDto));
        }

        public Task<ResultData<JsonNode>> DeleteAsync([NotNull] TeacherPaperRequestDto requestDto)
        {
            requestDto.Manager = true;
            return WhenNativeAsync(() => Native.DeletePaperAsync(requestDto));
        }

        public async Task<ResultData<JsonObject>> ReviewAsync([NotNull] PastPaperReviewDto requestDto)
        {
            try
            {
                if (NotNative<JsonObject>() is { } refused)
                {
                    return refused;
                }

                if (!ReviewStatuses.Contains(requestDto.Status))
                {
                    return new(OperationResult.NotValid) { Errors = [new() { Message = "The status is not a review's." }] };
                }

                List<string> notes = [.. (requestDto.Notes ?? []).Select(t => t.Trim()).Where(t => t.Length > 0)];
                if (notes.Count == 0 && requestDto.Status is 2 or 3 or 4)
                {
                    return new(OperationResult.NotValid) { Errors = [new() { Message = "Tell the teacher what to change." }] };
                }

                var uow = UnitOfWorkProvider.Value.CreateUnitOfWork();
                var paper = await uow.GetRepository<PastPaper>().GetManyQueryable(t => t.Id == requestDto.Id && !t.IsDeleted, tracking: true).FirstOrDefaultAsync();
                if (paper is null)
                {
                    return new(OperationResult.NotFound) { Errors = [new() { Message = "No result for this request." }] };
                }

                paper.Status = EnumerationExtensions.GetAll<PastPaperStatus, byte>()!.First(t => t!.Value == requestDto.Status)!;

                // The notes are shown as the teacher's edit form shows gama-api's: one "- " line each.
                if (notes.Count > 0)
                {
                    paper.ReviewNote = string.Join('\n', notes.Select(t => $"- {t}"));
                }

                _ = await uow.SaveChangesAsync();
                return new(OperationResult.Succeeded) { Data = new() { ["id"] = paper.Id, ["status"] = (int)paper.Status.Value } };
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return new(OperationResult.Failed) { Errors = [new() { Message = exc.Message }] };
            }
        }

        public async Task<ResultData<JsonObject>> SetScoreAsync(long id, byte score)
        {
            try
            {
                if (NotNative<JsonObject>() is { } refused)
                {
                    return refused;
                }

                if (score > 5)
                {
                    return new(OperationResult.NotValid) { Errors = [new() { Message = "The score is 0 to 5." }] };
                }

                var uow = UnitOfWorkProvider.Value.CreateUnitOfWork();
                var paper = await uow.GetRepository<PastPaper>().GetManyQueryable(t => t.Id == id && !t.IsDeleted, tracking: true).FirstOrDefaultAsync();
                if (paper is null)
                {
                    return new(OperationResult.NotFound) { Errors = [new() { Message = "No result for this request." }] };
                }

                paper.RefereeScore = score;
                _ = await uow.SaveChangesAsync();
                return new(OperationResult.Succeeded) { Data = new() { ["id"] = paper.Id, ["ref_score"] = (int)score } };
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return new(OperationResult.Failed) { Errors = [new() { Message = exc.Message }] };
            }
        }

        private IPastPaperSource Native => sources.Value.GetProvider(MigrationRoute.Native)
            ?? throw new InvalidOperationException("The native past paper source is not registered.");

        private Task<ResultData<JsonNode>> WhenNativeAsync(Func<Task<ResultData<JsonNode>>> call) =>
            NotNative<JsonNode>() is { } refused ? Task.FromResult(refused) : call();

        /// <summary>A refusal while gama-api is still where papers change, or none.</summary>
        private ResultData<T>? NotNative<T>() =>
            migrationRouter.Value.GetWriteRoute(MigrationDomain.PastPapers, MigrationFeatures.Teacher) == MigrationRoute.Native
                ? null
                : new(OperationResult.NotValid)
                {
                    Errors = [new() { Message = "Papers are managed on gama-api until Migration:WriteRoutes:PastPapers.Teacher is Native.", Reference = "notNative" }],
                };
    }
}
