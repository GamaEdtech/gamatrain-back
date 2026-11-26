namespace GamaEdtech.Data.Dto.PastPaper
{
    using System;

    /// <summary>One row of the past paper base price list (<c>PastPaperFilePrices</c>), as the admin sees it.</summary>
    public sealed class PastPaperFilePriceDto
    {
        public long Id { get; set; }

        /// <summary>The file extensions it prices, lower case, comma-separated (<c>doc,docx</c>).</summary>
        public required string Extensions { get; set; }

        /// <summary>The base price before pages, referee score and multipliers.</summary>
        public int BasePrice { get; set; }

        /// <summary>When the row was last written, by the gama-api copy or by an admin.</summary>
        public DateTimeOffset SyncDate { get; set; }
    }

    /// <summary>Adds (no <see cref="Id"/>) or edits a base price row.</summary>
    public sealed class ManagePastPaperFilePriceRequestDto
    {
        public long? Id { get; set; }
        public string? Extensions { get; set; }
        public int BasePrice { get; set; }
    }
}
