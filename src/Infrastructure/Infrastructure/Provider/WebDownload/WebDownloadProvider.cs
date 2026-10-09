namespace GamaEdtech.Infrastructure.Provider.WebDownload
{
    using System;
    using System.Diagnostics.CodeAnalysis;

    using GamaEdtech.Common.Data;
    using GamaEdtech.Common.HttpProvider;
    using GamaEdtech.Infrastructure.Interface;

    using static GamaEdtech.Common.Core.Constants;

    public sealed class WebDownloadProvider(Lazy<IHttpClientFactory> httpClientFactory) : IWebDownloadProvider
    {
        public async Task<ResultData<byte[]>> DownloadAsync([NotNull] Uri url, int maxBytes, CancellationToken cancellationToken)
        {
            if (!url.IsAbsoluteUri || (url.Scheme != Uri.UriSchemeHttps && url.Scheme != Uri.UriSchemeHttp))
            {
                return Fail("The link must be an http(s) URL.");
            }

            try
            {
                using var client = httpClientFactory.Value.CreateClient(PublicNetworkHttpClient.Name);
                using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                if (!response.IsSuccessStatusCode)
                {
                    return Fail($"The link answered HTTP {(int)response.StatusCode}.");
                }

                await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
                using MemoryStream content = new();
                var chunk = new byte[81920];
                int read;
                while ((read = await stream.ReadAsync(chunk, cancellationToken)) > 0)
                {
                    await content.WriteAsync(chunk.AsMemory(0, read), cancellationToken);
                    if (content.Length > maxBytes)
                    {
                        return Fail($"The file is larger than {maxBytes / 1024 / 1024} MB.");
                    }
                }

                return new(OperationResult.Succeeded) { Data = content.ToArray() };
            }
            catch (HttpRequestException exc)
            {
                return Fail($"The file could not be downloaded: {exc.Message}");
            }
            catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                return Fail("Downloading the file timed out.");
            }

            static ResultData<byte[]> Fail(string message) => new(OperationResult.NotValid) { Errors = [new() { Message = message }] };
        }
    }
}
