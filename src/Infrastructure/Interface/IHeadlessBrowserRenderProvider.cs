namespace GamaEdtech.Infrastructure.Interface
{
    using System.Diagnostics.CodeAnalysis;

    using GamaEdtech.Common.Data;
    using GamaEdtech.Common.DataAnnotation;

    [Injectable]
    public interface IHeadlessBrowserRenderProvider
    {
        /// <summary>
        /// Scans <paramref name="html"/> for MathJax-style inline LaTeX (<c>$...$</c>) and replaces each
        /// formula with a rendered &lt;img&gt; tag containing a base64 PNG, leaving the rest of the markup untouched.
        /// </summary>
        Task<ResultData<string>> RenderFormulasAsync([NotNull] string html);

        /// <summary>
        /// Renders <paramref name="html"/> to a PDF using Chromium's native print-to-PDF engine, so PDF
        /// output matches real browser rendering (fonts, colors, embedded images) instead of an OOXML/PDF
        /// library's approximation of it.
        /// </summary>
        /// <param name="html">Body content to print.</param>
        /// <param name="headerHtml">
        /// Optional header template repeated on every page (Chromium's own mechanism, not part of
        /// <paramref name="html"/>) -- supports the <c>pageNumber</c>/<c>totalPages</c>/<c>date</c>/
        /// <c>title</c>/<c>url</c> classes Chromium injects automatically. Pass <see langword="null"/> for none.
        /// </param>
        /// <param name="footerHtml">Same mechanism as <paramref name="headerHtml"/>, for the page footer.</param>
        Task<ResultData<byte[]>> RenderPdfAsync([NotNull] string html, string? headerHtml = null, string? footerHtml = null);
    }
}
