namespace GamaEdtech.Application.Service
{
    using System.Diagnostics.CodeAnalysis;

    using GamaEdtech.Application.Interface;
    using GamaEdtech.Common.Caching;
    using GamaEdtech.Common.Core;
    using GamaEdtech.Common.Data;
    using GamaEdtech.Common.DataAccess.UnitOfWork;
    using GamaEdtech.Common.Service;
    using GamaEdtech.Data.Dto.File;
    using GamaEdtech.Domain.Enumeration;

    using Microsoft.AspNetCore.Http;
    using Microsoft.Extensions.Caching.Distributed;
    using Microsoft.Extensions.Logging;

    using SkiaSharp;

    using static GamaEdtech.Common.Core.Constants;

    public sealed class EditorImageService(Lazy<IUnitOfWorkProvider> unitOfWorkProvider, Lazy<IHttpContextAccessor> httpContextAccessor,
        Lazy<ILogger<EditorImageService>> logger, Lazy<IFileService> fileService, Lazy<ICacheProvider> cacheProvider)
        : ServiceBase<EditorImageService>(unitOfWorkProvider, httpContextAccessor, logger), IEditorImageService
    {
        private const int JpegQuality = 90;

        /// <summary>Images one user may upload in an hour: enough to write any post, too few to fill the bucket.</summary>
        internal const int HourlyLimit = 60;

        public async Task<ResultData<Uri>> UploadAsync([NotNull] EditorImageUploadDto requestDto)
        {
            try
            {
                if (requestDto.Content.LongLength is 0 or > IEditorImageService.MaxBytes)
                {
                    return Refuse("The image must be at most 5 MB.");
                }

                if (!await CountUploadAsync(requestDto.UserId))
                {
                    return Refuse($"At most {HourlyLimit} images an hour; please try again later.");
                }

                // The kind comes from the bytes, never from the name: a renamed file is refused.
                var kind = Kind(requestDto.Content);
                if (kind is null)
                {
                    return Refuse("Only JPG, PNG, GIF or WebP images can be inserted.");
                }

                var (content, extension, contentType) = kind == "gif" ? (requestDto.Content, ".gif", "image/gif") : Reencoded(requestDto.Content, kind);
                if (content is null)
                {
                    return Refuse("The image could not be read.");
                }

                using MemoryStream stream = new(content);
                FormFile file = new(stream, 0, content.LongLength, "file", "image" + extension) { Headers = new HeaderDictionary(), ContentType = contentType };
                var stored = await fileService.Value.CreateFileAsync(new() { File = file, ContainerType = ContainerType.EditorImage });
                if (stored.OperationResult is not OperationResult.Succeeded || string.IsNullOrEmpty(stored.Data))
                {
                    return new(OperationResult.Failed) { Errors = stored.Errors ?? [new() { Message = "The image could not be stored." }] };
                }

                var url = fileService.Value.GetStaticFileUrl(new() { FileId = stored.Data, ContainerType = ContainerType.EditorImage });
                return Uri.TryCreate(url, UriKind.Absolute, out var address)
                    ? new(OperationResult.Succeeded) { Data = address }
                    : new(OperationResult.Failed) { Errors = [new() { Message = "The image has no public address." }] };
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return new(OperationResult.Failed) { Errors = [new() { Message = exc.Message }] };
            }
        }

        /// <summary>The image kind from its first bytes (JPG, PNG, GIF, WebP), or none. Internal for the tests.</summary>
        internal static string? Kind(byte[] content) => content switch
        {
            [0xFF, 0xD8, 0xFF, ..] => "jpg",
            [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, ..] => "png",
            [0x47, 0x49, 0x46, 0x38, _, 0x61, ..] => "gif",
            [0x52, 0x49, 0x46, 0x46, _, _, _, _, 0x57, 0x45, 0x42, 0x50, ..] => "webp",
            _ => null,
        };

        /// <summary>The image decoded and encoded again in its own format, which drops every metadata block (EXIF, GPS,
        /// comments). Null when it can't be decoded. Internal for the tests.</summary>
        internal static (byte[]? Content, string Extension, string ContentType) Reencoded(byte[] content, string kind)
        {
            var (format, extension, contentType) = kind switch
            {
                "png" => (SKEncodedImageFormat.Png, ".png", "image/png"),
                "webp" => (SKEncodedImageFormat.Webp, ".webp", "image/webp"),
                _ => (SKEncodedImageFormat.Jpeg, ".jpg", "image/jpeg"),
            };

            using var bitmap = SKBitmap.Decode(content);
            if (bitmap is null)
            {
                return (null, extension, contentType);
            }

            using var data = bitmap.Encode(format, JpegQuality);
            return (data?.ToArray(), extension, contentType);
        }

        /// <summary>Counts one upload for the user in the current hour, under a lock (one counter for every instance of the
        /// app). False once the hour's limit is reached.</summary>
        private async Task<bool> CountUploadAsync(long userId)
        {
            var key = $"EditorImages:{userId}:{DateTimeOffset.UtcNow:yyyyMMddHH}";
            await using var held = await cacheProvider.Value.LockAsync($"{key}:lock", TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(5));
            if (held is null)
            {
                return false;
            }

            var count = await cacheProvider.Value.GetAsync<int?>(key) ?? 0;
            if (count >= HourlyLimit)
            {
                return false;
            }

            await cacheProvider.Value.SetAsync(key, count + 1, new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(1) });
            return true;
        }

        private static ResultData<Uri> Refuse(string message) => new(OperationResult.NotValid) { Errors = [new() { Message = message }] };
    }
}
