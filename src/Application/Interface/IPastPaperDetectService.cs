namespace GamaEdtech.Application.Interface
{
    using GamaEdtech.Common.Data;
    using GamaEdtech.Common.DataAnnotation;
    using GamaEdtech.Data.Dto.PastPaper;

    /// <summary>
    /// Reads a question paper's PDF for the add form: board, grade, subject, paper, variant, session and a title, from the
    /// cover page's printed codes, or from an AI model when the page has none it knows. See docs/business/past-papers.md,
    /// "Filling the form from the paper".
    /// </summary>
    [Injectable]
    public interface IPastPaperDetectService
    {
        /// <summary>What the PDF says about itself; <c>NotFound</c> when it has no readable text or nothing was recognised. With
        /// <paramref name="askAi"/> a cover without a known code is read by the AI model.</summary>
        Task<ResultData<PastPaperDetectionDto>> DetectAsync(byte[] pdf, bool askAi = true, CancellationToken cancellationToken = default);

        /// <summary>A PDF's upload answer (<c>[{"file": {...}}]</c>) with what it says about itself added to the item as
        /// <c>detected</c> (camelCase): what the file is, and for the question paper (<paramref name="question"/>) the paper's
        /// details too, with the AI model when needed. The answer as it was when the upload failed, the file isn't a PDF or
        /// nothing was recognised.</summary>
        Task<ResultData<System.Text.Json.Nodes.JsonNode>> WithDetectionAsync(ResultData<System.Text.Json.Nodes.JsonNode> upload, byte[] pdf, bool question, CancellationToken cancellationToken = default);
    }
}
