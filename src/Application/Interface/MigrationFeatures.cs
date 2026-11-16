namespace GamaEdtech.Application.Interface
{
    /// <summary>
    /// The callers of a migrating domain that can be switched on their own (<c>Migration:Routes:{Domain}.{Feature}</c>).
    /// A feature without its own setting follows its domain's.
    /// </summary>
    public static class MigrationFeatures
    {
        /// <summary>The MCP exam import.</summary>
        public const string Mcp = "Mcp";

        /// <summary>The public pages of the website (the cached reads it gets through this backend).</summary>
        public const string Website = "Website";

        /// <summary>The paid exam export (PDF, Word, PowerPoint).</summary>
        public const string ExamExport = "ExamExport";

        /// <summary>The AI analysis of an exam result and the results page.</summary>
        public const string ExamAnalysis = "ExamAnalysis";

        /// <summary>Points for taking tests and exams.</summary>
        public const string Games = "Games";

        /// <summary>The job that copies gama-api's boards into this backend's own board list.</summary>
        public const string BoardSync = "BoardSync";
    }
}
