namespace GamaEdtech.Test.Infrastructure
{
    using GamaEdtech.Domain.Enumeration;
    using GamaEdtech.Infrastructure.Provider.File;

    using Microsoft.AspNetCore.Hosting;
    using Microsoft.AspNetCore.Http;
    using Microsoft.Extensions.Configuration;
    using Microsoft.Extensions.FileProviders;
    using Microsoft.Extensions.Logging.Abstractions;

    using Xunit;

    using static GamaEdtech.Common.Core.Constants;

    /// <summary>
    /// Paper files are private: they go to the private bucket (never the public one, even when it is not set), get no
    /// permanent CDN link, and are only reached through a short signed link. Signing a link is local work, so no storage
    /// is contacted here.
    /// </summary>
    public sealed class PrivateFileStorageTests : IDisposable
    {
        private readonly string root = Path.Combine(Path.GetTempPath(), $"gama-private-files-{Guid.NewGuid():N}");

        [Fact]
        public async Task APaperLinkIsSignedForThePrivateBucketWithAShortLifetime()
        {
            var url = await S3(privateBucket: "private-files").GetFileUrlAsync(new()
            {
                FileId = "q.pdf",
                ContainerType = ContainerType.PastPaper,
                DownloadName = "Physics 9702 P1.pdf",
            });

            Assert.Equal(OperationResult.Succeeded, url.OperationResult);
            var link = url.Data!.ToString();
            Assert.Contains("/private-files/pastpaper/q.pdf", link, StringComparison.Ordinal);
            Assert.DoesNotContain("public-files", link, StringComparison.Ordinal);
            // Five minutes by default; the SDK counts from its own clock, so allow a few seconds.
            var expires = int.Parse(url.Data.Query.TrimStart('?').Split('&').Single(t => t.StartsWith("X-Amz-Expires=", StringComparison.Ordinal))["X-Amz-Expires=".Length..], System.Globalization.CultureInfo.InvariantCulture);
            Assert.InRange(expires, 290, 300);
            Assert.Contains("response-content-disposition=", link, StringComparison.Ordinal);
        }

        [Fact]
        public async Task OtherFilesStayInThePublicBucket()
        {
            var url = await S3(privateBucket: "private-files").GetFileUrlAsync(new() { FileId = "a.jpg", ContainerType = ContainerType.School });

            Assert.Contains("/public-files/school/a.jpg", url.Data!.ToString(), StringComparison.Ordinal);
        }

        [Fact]
        public async Task WithoutAPrivateBucketAPaperFailsInsteadOfUsingThePublicOne()
        {
            var provider = S3(privateBucket: null);

            var url = await provider.GetFileUrlAsync(new() { FileId = "q.pdf", ContainerType = ContainerType.PastPaper });
            var upload = await provider.UploadFileAsync(new() { File = [1, 2, 3], FileExtension = ".pdf", ContentType = "application/pdf", ContainerType = ContainerType.PastPaper });
            var remove = await provider.RemoveFileAsync(new() { FileId = "q.pdf", ContainerType = ContainerType.PastPaper });

            Assert.Equal((OperationResult.Failed, OperationResult.Failed, OperationResult.Failed), (url.OperationResult, upload.OperationResult, remove.OperationResult));
            Assert.Null(url.Data);
        }

        [Fact]
        public void PrivacyBelongsToTheContainerNotItsInstance()
        {
            Assert.True(ContainerType.PastPaper.IsPrivate());
            Assert.True(new ContainerType(nameof(ContainerType.PastPaper), ContainerType.PastPaper.Value).IsPrivate());
            Assert.All([ContainerType.Default, ContainerType.School, ContainerType.Post, ContainerType.Ticket, ContainerType.User], t => Assert.False(t.IsPrivate()));
        }

        [Fact]
        public void APaperHasNoPermanentLink()
        {
            var provider = S3(privateBucket: "private-files");

            Assert.Null(provider.GetStaticFileUrl(new() { FileId = "q.pdf", ContainerType = ContainerType.PastPaper }));
            Assert.Equal("https://cdn.example.test/school/a.jpg", provider.GetStaticFileUrl(new() { FileId = "a.jpg", ContainerType = ContainerType.School }));
        }

        [Fact]
        public async Task LocallyAPaperIsSavedOutsideTheWebRootAndGetsNoLink()
        {
            var provider = Local();

            var upload = await provider.UploadFileAsync(new() { File = [1, 2, 3], FileExtension = ".pdf", ContentType = "application/pdf", ContainerType = ContainerType.PastPaper });
            var url = await provider.GetFileUrlAsync(new() { FileId = upload.Data, ContainerType = ContainerType.PastPaper });

            Assert.Equal(OperationResult.Succeeded, upload.OperationResult);
            Assert.True(File.Exists(Path.Combine(root, "content", "PrivateFiles", "pastpaper", upload.Data!)));
            Assert.False(Directory.Exists(Path.Combine(root, "web")));
            Assert.Equal(OperationResult.Failed, url.OperationResult);

            var remove = await provider.RemoveFileAsync(new() { FileId = upload.Data, ContainerType = ContainerType.PastPaper });
            Assert.Equal(OperationResult.Succeeded, remove.OperationResult);
            Assert.False(File.Exists(Path.Combine(root, "content", "PrivateFiles", "pastpaper", upload.Data!)));
        }

        [Fact]
        public async Task LocallyPrivateFilesAreListedInPagesWithTheirSizes()
        {
            var provider = Local();
            foreach (var bytes in new[] { 1, 2, 3 })
            {
                _ = await provider.UploadFileAsync(new() { File = new byte[bytes], FileExtension = ".pdf", ContentType = "application/pdf", ContainerType = ContainerType.PastPaper });
            }

            var first = await provider.ListFilesAsync(new() { ContainerType = ContainerType.PastPaper, MaxCount = 2 });
            var second = await provider.ListFilesAsync(new() { ContainerType = ContainerType.PastPaper, StartAfter = first.Data!.NextStartAfter, MaxCount = 2 });

            Assert.Equal((2, 1), (first.Data.Files.Count, second.Data!.Files.Count));
            Assert.Null(second.Data.NextStartAfter);
            Assert.Equal([1L, 2, 3], first.Data.Files.Concat(second.Data.Files).Select(t => t.Size).Order());
        }

        public void Dispose()
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }

        private static IConfiguration Configuration(string? privateBucket) => new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Core:Cdn"] = "https://cdn.example.test",
            ["FileProvider:Local:Path"] = "Files",
            ["FileProvider:AmazonS3:BucketName"] = "public-files",
            ["FileProvider:AmazonS3:PrivateBucketName"] = privateBucket,
            ["FileProvider:AmazonS3:ServiceURL"] = "http://localhost:9000",
            ["FileProvider:AmazonS3:AccessKeyId"] = "test-access-key",
            ["FileProvider:AmazonS3:SecretAccessKey"] = "test-secret-key",
        }).Build();

        private static AmazonS3FileProvider S3(string? privateBucket) =>
            new(new(() => NullLogger<AmazonS3FileProvider>.Instance), new(() => Configuration(privateBucket)));

        private LocalFileProvider Local() => new(
            new(() => NullLogger<LocalFileProvider>.Instance),
            new(() => Configuration(null)),
            new(() => new Environment(Path.Combine(root, "content"), Path.Combine(root, "web"))),
            new(() => new HttpContextAccessor()));

        private sealed class Environment(string contentRoot, string webRoot) : IWebHostEnvironment
        {
            public string WebRootPath { get; set; } = webRoot;
            public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
            public string ApplicationName { get; set; } = "Test";
            public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
            public string ContentRootPath { get; set; } = contentRoot;
            public string EnvironmentName { get; set; } = "Test";
        }
    }
}
