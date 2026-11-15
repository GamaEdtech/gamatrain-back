namespace GamaEdtech.Application.Service
{
    using System.Linq;

    using GamaEdtech.Application.Interface;
    using GamaEdtech.Common.Core;
    using GamaEdtech.Common.Data;
    using GamaEdtech.Common.DataAccess.UnitOfWork;
    using GamaEdtech.Common.Service;
    using GamaEdtech.Data.Dto.PastPaper;
    using GamaEdtech.Domain.Entity;
    using GamaEdtech.Domain.Enumeration;

    using Microsoft.AspNetCore.Http;
    using Microsoft.EntityFrameworkCore;
    using Microsoft.Extensions.Logging;

    using static GamaEdtech.Common.Core.Constants;

    public sealed class PastPaperFileCheckService(Lazy<IUnitOfWorkProvider> unitOfWorkProvider, Lazy<IHttpContextAccessor> httpContextAccessor,
        Lazy<ILogger<PastPaperFileCheckService>> logger, Lazy<IFileService> fileService)
        : ServiceBase<PastPaperFileCheckService>(unitOfWorkProvider, httpContextAccessor, logger), IPastPaperFileCheckService
    {
        private const int BatchSize = 1000;
        private const int MaxPages = 10_000;

        public async Task<ResultData<PastPaperFileCheckResultDto>> CheckAsync()
        {
            try
            {
                // 1. Everything stored, before any row is touched: a listing that fails part way changes nothing.
                Dictionary<string, long> stored = new(StringComparer.Ordinal);
                string? after = null;
                for (var page = 0; page < MaxPages; page++)
                {
                    var list = await fileService.Value.ListFilesAsync(new() { ContainerType = ContainerType.PastPaper, StartAfter = after, MaxCount = 1000 });
                    if (list.OperationResult is not OperationResult.Succeeded || list.Data is null)
                    {
                        return new(list.OperationResult) { Errors = list.Errors };
                    }

                    foreach (var file in list.Data.Files)
                    {
                        stored[file.Name] = file.Size;
                    }

                    after = list.Data.NextStartAfter;
                    if (after is null)
                    {
                        break;
                    }
                }

                // 2. Every row, a batch at a time.
                PastPaperFileCheckResultDto result = new() { StoredFiles = stored.Count };
                HashSet<string> used = new(StringComparer.Ordinal);
                List<string> missing = [];
                var now = DateTimeOffset.UtcNow;
                long afterId = 0;
                while (true)
                {
                    var uow = UnitOfWorkProvider.Value.CreateUnitOfWork();
                    var batch = await uow.GetRepository<PastPaperFile>().GetManyQueryable(t => t.Id > afterId, tracking: true)
                        .OrderBy(t => t.Id).Take(BatchSize).ToListAsync();
                    if (batch.Count == 0)
                    {
                        break;
                    }

                    foreach (var file in batch)
                    {
                        file.CheckDate = now;
                        if (file.Path is not null && stored.TryGetValue(file.Path, out var size))
                        {
                            file.Size = size;
                            _ = used.Add(file.Path);
                            result.Found++;
                        }
                        else
                        {
                            file.Size = null;
                            result.Missing++;
                            if (missing.Count < 20)
                            {
                                missing.Add(file.Path ?? $"(no path, file {file.Id})");
                            }
                        }
                    }

                    _ = await uow.SaveChangesAsync();
                    result.Rows += batch.Count;
                    afterId = batch[^1].Id;
                }

                result.Unused = stored.Count - used.Count;
                result.MissingExamples = missing;
                Logger.Value.LogWarning("Past paper file check: {Stored} stored, {Rows} rows, {Found} found, {Missing} missing, {Unused} unused",
                    result.StoredFiles, result.Rows, result.Found, result.Missing, result.Unused);
                return new(OperationResult.Succeeded) { Data = result };
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return new(OperationResult.Failed) { Errors = [new() { Message = exc.Message }] };
            }
        }
    }
}
