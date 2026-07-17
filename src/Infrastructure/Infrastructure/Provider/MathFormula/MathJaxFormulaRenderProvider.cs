namespace GamaEdtech.Infrastructure.Provider.MathFormula
{
    using System;
    using System.Diagnostics.CodeAnalysis;
    using System.Threading;
    using System.Threading.Tasks;

    using GamaEdtech.Common.Core;
    using GamaEdtech.Common.Data;
    using GamaEdtech.Common.DataAnnotation;
    using GamaEdtech.Infrastructure.Interface;

    using Microsoft.AspNetCore.Hosting;
    using Microsoft.Extensions.Logging;

    using PuppeteerSharp;

    using static GamaEdtech.Common.Core.Constants;

    /// <summary>
    /// Renders MathJax-style inline LaTeX ($...$) to raster images using the real MathJax engine running
    /// inside a headless Chromium instance (PuppeteerSharp), so malformed/non-standard LaTeX in exam content
    /// renders exactly as it does for students in the live MathJax-powered UI, instead of failing on a
    /// partial LaTeX parser. Kept as a process-lifetime singleton: launching Chromium per request is far too
    /// slow, and MathJax's script (vendored at wwwroot/lib/mathjax/tex-svg.js) is loaded into each page once.
    /// </summary>
    [ServiceLifetime(Microsoft.Extensions.DependencyInjection.ServiceLifetime.Singleton)]
    public sealed class MathJaxFormulaRenderProvider(Lazy<IWebHostEnvironment> environment, Lazy<ILogger<MathJaxFormulaRenderProvider>> logger)
        : IMathFormulaRenderProvider, IAsyncDisposable
    {
        // Each render opens a Chromium tab that parses/executes a 2MB script and rasterizes canvases --
        // real CPU and memory cost, not just I/O wait. Left unbounded, a burst of concurrent export
        // requests (e.g. a class exporting exams right before a test) can exhaust the host and crash the
        // shared browser for everyone. Cap concurrent renders to the machine's core count; the rest queue.
        private static readonly int MaxConcurrentRenders = Math.Max(1, Environment.ProcessorCount);

        private readonly SemaphoreSlim launchLock = new(1, 1);
        private readonly SemaphoreSlim renderLock = new(MaxConcurrentRenders, MaxConcurrentRenders);
        private IBrowser? browser;

        public async Task<ResultData<string>> RenderFormulasAsync([NotNull] string html)
        {
            if (!html.Contains('$', StringComparison.Ordinal))
            {
                return new(OperationResult.Succeeded) { Data = html };
            }

            await renderLock.WaitAsync();
            IPage? page = null;
            try
            {
                var browserInstance = await GetBrowserAsync();
                page = await browserInstance.NewPageAsync();

                // A real navigation (not SetContentAsync) is required here: CDP's setDocumentContent
                // doesn't reliably execute a ~2MB inline <script> the way a normal page load does, so the
                // shell is a small static file that pulls in tex-svg.js as a sibling <script src>.
                var shellPath = System.IO.Path.Combine(environment.Value.WebRootPath, "lib", "mathjax", "render-shell.html");
                _ = await page.GoToAsync($"file://{shellPath}");

                // Headless-shell has no compositor, so requestAnimationFrame -- which WaitForFunctionAsync
                // polls on by default, and PollingInterval alone doesn't override -- never fires and the
                // wait hangs forever. Poll manually instead; the condition itself is true within ~1s.
                var attempt = 0;
                var ready = false;
                while (!ready)
                {
                    ready = await page.EvaluateExpressionAsync<bool>(
                        "!!(window.MathJax && window.MathJax.startup && window.MathJax.startup.promise)");
                    if (ready)
                    {
                        break;
                    }

                    if (++attempt >= 100)
                    {
                        throw new TimeoutException("MathJax did not become ready within 10 seconds.");
                    }

                    await Task.Delay(100);
                }

                var rendered = await page.EvaluateFunctionAsync<string>(RenderScript, html);
                return new(OperationResult.Succeeded) { Data = rendered };
            }
            catch (Exception exc)
            {
                logger.Value.LogException(exc);
                return new(OperationResult.Failed) { Errors = [new() { Message = exc.Message },] };
            }
            finally
            {
                if (page is not null)
                {
                    await page.CloseAsync();
                }

                _ = renderLock.Release();
            }
        }

        public async ValueTask DisposeAsync()
        {
            if (browser is not null)
            {
                await browser.CloseAsync();
                browser.Dispose();
            }
            launchLock.Dispose();
            renderLock.Dispose();
        }

        private async Task<IBrowser> GetBrowserAsync()
        {
            if (browser is not null)
            {
                return browser;
            }

            await launchLock.WaitAsync();
            try
            {
                // Double-checked locking: a second caller may have already raced past the first null
                // check above while waiting on the lock. The analyzer can't reason about that cross-call
                // race, so it (wrongly) considers this check dead code.
#pragma warning disable CA1508 // browser is not always null here -- see comment above
                browser ??= await LaunchBrowserAsync();
#pragma warning restore CA1508
            }
            finally
            {
                _ = launchLock.Release();
            }

            return browser;
        }

        private static async Task<IBrowser> LaunchBrowserAsync()
        {
            // ChromeHeadlessShell (not full Chrome/Chromium) never draws a window, so it doesn't pull in
            // GTK/ATK/etc. -- keeps this working on minimal hosts that lack those UI-toolkit libraries.
            var fetcher = new BrowserFetcher(SupportedBrowser.ChromeHeadlessShell);
            var installedBrowser = await fetcher.DownloadAsync();

            return await Puppeteer.LaunchAsync(new LaunchOptions
            {
                Headless = true,
                ExecutablePath = installedBrowser.GetExecutablePath(),
                // Chromium's own sandbox needs kernel namespace features that aren't available in every
                // hosting/container environment; this page only ever renders our own vendored MathJax script
                // plus server-generated exam text (never third-party pages or attacker-controlled scripts),
                // so running without it here is the standard, low-risk headless-in-container trade-off.
                Args = ["--no-sandbox", "--disable-setuid-sandbox", "--disable-gpu"],
            });
        }

        private const string RenderScript = """
            async (html) => {
              const root = document.getElementById('root');
              root.innerHTML = html;
              await MathJax.typesetPromise([root]);

              const nodes = Array.from(root.querySelectorAll('mjx-container'));
              for (const node of nodes) {
                const svg = node.querySelector('svg');
                if (!svg) { continue; }

                const rect = svg.getBoundingClientRect();
                const width = Math.max(1, rect.width);
                const height = Math.max(1, rect.height);
                const svgString = new XMLSerializer().serializeToString(svg);
                const svgUrl = 'data:image/svg+xml;base64,' + btoa(unescape(encodeURIComponent(svgString)));

                const pngUrl = await new Promise((resolve, reject) => {
                  const img = new Image();
                  img.onload = () => {
                    const scale = 4;
                    const canvas = document.createElement('canvas');
                    canvas.width = Math.round(width * scale);
                    canvas.height = Math.round(height * scale);
                    const ctx = canvas.getContext('2d');
                    ctx.scale(scale, scale);
                    ctx.drawImage(img, 0, 0, width, height);
                    resolve(canvas.toDataURL('image/png'));
                  };
                  img.onerror = reject;
                  img.src = svgUrl;
                });

                const imgTag = document.createElement('img');
                imgTag.src = pngUrl;
                imgTag.setAttribute('width', Math.round(width));
                imgTag.setAttribute('height', Math.round(height));
                imgTag.setAttribute('style', 'vertical-align:middle;width:' + Math.round(width) + 'px;height:' + Math.round(height) + 'px');
                node.replaceWith(imgTag);
              }

              return root.innerHTML;
            }
            """;
    }
}
