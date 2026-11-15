namespace GamaEdtech.Data.Dto.PastPaper
{
    /// <summary>What one run of the past paper purchase copy (or link) did.</summary>
    public sealed class PastPaperPurchaseImportResultDto
    {
        /// <summary>gama-api payment rows read (every kind; only paid past paper files are kept).</summary>
        public int PaymentsRead { get; set; }

        /// <summary>Paid past paper files among them.</summary>
        public int Purchases { get; set; }

        public int Added { get; set; }

        public int Updated { get; set; }

        /// <summary>Ownership rows (<c>ContentPurchases</c>) created.</summary>
        public int Owned { get; set; }

        /// <summary>Purchases that matched an ownership row already there (the same file bought twice on gama-api, or
        /// an earlier run).</summary>
        public int AlreadyOwned { get; set; }

        /// <summary>Purchases still waiting: their buyer has no account here yet.</summary>
        public int WithoutUser { get; set; }

        /// <summary>Purchases still waiting: their paper (or extra file) is not copied (a test server's limit, or a
        /// deleted file).</summary>
        public int WithoutPaper { get; set; }
    }
}
