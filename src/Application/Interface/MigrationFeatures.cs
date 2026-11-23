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

        /// <summary>Building exams over the REST API (<c>examBuilder</c>): the website's test maker and the exam bots. Its writes
        /// follow <c>Migration:WriteRoutes:Exams.TestMaker</c>.</summary>
        public const string TestMaker = "TestMaker";

        /// <summary>The public pages of the website (the cached reads it gets through this backend).</summary>
        public const string Website = "Website";

        /// <summary>Downloading a past paper's files (price, charge, the paid-file record and the file link). A write: it charges,
        /// so it follows <c>Migration:WriteRoutes</c> and is never compared.</summary>
        public const string Downloads = "Downloads";

        /// <summary>A teacher's own past papers: their list, a paper for the edit form, and (as a write route) adding, editing
        /// and deleting them.</summary>
        public const string Teacher = "Teacher";

        /// <summary>The paid exam export (PDF, Word, PowerPoint).</summary>
        public const string ExamExport = "ExamExport";

        /// <summary>The AI analysis of an exam result and the results page.</summary>
        public const string ExamAnalysis = "ExamAnalysis";

        /// <summary>Points for taking tests and exams.</summary>
        public const string Games = "Games";

        /// <summary>The job that copies gama-api's boards into this backend's own board list.</summary>
        public const string BoardSync = "BoardSync";

        /// <summary>The topic matches (<c>Types</c>): their copy from gama-api and their review here (approving links topics).</summary>
        public const string TopicMatches = "TopicMatches";
    }
}
