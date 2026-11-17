namespace GamaEdtech.Test.Application
{
    using System.Reflection;
    using System.Text;

    using GamaEdtech.Application.Interface;
    using GamaEdtech.Application.Service;
    using GamaEdtech.Common.Caching;
    using GamaEdtech.Common.Data;
    using GamaEdtech.Data.Dto.File;
    using GamaEdtech.Domain.Enumeration;

    using Microsoft.AspNetCore.Http;
    using Microsoft.Extensions.Logging.Abstractions;

    using SkiaSharp;

    using Xunit;

    using static GamaEdtech.Common.Core.Constants;

    /// <summary>
    /// Images inserted in the rich editors: only real JPG/PNG/GIF/WebP images of at most 5 MB, stored as WebP (an animated
    /// GIF as it is) with their metadata removed, under a new name in the public <c>EditorImage</c> container (never listed, replaced or deleted), at most 60 an
    /// hour per user.
    /// </summary>
    public sealed class EditorImageServiceTests
    {
        private readonly StorageFake storage = new();
        private readonly Dictionary<string, object?> cache = new(StringComparer.Ordinal);

        [Fact]
        public async Task AnImageIsStoredUnderANewNameAndAnsweredAsALink()
        {
            var result = await Service().UploadAsync(new() { UserId = 7, FileName = "../../other/photo.png", Content = Png() });

            Assert.Equal(OperationResult.Succeeded, result.OperationResult);
            Assert.Equal("https://cdn.test/editorimage/stored-1.webp", result.Data!.ToString());
            // The user's file name (and any path in it) is never the key.
            Assert.Equal(("image.webp", ContainerType.EditorImage), (storage.Names.Single(), storage.Containers.Single()));
        }

        [Fact]
        public async Task OnlyRealImagesOfAtMostFiveMegabytesAreTaken()
        {
            var service = Service();
            // A script renamed to an image is refused by its content.
            Assert.Equal(OperationResult.NotValid, (await service.UploadAsync(new() { UserId = 7, FileName = "x.png", Content = Encoding.ASCII.GetBytes("<svg onload=alert(1)>") })).OperationResult);
            Assert.Equal(OperationResult.NotValid, (await service.UploadAsync(new() { UserId = 7, FileName = "x.jpg", Content = new byte[(5 * 1024 * 1024) + 1] })).OperationResult);
            Assert.Equal(OperationResult.NotValid, (await service.UploadAsync(new() { UserId = 7, FileName = "x.jpg", Content = [] })).OperationResult);
            Assert.Empty(storage.Names);
        }

        [Fact]
        public void APhotosMetadataIsRemoved()
        {
            var photo = WithExif(Jpeg());
            Assert.Contains("Exif", Encoding.ASCII.GetString(photo), StringComparison.Ordinal);

            var (content, extension, contentType) = EditorImageService.Reencoded(photo, "jpg");

            Assert.Equal((".webp", "image/webp"), (extension, contentType));
            Assert.DoesNotContain("Exif", Encoding.ASCII.GetString(content!), StringComparison.Ordinal);
            Assert.Equal("webp", EditorImageService.Kind(content!));
        }

        [Fact]
        public void APngKeepsEveryPixel()
        {
            // A diagram's exact colours (lossless), transparency included.
            using SKBitmap diagram = new(8, 8, SKColorType.Rgba8888, SKAlphaType.Unpremul);
            diagram.Erase(SKColors.Transparent);
            diagram.SetPixel(3, 4, new SKColor(12, 34, 56));
            using var png = diagram.Encode(SKEncodedImageFormat.Png, 100);

            var (content, extension, _) = EditorImageService.Reencoded(png.ToArray(), "png");

            Assert.Equal(".webp", extension);
            using var decoded = SKBitmap.Decode(content!);
            Assert.Equal((new SKColor(12, 34, 56), (byte)0), (decoded.GetPixel(3, 4), decoded.GetPixel(0, 0).Alpha));
        }

        [Fact]
        public async Task AGifIsStoredAsItIsSoAnAnimationIsKept()
        {
            byte[] gif = [0x47, 0x49, 0x46, 0x38, 0x39, 0x61, 1, 0, 1, 0, 0, 0, 0, 0x3B];

            var result = await Service().UploadAsync(new() { UserId = 7, FileName = "a.gif", Content = gif });

            Assert.Equal(OperationResult.Succeeded, result.OperationResult);
            Assert.Equal("image.gif", storage.Names.Single());
        }

        [Fact]
        public async Task AUserMayUploadSixtyImagesAnHour()
        {
            var service = Service();
            for (var i = 0; i < EditorImageService.HourlyLimit; i++)
            {
                Assert.Equal(OperationResult.Succeeded, (await service.UploadAsync(new() { UserId = 7, FileName = "a.png", Content = Png() })).OperationResult);
            }

            Assert.Equal(OperationResult.NotValid, (await service.UploadAsync(new() { UserId = 7, FileName = "a.png", Content = Png() })).OperationResult);
            // Another user is not affected.
            Assert.Equal(OperationResult.Succeeded, (await service.UploadAsync(new() { UserId = 8, FileName = "a.png", Content = Png() })).OperationResult);
        }

        private EditorImageService Service() => new(
            new(() => null!),
            new(() => new HttpContextAccessor()),
            new(() => NullLogger<EditorImageService>.Instance),
            new(() => FileServiceFake.Create(storage)),
            new(() => CacheFake.Create(cache)));

        private static byte[] Png() => Encoded(SKEncodedImageFormat.Png);

        private static byte[] Jpeg() => Encoded(SKEncodedImageFormat.Jpeg);

        private static byte[] Encoded(SKEncodedImageFormat format)
        {
            using SKBitmap bitmap = new(4, 4);
            bitmap.Erase(SKColors.Teal);
            using var data = bitmap.Encode(format, 90);
            return data.ToArray();
        }

        /// <summary>The JPEG with an EXIF block (as a phone writes its camera and location) right after its start marker.</summary>
        private static byte[] WithExif(byte[] jpeg)
        {
            var payload = Encoding.ASCII.GetBytes("Exif\0\0GPS 35.7 51.4 camera");
            var length = payload.Length + 2;
            byte[] segment = [0xFF, 0xE1, (byte)(length >> 8), (byte)(length & 0xFF), .. payload];
            return [.. jpeg[..2], .. segment, .. jpeg[2..]];
        }

        public sealed class StorageFake
        {
            public System.Collections.ObjectModel.Collection<string> Names { get; } = [];

            public System.Collections.ObjectModel.Collection<ContainerType> Containers { get; } = [];
        }

        public class FileServiceFake : DispatchProxy
        {
            private StorageFake storage = new();

            internal static IFileService Create(StorageFake storage)
            {
                var fake = Create<IFileService, FileServiceFake>();
                ((FileServiceFake)(object)fake).storage = storage;
                return fake;
            }

            protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
            {
                ArgumentNullException.ThrowIfNull(targetMethod);
                ArgumentNullException.ThrowIfNull(args);
                switch (targetMethod.Name)
                {
                    case nameof(IFileService.CreateFileAsync):
                        var request = (CreateFileRequestDto)args[0]!;
                        storage.Names.Add(request.File.FileName);
                        storage.Containers.Add(request.ContainerType);
                        return Task.FromResult(new ResultData<string?>(OperationResult.Succeeded) { Data = $"stored-{storage.Names.Count}{Path.GetExtension(request.File.FileName)}" });
                    case nameof(IFileService.GetStaticFileUrl):
                        var uri = (FileUriRequestDto)args[0]!;
                        return $"https://cdn.test/{uri.ContainerType.Name.ToLowerInvariant()}/{uri.FileId}";
                    default:
                        // Listing or deleting must never be called.
                        throw new NotSupportedException(targetMethod.Name);
                }
            }
        }

        public class CacheFake : DispatchProxy
        {
            private Dictionary<string, object?> items = [];

            internal static ICacheProvider Create(Dictionary<string, object?> items)
            {
                var fake = Create<ICacheProvider, CacheFake>();
                ((CacheFake)(object)fake).items = items;
                return fake;
            }

            protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
            {
                ArgumentNullException.ThrowIfNull(targetMethod);
                ArgumentNullException.ThrowIfNull(args);
                return targetMethod.Name switch
                {
                    nameof(ICacheProvider.LockAsync) => Task.FromResult<IAsyncDisposable?>(Held.Instance),
                    nameof(ICacheProvider.GetAsync) => Task.FromResult((int?)items.GetValueOrDefault((string)args[0]!)),
                    nameof(ICacheProvider.SetAsync) => SetAsync((string)args[0]!, args[1]),
                    _ => throw new NotSupportedException(targetMethod.Name),
                };
            }

            private Task SetAsync(string key, object? value)
            {
                items[key] = value;
                return Task.CompletedTask;
            }

            private sealed class Held : IAsyncDisposable
            {
                public static readonly Held Instance = new();

                public ValueTask DisposeAsync() => ValueTask.CompletedTask;
            }
        }
    }
}
