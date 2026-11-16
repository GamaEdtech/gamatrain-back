namespace GamaEdtech.Data.Dto.PastPaper
{
    /// <summary>One file of a past paper as one user may download it: what it costs them and where it is stored.</summary>
    public sealed class PastPaperDownloadDto
    {
        public required long PaperId { get; init; }

        /// <summary>The file's <c>ContentPurchases</c> variant: <c>Question</c>, <c>QuestionWord</c>, <c>Answer</c> or
        /// <c>Extra:{id}</c> (gama-api's extra file id).</summary>
        public required string Variant { get; init; }

        /// <summary>Its price (gama-api's rule, <c>PastPaperPricing</c>); 0 is free.</summary>
        public required int Points { get; init; }

        /// <summary>The paper's owner or an admin: they download without paying and without counting a download.</summary>
        public required bool Privileged { get; init; }

        /// <summary>The file's key in the private bucket's <c>PastPaper</c> container.</summary>
        public required string Path { get; init; }

        /// <summary>The name the browser saves it under (gama-api's <c>{id}_{qp|qm|ms|attached}_Gamatrain.com.{ext}</c>).</summary>
        public required string DownloadName { get; init; }

        /// <summary>The paper's owner here, who earns the commission on a paid download; null when they have no account here.</summary>
        public long? OwnerUserId { get; init; }
    }
}
