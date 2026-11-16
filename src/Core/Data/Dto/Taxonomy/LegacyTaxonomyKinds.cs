namespace GamaEdtech.Data.Dto.Taxonomy
{
    /// <summary>
    /// gama-api's lookup lists (<c>type_master</c> ids) that this backend copies, and the <c>types/list?type=</c> name of
    /// each. Only the lists the website, the past paper page, the MCP import and the board sync read are copied.
    /// </summary>
    public static class LegacyTaxonomyKinds
    {
        public const short Board = 1;
        public const short Grade = 2;
        public const short Subject = 4;
        public const short Course = 5;
        public const short Classification = 6;
        public const short Topic = 11;
        public const short Paper = 22;

        /// <summary>The type of a past paper's extra file (insert, audio, mark scheme...); its metadata holds the file's price
        /// multiplier.</summary>
        public const short ExtraFileType = 20;

        /// <summary>A past paper's variant (<c>nv</c>, 1, 2...); the paper's <c>variant</c> is one of these ids.</summary>
        public const short Variant = 29;

        /// <summary>The list name gama-api's <c>types/list</c> takes, by kind.</summary>
        public static readonly IReadOnlyDictionary<string, short> ByListName = new Dictionary<string, short>(StringComparer.Ordinal)
        {
            ["section"] = Board,
            ["base"] = Grade,
            ["lesson"] = Subject,
            ["course"] = Course,
            ["test_type"] = Classification,
            ["topic"] = Topic,
            ["exam_type"] = Paper,
            ["test_extra_file"] = ExtraFileType,
            ["variant"] = Variant,
        };
    }
}
