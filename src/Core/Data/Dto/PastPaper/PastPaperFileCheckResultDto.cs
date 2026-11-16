namespace GamaEdtech.Data.Dto.PastPaper
{
    /// <summary>What one check of the copied past paper files found.</summary>
    public sealed class PastPaperFileCheckResultDto
    {
        /// <summary>Files stored in the private bucket's <c>PastPaper</c> container.</summary>
        public int StoredFiles { get; set; }

        /// <summary><c>PastPaperFiles</c> rows checked.</summary>
        public int Rows { get; set; }

        /// <summary>Rows whose file is stored (their <c>Size</c> is now set).</summary>
        public int Found { get; set; }

        /// <summary>Rows whose file is not stored: the copy missed it, or gama-api never had it.</summary>
        public int Missing { get; set; }

        /// <summary>Stored files no row points to (left over from deleted papers or replaced uploads).</summary>
        public int Unused { get; set; }

        /// <summary>Up to 20 paths of missing files, to look into.</summary>
        public IReadOnlyList<string> MissingExamples { get; set; } = [];
    }
}
