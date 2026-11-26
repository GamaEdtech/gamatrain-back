namespace GamaEdtech.Presentation.ViewModel.PastPaper
{
    using GamaEdtech.Common.DataAnnotation;

    /// <summary>One row of the past paper base price list.</summary>
    public sealed class PastPaperFilePriceViewModel
    {
        public long Id { get; set; }

        /// <summary>The file extensions it prices, lower case, comma-separated (<c>doc,docx</c>).</summary>
        public string? Extensions { get; set; }

        /// <summary>The base price before pages, referee score and multipliers.</summary>
        public int BasePrice { get; set; }

        /// <summary>When the row was last written (ISO 8601 UTC), by the gama-api copy or by an admin.</summary>
        public string? SyncDate { get; set; }
    }

    /// <summary>Adds or edits a base price row.</summary>
    public sealed class ManagePastPaperFilePriceRequestViewModel
    {
        /// <summary>One or more file extensions, comma-separated (<c>doc,docx</c>); each may be priced by one row only.</summary>
        [Display]
        [Required]
        public string? Extensions { get; set; }

        /// <summary>0 to 100000. 0 makes the extensions free, as deleting the row does.</summary>
        [Display]
        [Required]
        public int? BasePrice { get; set; }
    }
}
