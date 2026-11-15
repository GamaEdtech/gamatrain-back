namespace GamaEdtech.Data.Dto.PastPaper
{
    /// <summary>What one run of the past paper copy did.</summary>
    public sealed class PastPaperImportResultDto
    {
        public int PapersRead { get; set; }

        public int PapersAdded { get; set; }

        public int PapersUpdated { get; set; }

        /// <summary>Papers whose gama-api owner is not linked to a user here (yet).</summary>
        public int PapersWithoutUser { get; set; }

        public int ExtraFilesRead { get; set; }

        /// <summary>Extra files whose paper is not in gama-api's paper table; skipped.</summary>
        public int ExtraFilesWithoutPaper { get; set; }

        /// <summary>Files (question, answer, Word, extra) now recorded for the copied papers.</summary>
        public int FilesAdded { get; set; }

        public int FilesUpdated { get; set; }

        public int FilesRemoved { get; set; }
    }
}
