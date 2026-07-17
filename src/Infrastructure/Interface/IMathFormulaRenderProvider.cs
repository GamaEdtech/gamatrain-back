namespace GamaEdtech.Infrastructure.Interface
{
    using System.Diagnostics.CodeAnalysis;

    using GamaEdtech.Common.Data;
    using GamaEdtech.Common.DataAnnotation;

    [Injectable]
    public interface IMathFormulaRenderProvider
    {
        /// <summary>
        /// Scans <paramref name="html"/> for MathJax-style inline LaTeX (<c>$...$</c>) and replaces each
        /// formula with a rendered &lt;img&gt; tag containing a base64 PNG, leaving the rest of the markup untouched.
        /// </summary>
        Task<ResultData<string>> RenderFormulasAsync([NotNull] string html);
    }
}
