namespace GamaEdtech.Application.Service
{
    using System.Diagnostics.CodeAnalysis;
    using System.Linq;

    using EntityFramework.Exceptions.Common;

    using GamaEdtech.Application.Interface;
    using GamaEdtech.Common.Core;
    using GamaEdtech.Common.Data;
    using GamaEdtech.Common.DataAccess.UnitOfWork;
    using GamaEdtech.Common.Service;
    using GamaEdtech.Data.Dto.PastPaper;
    using GamaEdtech.Domain.Entity;

    using Microsoft.AspNetCore.Http;
    using Microsoft.EntityFrameworkCore;
    using Microsoft.Extensions.Logging;

    using static GamaEdtech.Common.Core.Constants;

    /// <summary>
    /// The admin's past paper base price list. Only rows for past paper files (<c>CoreType</c> 1, every paper type) are
    /// read by <c>PastPaperPricing</c>, so those are the only ones listed and managed. See docs/business/past-papers.md,
    /// "Prices".
    /// </summary>
    public sealed class PastPaperFilePriceService(Lazy<IUnitOfWorkProvider> unitOfWorkProvider, Lazy<IHttpContextAccessor> httpContextAccessor,
        Lazy<ILogger<PastPaperFilePriceService>> logger)
        : ServiceBase<PastPaperFilePriceService>(unitOfWorkProvider, httpContextAccessor, logger), IPastPaperFilePriceService
    {
        /// <summary>The price kind <c>PastPaperPricing</c> reads (1, paper files; the value the copied rows carry).</summary>
        public const byte PaperFileType = 1;

        public const int MaxBasePrice = 100_000;
        private const int MaxExtensionsLength = 50;
        private const int MaxExtensionLength = 10;

        public async Task<ResultData<IReadOnlyList<PastPaperFilePriceDto>>> GetPricesAsync()
        {
            try
            {
                var rows = await UnitOfWorkProvider.Value.CreateUnitOfWork().GetRepository<PastPaperFilePrice>()
                    .GetManyQueryable(t => t.CoreType == PaperFileType).OrderBy(t => t.Id)
                    .Select(t => new PastPaperFilePriceDto { Id = t.Id, Extensions = t.Extensions ?? string.Empty, BasePrice = t.BasePrice, SyncDate = t.SyncDate })
                    .ToListAsync();
                return new(OperationResult.Succeeded) { Data = rows };
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return new(OperationResult.Failed) { Errors = [new() { Message = exc.Message }] };
            }
        }

        public async Task<ResultData<long>> ManagePriceAsync([NotNull] ManagePastPaperFilePriceRequestDto requestDto)
        {
            try
            {
                if (requestDto.BasePrice is < 0 or > MaxBasePrice)
                {
                    return NotValid<long>($"The base price must be between 0 and {MaxBasePrice}.", "basePrice");
                }

                var extensions = NormalizeExtensions(requestDto.Extensions);
                if (extensions is null)
                {
                    return NotValid<long>($"Give one or more file extensions, comma-separated (letters and digits, up to {MaxExtensionLength} each, e.g. \"doc,docx\").", "extensions");
                }

                var uow = UnitOfWorkProvider.Value.CreateUnitOfWork();
                var repository = uow.GetRepository<PastPaperFilePrice>();
                var rows = await repository.GetManyQueryable(t => t.CoreType == PaperFileType, tracking: true).ToListAsync();

                PastPaperFilePrice? row = null;
                if (requestDto.Id.HasValue)
                {
                    row = rows.Find(t => t.Id == requestDto.Id.Value);
                    if (row is null)
                    {
                        return NotFound<long>();
                    }
                }

                var taken = rows.Where(t => t != row)
                    .SelectMany(t => (t.Extensions ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                    .Select(t => t.ToLowerInvariant())
                    .Intersect(extensions.Split(','))
                    .ToList();
                if (taken.Count > 0)
                {
                    return NotValid<long>($"Already priced by another row: {string.Join(", ", taken)}.", "extensions");
                }

                var now = DateTimeOffset.UtcNow;
                if (row is null)
                {
                    // Ids are not generated: copied rows keep gama-api's, so a new one takes the next free id.
                    var maxId = await repository.GetManyQueryable().MaxAsync(t => (long?)t.Id) ?? 0;
                    row = new()
                    {
                        Id = maxId + 1,
                        CoreType = PaperFileType,
                        CorePaperTypeId = 0,
                        Extensions = extensions,
                        BasePrice = requestDto.BasePrice,
                        SyncDate = now,
                    };
                    repository.Add(row);
                }
                else
                {
                    row.Extensions = extensions;
                    row.BasePrice = requestDto.BasePrice;
                    row.SyncDate = now;
                }

                _ = await uow.SaveChangesAsync();
                return new(OperationResult.Succeeded) { Data = row.Id };
            }
            catch (UniqueConstraintException)
            {
                return new(OperationResult.Duplicate) { Errors = [new() { Message = "Another admin added a price at the same time; try again.", Reference = "conflict" }] };
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return new(OperationResult.Failed) { Errors = [new() { Message = exc.Message }] };
            }
        }

        public async Task<ResultData<bool>> DeletePriceAsync(long id)
        {
            try
            {
                var uow = UnitOfWorkProvider.Value.CreateUnitOfWork();
                var repository = uow.GetRepository<PastPaperFilePrice>();
                var row = await repository.GetManyQueryable(t => t.Id == id && t.CoreType == PaperFileType, tracking: true).FirstOrDefaultAsync();
                if (row is null)
                {
                    return NotFound<bool>();
                }

                repository.Remove(row);
                _ = await uow.SaveChangesAsync();
                return new(OperationResult.Succeeded) { Data = true };
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return new(OperationResult.Failed) { Errors = [new() { Message = exc.Message }] };
            }
        }

        /// <summary>
        /// Lower case, no dots or spaces, no repeats, comma-separated (" .PDF, doc " gives <c>pdf,doc</c>); null when empty,
        /// an extension isn't letters and digits, or the list is too long for the column. Internal for the tests.
        /// </summary>
        internal static string? NormalizeExtensions(string? value)
        {
            var parts = (value ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(t => t.TrimStart('.').ToLowerInvariant())
                .Distinct(StringComparer.Ordinal)
                .ToList();
            if (parts.Count == 0 || parts.Exists(t => t.Length is 0 or > MaxExtensionLength || !t.All(c => c is (>= 'a' and <= 'z') or (>= '0' and <= '9'))))
            {
                return null;
            }

            var joined = string.Join(',', parts);
            return joined.Length > MaxExtensionsLength ? null : joined;
        }

        private static ResultData<T> NotValid<T>(string message, string reference) =>
            new(OperationResult.NotValid) { Errors = [new() { Message = message, Reference = reference }] };

        private static ResultData<T> NotFound<T>() =>
            new(OperationResult.NotFound) { Errors = [new() { Message = "No such price.", Reference = "notFound" }] };
    }
}
